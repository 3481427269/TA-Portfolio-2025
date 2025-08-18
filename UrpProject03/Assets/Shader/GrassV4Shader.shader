Shader "Unlit/GrassV4"
{
    Properties
    {
        _DownCol("_DownCol", Color) = (0,0,0,0)
        _SpecularCol("_SpecularCol", Color) = (1,1,1,1)
        _ButtomCol("_ButtomCol", Color) = (0,0,0,0)
        _UpCol("_UpCol", Color) = (1,1,1,1)
        _Strength("Strength", float) = 1
        _WindSpeed("_WindSpeed", float) = 1.0
        _MeshScale("_Scale", float) = 5
        
    }
    SubShader
    {
         Tags 
            { 
                "RenderPipeline"="UniversalPipeline"
                "LightMode"="ForwardOnly"
            }
        
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="SRPDefaultUnlit" }
           
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            //#pragma multi_compile_instancing
            
            #pragma multi_compile _ USE_CULLING 

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
           
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;
                //UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 normalWS : TEXCOORD2;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD0;
                float fogFactor : TEXCOORD3;
                float4 screenPos : TEXCOORD4;
                float4 positionOS : TEXCOORD5;
                //int instanceID : TEXCOORD6;
                //UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
                UNITY_DEFINE_INSTANCED_PROP(float4, _DownCol)
                UNITY_DEFINE_INSTANCED_PROP(float4, _UpCol)
                UNITY_DEFINE_INSTANCED_PROP(float4, _ButtomCol)
                UNITY_DEFINE_INSTANCED_PROP(float4, _SpecularCol)
                UNITY_DEFINE_INSTANCED_PROP(float, _PushRadius)
                UNITY_DEFINE_INSTANCED_PROP(float, _Strength)
                UNITY_DEFINE_INSTANCED_PROP(float, _WindSpeed)
                UNITY_DEFINE_INSTANCED_PROP(float, _MeshScale)
            UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)

            #define _DownCol     UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _DownCol)
            #define _UpCol       UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _UpCol)
            #define _ButtomCol   UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _ButtomCol)
            #define _SpecularCol UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _SpecularCol)
            #define _PushRadius UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _PushRadius)
            #define _Strength UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Strength)
            #define _WindSpeed UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _WindSpeed)
            #define _MeshScale UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _MeshScale)

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_CameraDepthTexture);  //MetalMap
            SAMPLER(sampler_CameraDepthTexture);

            struct GrassData
            {
                float3 pos;
                float scale;
                float3 rot;
                uint data;
            };

            //StructuredBuffer<float4x4> _MatsOut;
            //StructuredBuffer<GrassData> _GrassDataBuf;
            StructuredBuffer<uint> _VisibleIndices;
            //float4 _PlayerPos;

            
            // 创建模型矩阵
            /*float4x4 CreateModelMatrix(float3 pos, float3 rot, float scl, float h)
            {
                // 转换为弧度
                float3 rad = radians(rot);
                
                // 提取旋转分量
                float cx = cos(rad.x), sx = sin(rad.x);
                float cy = cos(rad.y), sy = sin(rad.y);
                float cz = cos(rad.z), sz = sin(rad.z);
                
                // 构建旋转矩阵 - 优化顺序为ZXY
                float4x4 rotMatrix = float4x4(
                    cy*cz - sx*sy*sz, -cx*sz, sy*cz + sx*cy*sz, 0,
                    cy*sz + sx*sy*cz, cx*cz, sy*sz - sx*cy*cz, 0,
                    -cx*sy,          sx,     cx*cy,            0,
                    0, 0, 0, 1
                );
                
                // 缩放矩阵
                float4x4 scaleMatrix = float4x4(
                    scl, 0, 0, 0,
                    0, h, 0, 0,
                    0, 0, scl, 0,
                    0, 0, 0, 1
                );
                
                // 平移矩阵
                float4x4 transMatrix = float4x4(
                    1, 0, 0, pos.x,
                    0, 1, 0, pos.y,
                    0, 0, 1, pos.z,
                    0, 0, 0, 1
                );
                
                // 组合变换: T * R * S
                return mul(transMatrix, mul(rotMatrix, scaleMatrix));
            }*/

            float rand(float2 seed) {
                return frac(sin(dot(seed.xy, float2(12.9898, 78.233))) * 43758.5453);
            }

            // 低频 2D 噪声（可当作世界风场）
            float LowFreqNoise(float2 p, float time, float speed, float scale)
            {
                // 让相位随时间缓慢滚动
                float phase = time * speed;

                // 两层低频正弦叠加，生成“丘陵”感
                float n  = sin(p.x * scale + phase) * 0.5 + 0.5;
                n       += sin(p.y * scale * 0.7 + phase * 0.8) * 0.5 + 0.5;

                return n * 0.5;               // 归一化到 0~1
            }

            #define PI 3.14159265358979323846

            float3 CalculateWindEffect(float id)
            {
                // 确保风向向量是单位向量
                float3 windDir = normalize(float3(1, 0, 1));//normalize(_WindDirection);
                
                // 使用唯一的相位偏移
                float phaseOffset = rand(float2(id, 0)) * 2 * PI;
                
                // 使用更自然的风场函数
                float timeFactor = _Time.x * 5 *_WindSpeed;
                float frequency = lerp(0.8, 1.5, rand(float2(id, 0)));
                float amplitude = 1;//_WindStrength * _BendIntensity * blend * 0.5;
                
                // 基础摆动 - 主要影响XZ平面
                float mainSwing = sin(timeFactor * frequency + phaseOffset) * amplitude ;
                
                // 次要摆动 - 创造更自然的效果
                float secondarySwing = cos(timeFactor * frequency * 1.7 + phaseOffset) * amplitude * 0.3;
            
                // 组合风场效果
                float3 windEffect = windDir * mainSwing ;
                windEffect += float3(windDir.z, 0, -windDir.x) * secondarySwing;
                
                // 增加顶部摆动幅度
                windEffect.y = abs(windEffect.x + windEffect.z) ;
                
                return windEffect;
            }

            /*float _Radius;

             // 工具函数：绕轴旋转矩阵
            float3x3 AngleAxis3x3(float angle, float3 axis)
            {
                float c = cos(angle), s = sin(angle), omc = 1 - c;
                float x = axis.x, y = axis.y, z = axis.z;
                return float3x3(
                    c + x*x*omc,     x*y*omc - z*s,   x*z*omc + y*s,
                    y*x*omc + z*s,   c + y*y*omc,     y*z*omc - x*s,
                    z*x*omc - y*s,   z*y*omc + x*s,   c + z*z*omc
                );
            }
            //========== 顶点着色器里调用 ==============
            void ApplyPlayerInteraction(inout float3 worldPos, inout float3 normal)
            {
                float3 toPlayer = _PlayerPos.xyz - worldPos;
                toPlayer.y = 0;                       // 只在 XZ 平面计算
                float dist = length(toPlayer);

                float r = _Radius;       
                if(r==0)return;             // 交互半径，脚本设置
                if (dist < r)
                {
                    // 1. 衰减系数：二次曲线
                    float falloff = 1.0 - saturate(dist / r);
                    falloff *= falloff;

                    // 2. 弯曲轴与角度
                    float3 bendAxis = normalize(float3(-toPlayer.z, 0, toPlayer.x)); // 垂直于 toPlayer
                    float  bendAngle = 55.0 * 0.0174533 * falloff;                 // 55° -> 弧度

                    // 3. 旋转顶点 & 法线
                    float3x3 rotMat = AngleAxis3x3(bendAngle, bendAxis);
                    worldPos = mul(rotMat, worldPos - _PlayerPos.xyz) + _PlayerPos.xyz;
                    normal   = mul(rotMat, normal);

                    // 4. 轻微压缩（高度方向）
                    worldPos.y *= lerp(1.0, 0.7, falloff);
                }
            }*/

            float4x4 _ObjectToWorld;
            //float _Height, _MinHeight, _MaxHeight;
            StructuredBuffer<float4x4> _MatsOut;
            //float4x4 _CamVP;

            v2f vert (appdata v, uint instanceID : SV_InstanceID)
            {
                 v2f o;
                //UNITY_SETUP_INSTANCE_ID(v);
                //UNITY_TRANSFER_INSTANCE_ID(v, o);

                uint realInstanceID;
                #ifdef USE_CULLING
                    realInstanceID = _VisibleIndices[instanceID];
                #else
                    realInstanceID = instanceID;
                #endif

                //uint seed =  _GrassDataBuf[realInstanceID].data  & 0xFFFFu;//_GrassDataBuf[realInstanceID].data  & 0xFFFFu; // 低 16 位
                //float height = lerp(_MinHeight, _MaxHeight, rand(float2(realInstanceID, 0)));

                float4x4 transform = _MatsOut[realInstanceID];
                float4 localPosInParent = mul(transform, v.vertex);
                float4 wPos = mul(_ObjectToWorld, localPosInParent); 
                
                /*float4x4 grassModelMatrix = CreateModelMatrix(
                    _GrassDataBuf[realInstanceID].pos,
                    _GrassDataBuf[realInstanceID].rot,
                    _GrassDataBuf[realInstanceID].scale,
                    height);
                
                // 2. 将顶点从草模型空间 -> 父对象局部空间
                float4 localPosInParent = mul(grassModelMatrix, float4(v.vertex.xyz, 1.0));
                
                // 3. 应用父对象的变换：局部空间 -> 世界空间
                float4 wPos = mul(_ObjectToWorld, localPosInParent); 
                wPos = worldPos;

                //float noise = LowFreqNoise(wPos.xz , _Time.y * 3, 1, 7);*/
                float worldWave = sin(_Time.x * _WindSpeed+ wPos.x *1.0f/ _MeshScale  + wPos.z *1/ _MeshScale);
                worldWave *= localPosInParent.y * _Strength;// _WorldAmplitude *      // 大振幅

                // 2. 植株随机波（高频、小振幅）
                float3 plantWave = CalculateWindEffect(realInstanceID ) * localPosInParent.y * 1 ;//_PlantAmplitude ;

                // 3. 叠加
                wPos.xz += worldWave ;//* float2(1, 1);   // 整体起伏
                wPos.xz += plantWave.xz;               // 每根草微摆

                o.normalWS = normalize(TransformObjectToWorldNormal(v.normal));

                //ApplyPlayerInteraction(wPos.xyz, o.normalWS);

                o.vertex    = mul(UNITY_MATRIX_VP, wPos);        // 世界->裁剪
                o.positionWS = wPos.xyz;
                o.positionOS = v.vertex;
                
                // 转换法线到世界空间
                o.uv = v.uv;
                //o.instanceID = instanceID;
                o.screenPos = ComputeScreenPos(o.vertex);
                
                // 计算雾效因子
                o.fogFactor = realInstanceID;//ComputeFogFactor(o.vertex.z);
                
                return o;
            }

    
            half4 frag (v2f i) : SV_Target
            {
                //UNITY_SETUP_INSTANCE_ID(i);
                
                half3 col = lerp(_DownCol, _UpCol, i.uv.y);
                col *= lerp(_ButtomCol, 1, i.uv.y);
                float u = smoothstep(0.9, 1.0, i.uv.y);
                col += lerp(col, _SpecularCol, u);
                            
                return half4(col.xyz, 1);
                //return i.fogFactor;
                //return float4(frac(i.instanceID * 0.1), frac(i.instanceID * 0.2), 1, 1);
                //return worldWave;
            }
            ENDHLSL
        }
        
        
    }
}