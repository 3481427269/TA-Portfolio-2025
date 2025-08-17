using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;
using Unity.Burst;
using Unity.Mathematics;
using Random = UnityEngine.Random;
using Unity.Collections.LowLevel.Unsafe;
using System.Threading;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public class GrassPainV4 : MonoBehaviour
{
    [Header("资源")]
    public Mesh[] grassMesh;
    public Material[] grassMaterial;
    //public ComputeShader computeShader;

    public LayerMask paintLayer;

    public float brushRadius = 1.0f;
    public float Density = 2.0f;

    //public int seed;
    public float scale;
    private float Height = 1.0f;
    public float MinHeight = 1.0f;
    public float MaxHeight = 2.0f;
    [SerializeField] private int selectedMeshId = 0;
    [SerializeField] private int selectedMaterialId = 0;
    public int maxGrassCount = 1 << 20;

    [System.Serializable]
    [HideInInspector]
    public struct GrassData
    {
        public Vector3 pos;//12//位置
        public float scale;//4//统一缩放
        public Vector3 rot;//12//旋转
        public uint data;//4Byte 32位//高8位mesh类型 中8位材质类型  低16位随机种子

        //public uint typeId;//4//Mesh类型
        //public uint materialId;//4//材质类型
        //public uint seed;//4//随机种子
    }

    [SerializeField][HideInInspector] public List<GrassData> grassList = new List<GrassData>();
    private ComputeBuffer grassDataBuf;
    private ComputeBuffer drawArgsBuf;
    //private ComputeBuffer matBuf;
    private int grassCount;

    private bool isDirty = true;
  

    void ReleaseBuffer()
    {
        grassDataBuf?.Release();
        drawArgsBuf?.Release();
        //matBuf?.Release();

        grassDataBuf = null;
        drawArgsBuf = null;
        //matBuf = null;

    }
    private void Initialize()
    {
        if (grassMesh == null || grassMaterial == null  || maxGrassCount < 1)
        {
            Debug.LogWarning("missing reference or maxGrassCount < 0");
            return;
        }

        ReleaseBuffer();

        selectedMeshId = 0;
        selectedMaterialId = 0;


        if (grassList.Count == 0)
        {
            uint meshId = (uint)selectedMeshId & 0xFF;
            uint materialId = (uint)selectedMaterialId & 0xFF;
            uint seedId = (uint)Random.Range(0, 0x10000);

            uint newData =
            (meshId << 24) |
            (materialId << 16) |
            (seedId << 0);
            grassList.Add(new GrassData
            {
                pos = new Vector3(0, 0, 0),
                rot = new Vector3(0, 0, 0),
                scale = scale,
                data = newData
            });
        }

        grassCount = grassList.Count;
        grassDataBuf = new ComputeBuffer(maxGrassCount, 32, ComputeBufferType.Structured);
        grassDataBuf.SetData(grassList);

        drawArgsBuf = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        UpdateDrawArgsBuffer(grassCount);

        //matBuf = new ComputeBuffer(maxGrassCount, 16);

    }
    void OnEnable()
    {
        Initialize();
    }
    void OnDisable()
    {
        // 其他资源的释放...
        ReleaseBuffer();
    }

    void Start()
    {
        Initialize();
    }

    private void RenderGrass()
    {
        if (grassDataBuf == null || drawArgsBuf == null)
        {
            Initialize();
        }

        if (selectedMeshId < 0 || selectedMeshId >= grassMesh.Length || grassMesh[selectedMeshId] == null)
        {
            Debug.LogError($"Invalid mesh index: {selectedMeshId}");
            return;
        }
        if (selectedMaterialId < 0 || selectedMaterialId >= grassMaterial.Length || grassMaterial[selectedMaterialId] == null)
        {
            Debug.LogError($"Invalid material index: {selectedMaterialId}");
            return;
        }

        Camera currentCamera = Camera.main;

        Matrix4x4 proj = GL.GetGPUProjectionMatrix(currentCamera.projectionMatrix, true);
        Matrix4x4 vp = currentCamera.projectionMatrix * currentCamera.worldToCameraMatrix;

        grassMaterial[selectedMaterialId].SetBuffer("_GrassDataBuf", grassDataBuf);
        grassMaterial[selectedMaterialId].SetMatrix("_ObjectToWorld", transform.localToWorldMatrix);
        grassMaterial[selectedMaterialId].SetFloat("_Height", Height);
        grassMaterial[selectedMaterialId].SetFloat("_MinHeight", MinHeight);
        grassMaterial[selectedMaterialId].SetFloat("_MaxHeight", MaxHeight);
        grassMaterial[selectedMaterialId].DisableKeyword("USE_CULLING");

        Bounds bounds = new Bounds(transform.position, Vector3.one * 100);//new Bounds(transform.position, Vector3.one * 100f);
        Graphics.DrawMeshInstancedIndirect(grassMesh[selectedMeshId], 0, grassMaterial[selectedMaterialId], bounds, drawArgsBuf);

    }

    private float dt = 0f;
    private float fps = 0f;
    private float minFps = 999f;
    private float maxFps = 0f;
    // Update is called once per frame

    void Update()
    {
        /*dt += Time.unscaledDeltaTime;
        if (dt >= 1f)
        {
            fps = 1f / Time.unscaledDeltaTime;
            minFps = Mathf.Min(minFps, fps);
            maxFps = Mathf.Max(maxFps, fps);
            Debug.Log($"FPS: cur={fps:F1}  min={minFps:F1}  max={maxFps:F1}");
            dt = 0f;
        }*/

        if (Application.isPlaying)
        {
        }

        // 6. 渲染
        
        {
            RenderGrass();
        }
        //Debug.Log("count = " + grassCount);
        //Debug.Log($"草量:{grassList.Count}  buffer空:{grassDataBuf==null}  ");
    }

    private void UpdateDrawArgsBuffer(int instanceCount)
    {
        if (drawArgsBuf == null)
        {
            drawArgsBuf = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        }

        uint[] args = new uint[5];
        args[0] = grassMesh[selectedMeshId].GetIndexCount(0); // 索引数
        args[1] = (uint)instanceCount; // 实例数量
        args[2] = grassMesh[selectedMeshId].GetIndexStart(0);
        args[3] = grassMesh[selectedMeshId].GetBaseVertex(0);
        args[4] = 0; //实例偏移

        drawArgsBuf.SetData(args);
        Debug.Log($"实例数:{args[1]}");
    }

    public void AddPosition(Vector3 pos)
    {

        if (grassDataBuf == null || drawArgsBuf == null)
            Initialize();
        if (grassCount > maxGrassCount) return;

        uint meshId = (uint)selectedMeshId & 0xFF;
        uint materialId = (uint)selectedMaterialId & 0xFF;
        uint seedId = (uint)Random.Range(0, 0x10000);

        Vector3 localPos = transform.InverseTransformPoint(pos);
        localPos.y = 0.0f;
        localPos += new Vector3(Random.Range(-0.5f,0.5f), 0f, Random.Range(-0.5f,0.5f));
        Debug.Log($"世界坐标: {pos} -> 局部坐标: {localPos}");

        uint newData =
        (meshId << 24) |
        (materialId << 16) |
        (seedId << 0);
        grassList.Add(new GrassData
        {
            pos = localPos,
            rot = new Vector3(0, Random.Range(0, 180), 0),
            scale = scale,
            data = newData
        });

        grassCount = grassList.Count;
        grassDataBuf.SetData(grassList, grassCount - 1, grassCount - 1, 1); // 只传有效长度

        UpdateDrawArgsBuffer(grassCount);

        /*int kernelIndex = computeShader.FindKernel("ComputeGrassV4");
        computeShader.SetBuffer(kernelIndex, "_GrassDataBuf", grassDataBuf);
        computeShader.SetBuffer(kernelIndex, "_DrawArgsBuf", drawArgsBuf);

        int group = Mathf.CeilToInt(grassCount / 128f);
        computeShader.Dispatch(kernelIndex, group, 1, 1);*/
        isDirty = true;
    }

    public void RemoveGrassAtPosition(Vector3 worldPosition)
    {
        if (grassList.Count < 1) return;
        int removedCount = 0;

        // 将世界坐标转换为局部坐标
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);

        // 计算局部空间中的半径（考虑物体的缩放）
        float localRadius = brushRadius / Mathf.Max(
            transform.lossyScale.x,
            transform.lossyScale.y,
            transform.lossyScale.z
        );

        // 从后往前遍历避免索引问题
        for (int i = grassList.Count - 1; i >= 0; i--)
        {
            // 在局部空间中计算距离
            float distance = Vector3.Distance(grassList[i].pos, localPosition);
            if (distance <= localRadius)
            {
                grassList.RemoveAt(i);
                removedCount++;
            }
        }

        grassCount = grassList.Count;

        grassDataBuf.SetData(grassList, 0, 0, grassCount); // 只传有效长度
        UpdateDrawArgsBuffer(grassCount);
        isDirty = true;
    }
    

