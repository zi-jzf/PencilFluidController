using UnityEngine;

//C#とComputeShaderで共有する構造体
public struct ParticleData
{
    public Vector3 position;
    public Vector3 velocity;
    public Vector2 initialUV; //テクスチャから色をサンプリングするための元座標
    public Vector4 color; //画像から取得したいと情報を保持する
}
