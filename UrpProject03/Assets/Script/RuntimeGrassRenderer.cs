using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering; 

public class RuntimeGrassRenderer : MonoBehaviour
{
    public GrassDataAsset grassData;
    public Mesh[] grassMeshes;
    public Material[] grassMaterials;
    public GameObject player;
    public float PushRadius;

    private ComputeBuffer grassBuffer;
    private ComputeBuffer argsBuffer;
    private Bounds renderBounds;

    private ComputeBuffer visibleIndexBuffer;   // 存可见草的 index
    private uint[] visibleIndices;               // CPU 临时数组
    private Plane[] cachedPlanes = new Plane[6];
    
    private int visibleGrassCount;

    public float MinHeight = 1.0f;
    public float MaxHeight = 1.0f;
    public float MaxDistSq = 1000;

    void Start()
    {
        if (grassData == null)
        {
            Debug.LogError("GrassData asset not assigned");
            return;
        }

        InitializeBuffers();
        Debug.Log($"Baked 数据总条数：{grassData.grassInstances.Length}");
    }

    void InitializeBuffers()
    {
        // 确保有草数据
        if (grassData.grassInstances == null || grassData.grassInstances.Length == 0)
        {
            Debug.LogError("No grass instances in GrassData asset");
            return;
        }

        // 创建缓冲区
        int maxGrass = grassData.grassInstances.Length;
        grassBuffer = new ComputeBuffer(maxGrass, 32, ComputeBufferType.Structured); // 32 bytes per grass

        // 关键：将草数据设置到缓冲区
        grassBuffer.SetData(grassData.grassInstances);

        // 创建参数缓冲区
        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);

        // 设置渲染边界 - 确保边界包含所有草
        renderBounds = new Bounds(transform.position, Vector3.one * 100);//new Bounds(grassData.boundsCenter, grassData.boundsSize);
        Debug.Log($"Bounds: Center={renderBounds.center}, Size={renderBounds.size}");

        //Material material = grassMaterials[0];

        visibleIndexBuffer = new ComputeBuffer(maxGrass, 4, ComputeBufferType.Append);
        visibleIndices = new uint[maxGrass];

        visibleNative = new NativeList<int>(maxGrass, Allocator.Persistent);

        //selectedMaterialId = (grassData.grassInstances.variationData >> 16) & 0xFF; 

        //UpdateDrawArgsBuffer();
        uint[] args = new uint[5] {
            grassMeshes[0].GetIndexCount(0),   // 固定
            0,                       // 实例数由 CopyCount 动态改写
            grassMeshes[0].GetIndexStart(0),
            grassMeshes[0].GetBaseVertex(0),
            0
        };
        argsBuffer.SetData(args);
    }



    void PerformCulling()
    {
        Camera cam = Camera.main;
        if (!cam) return;

        // 把视锥 6 个面拿到
        GeometryUtility.CalculateFrustumPlanes(cam, cachedPlanes);

        int write = 0;
        var camPos = cam.transform.position;

        for (uint i = 0; i < grassData.grassInstances.Length; i++)
        {
            Vector3 world = transform.TransformPoint(grassData.grassInstances[i].position);
            if (Vector3.SqrMagnitude(world - camPos) > 100f * 100f)   // 距离平方，省掉 sqrt
                continue;

            Bounds b = new Bounds(world, Vector3.one * grassData.grassInstances[i].scale);
            if (!GeometryUtility.TestPlanesAABB(cachedPlanes, b))
                continue;

            visibleIndices[write++] = i;   // 记录可见索引
        }

        visibleIndexBuffer.SetData(visibleIndices, 0, 0, write);

        // 告诉 GPU 要画多少实例
        visibleGrassCount = write;
        UpdateDrawArgsBuffer();
    }

    public ComputeShader cullCompute;

    Vector4[] GetFrustumPlanes(Camera cam)
{
    Plane[] p = GeometryUtility.CalculateFrustumPlanes(cam);
    Vector4[] planes = new Vector4[6];
    for (int i = 0; i < 6; ++i)
        planes[i] = new Vector4(p[i].normal.x, p[i].normal.y, p[i].normal.z, p[i].distance);
    return planes;
}

    void CullOnGpu()
    {
        Camera cam = Camera.main;
        int kernel = cullCompute.FindKernel("ComputeGrassV4");
        cullCompute.SetBuffer(kernel, "_AllGrass", grassBuffer);
        cullCompute.SetBuffer(kernel, "_VisibleIndices", visibleIndexBuffer);
        cullCompute.SetBuffer(kernel, "_Args", argsBuffer);  // 用于 CopyCount
        cullCompute.SetVectorArray("_Planes", GetFrustumPlanes(cam));
        cullCompute.SetVector("_CamPos", cam.transform.position);
        cullCompute.SetFloat("_MaxDistSq", MaxDistSq * MaxDistSq);
        cullCompute.SetInt("_TotalCount", grassData.grassInstances.Length);
        cullCompute.SetMatrix("_ObjectToWorld", transform.localToWorldMatrix);
        //cullCompute.SetInt("_VisibleCount", visibleGrassCount);

        visibleIndexBuffer.SetCounterValue(0);                         // 重置 Append
        int groups = Mathf.CeilToInt(grassData.grassInstances.Length / 64f);
        cullCompute.Dispatch(kernel, groups, 1, 1);

        // 把可见数量写进 argsBuffer
        ComputeBuffer.CopyCount(visibleIndexBuffer, argsBuffer, 4);
    }

    NativeList<int> visibleNative;

