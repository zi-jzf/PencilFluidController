using UnityEngine;

//C#とComputeShaderで共有する構造体
public struct ParticleData
{
    public Vector3 position;
    public Vector3 velocity;
    public Vector2 intialUV; //テクスチャから色をサンプリングするための元座標
}
