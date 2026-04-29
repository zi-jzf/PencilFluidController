using UnityEngine;
using System.Runtime.InteropServices;
using UnityEngine.InputSystem;
using System.Collections;

public class FluidSimulationManager : MonoBehaviour
{
    [Header("Compute Requirements")]
    public ComputeShader fluidComputeShader;
    public Texture2D sourceImage; //任意の画像を設定
    public float canvasSize = 10.0f; //画像の表示スケール

    [Header("Interaction(Mouse)")]
    public float interactionRadius = 2.0f;
    public float interactionForce = 50.0f;

    [Header("Fluid Solver Reference")]
    public FluidGridSolver gridSolver; //流体ソルバーへの参照

    [Header("Reset Settings")]
    public float resetDuration = 2.0f; //完全リセットに掛ける係数
    private float currentResetBlend = 0.0f;
    private Coroutine resetCoroutine;

    //外部から参照するためのプロパティ
    public ComputeBuffer ParticleBuffer { get; private set; }
    public ComputeBuffer ArgsBuffer { get; private set; } //描画命令用バッファ

    private int particleCount;
    private int initKernel;
    private int updateKernel;
    private const int ThreadGroupSize = 256;

    //マウス入力計算用
    private Vector3 lastMousePos;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if(sourceImage == null){
            Debug.LogError("Source Image is not assigned!");
            return;
        }

        //画像のピクセル数=パーティクル数として自動設定
        particleCount = sourceImage.width * sourceImage.height;

        InitializeBuffers();
        InitializeComputeShader();
    }

    private void InitializeBuffers()
    {
        int stride = Marshal.SizeOf<ParticleData>();
        ParticleBuffer = new ComputeBuffer(particleCount, stride);
        
        //バッファの初期データ配列を作成しセットする(ダミーデータ)
        ParticleData[] initialData = new ParticleData[particleCount];
        ParticleBuffer.SetData(initialData);

        //描画用のIndirect Arguments Bufferを作成
        //[頂点数(Quadなので6), インスタンス数(パーティクル数), 開始頂点, 開始インスタンス]
        ArgsBuffer = new ComputeBuffer(1, 4 * sizeof(uint), ComputeBufferType.IndirectArguments);
        ArgsBuffer.SetData(new uint[] {6, (uint)particleCount, 0, 0});
        
    }

    private void InitializeComputeShader()
    {
        initKernel = fluidComputeShader.FindKernel("InitParticles");
        updateKernel = fluidComputeShader.FindKernel("UpdateParticles");

        fluidComputeShader.SetInt("_ParticleCount", particleCount);

        fluidComputeShader.SetInt("_ResolutionX", sourceImage.width);
        fluidComputeShader.SetInt("_ResolutionY", sourceImage.height);

        fluidComputeShader.SetFloat("_CanvasSize", canvasSize);
        fluidComputeShader.SetTexture(initKernel, "_MainTex", sourceImage);
        fluidComputeShader.SetBuffer(initKernel, "_ParticleBuffer", ParticleBuffer);
        
        //UpdateParticleにも画像を渡す(Pencilの視覚的フィードバックの部分で使用するため)
        fluidComputeShader.SetTexture(updateKernel, "_MainTex", sourceImage);

        //初期化カーネルの実行
        int threadGroups = Mathf.CeilToInt(particleCount / (float)ThreadGroupSize);
        fluidComputeShader.Dispatch(initKernel, threadGroups, 1, 1);

        //Update用のバッファセット
        fluidComputeShader.SetBuffer(updateKernel, "_ParticleBuffer", ParticleBuffer);
    }

    // Update is called once per frame
    void Update()
    {
        DispatchSimulation();
    }

    private void DispatchSimulation()
    {
        //FluidGridSolverがアタッチされていない、またはテクスチャの準備ができていない場合はスキップ
        if (gridSolver == null || gridSolver.velocityTx_A == null) return;

        //ペンの入力をPencilReceiverから取得
        PencilData data = PencilReceiver.CurrentData;
        int isInteracting = (data != null && data.isPressed) ? 1 : 0;

        fluidComputeShader.SetFloat("_DeltaTime", Time.deltaTime);
        fluidComputeShader.SetFloat("_ResetBlend", currentResetBlend);

        //カーソル描画用にペンの情報をシェーダーに渡す
        if (data != null)
        {
            fluidComputeShader.SetVector("_PencilUV", new Vector2(data.x, data.y));
            fluidComputeShader.SetFloat("_PencilPressure", data.pressure);
            fluidComputeShader.SetInt("_IsPencilActive", data.isPressed ? 1 : 0);
        }
        else
        {
            fluidComputeShader.SetInt("_IsPencilActive", 0);
        }

        // 気象庁（GridSolver）が計算した最新の「風のテクスチャ」を渡す
        fluidComputeShader.SetTexture(updateKernel, "_VelocityField", gridSolver.velocityTx_A);
        
        // 障害物テクスチャがあれば渡す
        if(gridSolver.obstacleMask != null){
            fluidComputeShader.SetTexture(updateKernel, "_ObstacleMask", gridSolver.obstacleMask);
        }

        int threadGroups = Mathf.CeilToInt(particleCount / (float)ThreadGroupSize);
        fluidComputeShader.Dispatch(updateKernel, threadGroups, 1, 1);
    }

    // インスペクター用の強制リセット・再開ボタン
    // FluidSimulationManagerスクリプトの名前部分を右クリックして実行
    [ContextMenu("Trigger Reset (元の絵に戻す)")]
    public void TriggerReset()
    {
        if (resetCoroutine != null) StopCoroutine(resetCoroutine);
        resetCoroutine = StartCoroutine(SmoothReset(true));
    }

    [ContextMenu("Release Fluid (再び流体化させる)")]
    public void ReleaseFluid()
    {
        if (resetCoroutine != null) StopCoroutine(resetCoroutine);
        resetCoroutine = StartCoroutine(SmoothReset(false));
    }

    // 滑らかにブレンド値を遷移させるコルーチン
    private IEnumerator SmoothReset(bool isResetting)
    {
        float startValue = currentResetBlend;
        float targetValue = isResetting ? 1.0f : 0.0f;
        float elapsed = 0f;

        while (elapsed < resetDuration)
        {
            elapsed += Time.deltaTime;
            // SmoothStep関数で、徐々に加速・減速しながら滑らかに値を変化させる
            float t = elapsed / resetDuration;
            currentResetBlend = Mathf.SmoothStep(startValue, targetValue, t);
            yield return null;
        }

        currentResetBlend = targetValue;
    }

    void OnDestroy()
    {
        ParticleBuffer?.Release();
        ArgsBuffer?.Release();
    }
}