void PerformCullingJob()
{
    Camera cam = Camera.main;
    if (!cam) return;

        // 生成托管数组
    var localToWorld = transform.localToWorldMatrix;   // 这是 Matrix4x4
    Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);

// 直接拷成 float4
    NativeArray<float4> frustum = new NativeArray<float4>(6, Allocator.TempJob);
    for (int i = 0; i < 6; ++i) {
        frustum[i] = new float4(-planes[i].normal, -planes[i].distance);
    }

    // 修复2：预设NativeList容量
    visibleNative.Clear();
    visibleNative.Capacity = grassData.grassInstances.Length; // 关键！

    // 修复3：正确的矩阵构造
    var mat = new float4x4(
        localToWorld.GetColumn(0),
        localToWorld.GetColumn(1),
        localToWorld.GetColumn(2),
        localToWorld.GetColumn(3)
    );

    var job = new FrustumCullingJob
    {
        grass = new NativeArray<GrassDataAsset.BakedGrassData>(grassData.grassInstances, Allocator.TempJob),
        planes = frustum,
        camPos = cam.transform.position,
        maxDistSq = 500f * 500f,
        visible = visibleNative.AsParallelWriter(),
        localToWorld = mat
    };

    var handle = job.Schedule(grassData.grassInstances.Length, 64);
    handle.Complete();

    visibleIndexBuffer.SetData(visibleNative.AsArray());
    visibleGrassCount = visibleNative.Length;
    UpdateDrawArgsBuffer();

    job.grass.Dispose();
    visibleNative.Clear();
}

    // 添加此方法更新绘制参数
    void UpdateDrawArgsBuffer()
    {
        if (grassMeshes == null || grassMeshes.Length == 0) return;

        Mesh mesh = grassMeshes[0];
        uint[] args = new uint[5] {
            mesh.GetIndexCount(0),
            (uint)visibleGrassCount,
            mesh.GetIndexStart(0),
            mesh.GetBaseVertex(0),
            0
        };
        argsBuffer.SetData(args);

        Debug.Log("instance num is" + args[1]);
        Debug.Log("bounds is" + transform.position);
    }

    void Update()
    {
        if (grassData == null || grassBuffer == null) return;

        //PerformCulling();
        //PerformCullingJob();
        CullOnGpu();
        RenderGrass();

        
        //Debug.Log($"Rendered {visibleGrassCount} grass instances");

        //long gpuBytes = grassBuffer.count * grassBuffer.stride;
        //Debug.Log($"GPU 显存占用: {gpuBytes / 1024f:F2} KB");
    }

    // 调试方法：绘制边界框
    void DebugDrawBounds()
    {
        Debug.DrawLine(renderBounds.min, new Vector3(renderBounds.max.x, renderBounds.min.y, renderBounds.min.z), Color.red);
        Debug.DrawLine(renderBounds.min, new Vector3(renderBounds.min.x, renderBounds.max.y, renderBounds.min.z), Color.green);
        Debug.DrawLine(renderBounds.min, new Vector3(renderBounds.min.x, renderBounds.min.y, renderBounds.max.z), Color.blue);

        Debug.DrawLine(renderBounds.max, new Vector3(renderBounds.min.x, renderBounds.max.y, renderBounds.max.z), Color.red);
        Debug.DrawLine(renderBounds.max, new Vector3(renderBounds.max.x, renderBounds.min.y, renderBounds.max.z), Color.green);
        Debug.DrawLine(renderBounds.max, new Vector3(renderBounds.max.x, renderBounds.max.y, renderBounds.min.z), Color.blue);
    }

    void RenderGrass()
    {

        if (//visibleGrassCount == 0 ||
            grassMeshes == null || grassMeshes.Length == 0 ||
            grassMaterials == null || grassMaterials.Length == 0)
        {
            Debug.LogWarning("Cannot render grass - missing components");
            return;
        }

        Mesh mesh = grassMeshes[0];
        Material material = grassMaterials[0];

        if (player != null)
            material.SetVector("_PlayerPos", player.transform.position);
        else
            material.SetVector("_PlayerPos", Vector4.zero);

        // 确保材质支持GPU实例化
        if (!material.enableInstancing)
        {
            Debug.LogWarning("Material does not have instancing enabled. Forcing enable.");
            material.enableInstancing = true;
        }

        // 设置材质参数


        // 重要：使用正确的变换矩阵
        material.SetMatrix("_ObjectToWorld", transform.localToWorldMatrix);
        material.SetBuffer("_GrassDataBuf", grassBuffer);
        material.SetBuffer("_VisibleIndices", visibleIndexBuffer);
        material.EnableKeyword("USE_CULLING");
        Vector4 playerPos = Vector4.zero;
        float radius = 0f;          // 0 = 关闭交互

        if (player != null)
        {
            playerPos = player.transform.position;
            radius = PushRadius;
        }

        material.SetVector("_PlayerPos", playerPos);
        material.SetFloat("_Radius", radius);

        material.SetFloat("_MinHeight", MinHeight);
        material.SetFloat("_MaxHeight", MaxHeight);


        material.SetFloat("_Height", 1.0f);

        // 渲染
        Graphics.DrawMeshInstancedIndirect(mesh, 0, material, renderBounds, argsBuffer);
        

        //Debug.Log($"Rendered {visibleGrassCount} grass instances");
    }

    void OnDisable()
    {
        grassBuffer?.Release();
        argsBuffer?.Release();

        visibleIndexBuffer?.Release();
        visibleNative.Dispose();
    }

    // 添加OnDrawGizmos以可视化草位置
    void OnDrawGizmosSelected()
    {
        /* if (grassData == null || grassData.grassInstances == null) return;

         Gizmos.color = Color.green;
         Matrix4x4 originalMatrix = Gizmos.matrix;
         Gizmos.matrix = transform.localToWorldMatrix;

         foreach (var grass in grassData.grassInstances)
         {
             Gizmos.DrawWireSphere(grass.position, 0.1f);
         }

         Gizmos.matrix = originalMatrix;*/
    }
    

    [BurstCompile]
    struct FrustumCullingJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float4> planes; // 6 planes
        [ReadOnly] public NativeArray<GrassDataAsset.BakedGrassData> grass;
        [ReadOnly] public float3 camPos;
        [ReadOnly] public float maxDistSq;
         [ReadOnly] public float4x4 localToWorld;
        [WriteOnly] public NativeList<int>.ParallelWriter visible;

        public void Execute(int i)
        {
            float3 p = math.transform(localToWorld, grass[i].position);
            //if (math.distancesq(p, camPos) > maxDistSq) return;

            // 修复4：正确的平面测试
            float4 homoPoint = new float4(p, 1);
            for (int k = 0; k < 6; ++k)
            {
                float d = math.dot(planes[k], homoPoint);
                if (d >- grass[i].scale) return;
            }
            visible.AddNoResize(i);
        }
    }
}