// 添加清除方法
public void ClearAllGrass()
{
    if (grassList.Count == 0) return;
    
    Debug.Log($"Clearing all grass ({grassList.Count} instances)");
    
    // 清空数据
    grassList.Clear();
    grassCount = 0;
    
        {
            uint meshId = (uint)selectedMeshId & 0xFF;
            uint materialId = (uint)selectedMaterialId & 0xFF;
            uint seedId = (uint)Random.Range(0, 0x10000);

            uint newData =
            (meshId << 24) |
            (materialId << 16) |
            (seedId << 0);
            grassList.Add(new GrassData
            {
                pos = new Vector3(0, 0, 0),
                rot = new Vector3(0, 0, 0),
                scale = scale,
                data = newData
            });
        }
    
    grassCount = grassList.Count;
    grassDataBuf = new ComputeBuffer(maxGrassCount, 32, ComputeBufferType.Structured);
    grassDataBuf.SetData(grassList);

    drawArgsBuf = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
    UpdateDrawArgsBuffer(grassCount);
    
    isDirty = true;
}

    [Header("Baking")]
    public bool autoSaveAfterPaint = false;
    public GrassDataAsset grassDataAsset;
    
    public void BakeToAsset()
    {
        if (grassDataAsset == null)
        {
            Debug.LogError("No GrassDataAsset assigned for baking");
            return;
        }
        
        // 转换数据格式
        var bakedData = new GrassDataAsset.BakedGrassData[grassList.Count];
        for (int i = 0; i < grassList.Count; i++)
        {
            bakedData[i] = new GrassDataAsset.BakedGrassData
            {
                position = grassList[i].pos,
                scale = grassList[i].scale,
                rotation = grassList[i].rot,
                variationData = grassList[i].data
            };
        }
        
        // 更新资源
        grassDataAsset.grassInstances = bakedData;

        // 计算边界
        //Bounds bounds = CalculateBounds();
        //grassDataAsset.boundsCenter = bounds.center;
        //grassDataAsset.boundsSize = bounds.size;
        
        // 标记资源为已修改
#if UNITY_EDITOR
        EditorUtility.SetDirty(grassDataAsset);
        AssetDatabase.SaveAssets();
#endif
        
        Debug.Log($"Baked {grassList.Count} grass instances to asset");
    }
    
    private Bounds CalculateBounds()
    {
        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        foreach (var grass in grassList)
        {
            bounds.Encapsulate(transform.TransformPoint(grass.pos));
        }
        return bounds;
    }

    
}
