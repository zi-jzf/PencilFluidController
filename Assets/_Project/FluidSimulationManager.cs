using UnityEngine;
using System.Runtime.InteropServices;

public class FluidSimulationManager : MonoBehaviour
{
    [Header("Compute Requirements")]
    public ComputeShader fluidComputeShader;
    public int particleCount = 1000000;

    [Header("Interaction(Mouse)")]
    public float interactionRadius = 2.0f;
    public float interactionForce = 50.0f;

    //外部から参照するためのプロパティ
    public ComputeBuffer ParticleBuffer { get; private set; }

    private int initKernel;
    private int updateKernel;
    private const int ThreadGroupSize = 256;

    //マウス入力計算用
    private Vector3 lastMousePos;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        InitializeBuffers();
        InitializeComputeShader();
    }

    private void InitializeBuffers()
    {
        int stride = Marshal.SizeOf<ParticleData>();
        ParticleBuffer = new ComputeBuffer(particleCount, stride);
        
        //バッファの初期データ配列を作成しセットする
        ParticleData[] initialData = new ParticleData[particleCount];

        //TODO: 画像の解像度に合わせてinitialData[i].positionとinitialUVをグリッド状に初期化する処理
        ParticleBuffer.SetData(initialData);
    }

    private void InitializeComputeShader()
    {
        initKernel = fluidComputeShader.FindKernel("InitParticles");
        updateKernel = fluidComputeShader.FindKernel("UpdateParticles");

        fluidComputeShader.SetInt("_ParticleCount", particleCount);

        fluidComputeShader.SetBuffer(initKernel, "_ParticleBuffer", ParticleBuffer);
        fluidComputeShader.SetBuffer(updateKernel, "_ParticleBuffer", ParticleBuffer);

        //初期化カーネルの実行
        int threadGroups = Mathf.CeilToInt(particleCount / (float)ThreadGroupSize);
        fluidComputeShader.Dispatch(initKernel, threadGroups, 1, 1);
    }

    // Update is called once per frame
    void Update()
    {
        DispatchSimulation();
    }

    private void DispatchSimulation()
    {
        //マウス座標の取得(スクリーンからワールド座標への変換)
        //実際にはZ深度を固定したPlaneに対するRayCast等で取得
        Vector3 currentMousePos = GetMouseWorldPosition();
        Vector3 mouseVelocity = (currentMousePos - lastMousePos) / Time.deltaTime;

        fluidComputeShader.SetFloat("_DeltaTime", Time.deltaTime);
        fluidComputeShader.SetVector("_MousePosition", currentMousePos);
        fluidComputeShader.SetVector("_MouseVelocity", mouseVelocity);
        fluidComputeShader.SetFloat("_InteractionRadius", interactionRadius);
        fluidComputeShader.SetFloat("_InteractionForce", interactionForce);

        //シミュレーションカーネルの実行
        int threadGroups = Mathf.CeilToInt(particleCount / (float)ThreadGroupSize);
        fluidComputeShader.Dispatch(updateKernel, threadGroups, 1, 1);

        lastMousePos = currentMousePos;
    }

    private Vector3 GetMouseWorldPosition()
    {
        //TODO: プロジェクトのカメラ設定に合わせたワールド座標変換の実装
        return Vector3.zero;
    }

    void OnDestroy()
    {
        if(ParticleBuffer != null)
        {
            ParticleBuffer.Release();
            ParticleBuffer = null;
        }
    }
}
