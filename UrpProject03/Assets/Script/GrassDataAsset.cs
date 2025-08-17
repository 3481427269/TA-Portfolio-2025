using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GrassData", menuName = "Terrain/Grass Data")]
public class GrassDataAsset : ScriptableObject
{
    [System.Serializable]
    public struct BakedGrassData
    {
        public Vector3 position;
        public float scale;
        public Vector3 rotation;
        public uint variationData; // 包含meshID, materialID, seed
    }

    //public Vector3 boundsCenter;
    //public Vector3 boundsSize;
    public BakedGrassData[] grassInstances;
    
    // 分区数据（可选，用于大型场景）
    //public List<GrassDataChunk> chunks;
}

[System.Serializable]
public struct GrassDataChunk
{
    public Vector3 chunkCenter;
    public Vector3 chunkSize;
    public int startIndex;
    public int count;
}