using UnityEngine;
using UnityEngine.InputSystem;

public class FluidGridSolver : MonoBehaviour
{
    [Header("Solver Settings")]
    public ComputeShader fluidGridCompute;
    public int resolution = 256; //グリッド自体の解像度(高すぎると重くなる)
    public float fluidViscosity = 0.01f; //粘性
    public int solverIterations = 40; //圧力計算のループ回数(多いほど正確だが重い)

    [Header("Interaction")]
    public float forceRadius = 0.05f;
    public float forceMultiplier = 500.0f;

    [Header("Obstacle(MonoMaskImage)")]
    public Texture2D obstacleMask; //白黒のマスク画像

    //流体計算に必要なテクスチャ群(ピンポンバッファ等)
    public RenderTexture velocityTx_A;
    public RenderTexture velocityTx_B;
    public RenderTexture pressureTx_A;
    public RenderTexture pressureTx_B;
    public RenderTexture divergenceTx;

    private int advectKernel, forceKernel, divergenceKernel, jacobiKernel, projectKernel; 
    private const int THREAD_SIZE = 8;
    private Vector3 lastMousePos;

    private Vector2 lastPencilUV;

    void Start()
    {
        InitializeTextures();
        InitializeCompute();
    }

    private void InitializeTextures()
    {
        //速度場(XYの2要素なのでRGFloat)
        velocityTx_A = CreateRenderTexture(RenderTextureFormat.RGFloat);
        velocityTx_B = CreateRenderTexture(RenderTextureFormat.RGFloat);
        
        //圧力・発散場(スカラー値なのでRFloat)
        pressureTx_A = CreateRenderTexture(RenderTextureFormat.RFloat);
        pressureTx_B = CreateRenderTexture(RenderTextureFormat.RFloat);
        divergenceTx = CreateRenderTexture(RenderTextureFormat.RFloat);
    }

    private RenderTexture CreateRenderTexture(RenderTextureFormat format)
    {
        RenderTexture rt = new RenderTexture(resolution, resolution, 0, format);
        rt.enableRandomWrite = true; //ComputeShaderで書き込むために必須
        rt.filterMode = FilterMode.Bilinear; //滑らかに補間する
        rt.wrapMode = TextureWrapMode.Clamp; //はみ出さない
        rt.Create();
        return rt;
    }

    private void InitializeCompute()
    {
        advectKernel = fluidGridCompute.FindKernel("Advect");
        forceKernel = fluidGridCompute.FindKernel("AddForce");
        divergenceKernel = fluidGridCompute.FindKernel("Divergence");
        jacobiKernel = fluidGridCompute.FindKernel("Jacobi");
        projectKernel = fluidGridCompute.FindKernel("Project");

        if(obstacleMask != null)
        {
            fluidGridCompute.SetTexture(advectKernel, "_ObstacleMask", obstacleMask);
            fluidGridCompute.SetTexture(forceKernel, "_ObstacleMask", obstacleMask);
            fluidGridCompute.SetTexture(divergenceKernel, "_ObstacleMask", obstacleMask);
            fluidGridCompute.SetTexture(jacobiKernel, "_ObstacleMask", obstacleMask);
            fluidGridCompute.SetTexture(projectKernel, "_ObstacleMask", obstacleMask);
        }
        else
        {
            Debug.LogWarning("obstacleMaskがセットされていません");
        }
    }

    void Update()
    {
        StepSimulation();
    }

    private void StepSimulation()
    {
        int threadGroupsX = Mathf.CeilToInt(resolution / (float)THREAD_SIZE);
        int threadGroupsY = Mathf.CeilToInt(resolution / (float)THREAD_SIZE);
        
        fluidGridCompute.SetFloat("_DeltaTime", Time.deltaTime);
        fluidGridCompute.SetInt("_Resolution", resolution);
        
        //Step1: 移流(Advect) - 速度場自身を動かす
        fluidGridCompute.SetTexture(advectKernel, "_VelocityRead", velocityTx_A);
        fluidGridCompute.SetTexture(advectKernel, "_VelocityWrite", velocityTx_B);
        fluidGridCompute.Dispatch(advectKernel, threadGroupsX, threadGroupsY, 1);
        Swap(ref velocityTx_A, ref velocityTx_B);

        //Step2: 外力(Add Force) - 入力を反映
        //ApplyMouseForce(threadGroupsX, threadGroupsY);
        ApplyPencilForce(threadGroupsX, threadGroupsY);
        
        //Step3: 発散(Divergence) - どこに流体が密集しているか計算
        fluidGridCompute.SetTexture(divergenceKernel, "_VelocityRead", velocityTx_A);
        fluidGridCompute.SetTexture(divergenceKernel, "_DivergenceWrite", divergenceTx);
        fluidGridCompute.Dispatch(divergenceKernel, threadGroupsX, threadGroupsY, 1);

        //Step4: 圧力(Pressure) - ヤコビ反復法で圧力を解く
        //毎フレーム圧力をリセット(または前フレームの値を初期推測値として使う)
        Graphics.Blit(Texture2D.blackTexture, pressureTx_A);
        for(int i = 0; i < solverIterations; i++)
        {
            fluidGridCompute.SetTexture(jacobiKernel, "_PressureRead", pressureTx_A);
            fluidGridCompute.SetTexture(jacobiKernel, "_PressureWrite", pressureTx_B);
            fluidGridCompute.SetTexture(jacobiKernel, "_DivergenceRead", divergenceTx);
            fluidGridCompute.Dispatch(jacobiKernel, threadGroupsX, threadGroupsY,1);
            Swap(ref pressureTx_A, ref pressureTx_B);
        }

        //Step5: 投影(Projection) - 圧力を使って速度場を非圧縮に修正
        fluidGridCompute.SetTexture(projectKernel, "_PressureRead", pressureTx_A);
        fluidGridCompute.SetTexture(projectKernel, "_VelocityRead", velocityTx_A);
        fluidGridCompute.SetTexture(projectKernel, "_VelocityWrite", velocityTx_B);
        fluidGridCompute.Dispatch(projectKernel, threadGroupsX, threadGroupsY, 1);
        Swap(ref velocityTx_A, ref velocityTx_B);

    }

    private void ApplyMouseForce(int threadGroupsX, int threadGroupsY)
    {
        if (Mouse.current == null || Camera.main == null) return;

        Vector2 mousePos2D = Mouse.current.position.ReadValue();
        Vector3 mouseScreenPos = new Vector3(mousePos2D.x, mousePos2D.y, Mathf.Abs(Camera.main.transform.position.z));
        Vector3 currentMousePos = Camera.main.ScreenToWorldPoint(mouseScreenPos);

        // 画面の座標系から 0.0 ~ 1.0 のUV空間に変換する処理（仮）
        // ※ FluidSimulationManagerのキャンバスサイズ等に合わせて後で調整します
        Vector2 forcePosUV = new Vector2(0.5f + (currentMousePos.x / 10f), 0.5f + (currentMousePos.y / 10f));
        
        Vector2 mouseVelocity = Vector2.zero;
        if (Mouse.current.leftButton.isPressed)
        {
            mouseVelocity = new Vector2(currentMousePos.x - lastMousePos.x, currentMousePos.y - lastMousePos.y) / Time.deltaTime;
        }

        fluidGridCompute.SetVector("_ForcePos", forcePosUV);
        fluidGridCompute.SetVector("_ForceDir", mouseVelocity * forceMultiplier);
        fluidGridCompute.SetFloat("_ForceRadius", forceRadius);

        fluidGridCompute.SetTexture(forceKernel, "_VelocityRead", velocityTx_A);
        fluidGridCompute.SetTexture(forceKernel, "_VelocityWrite", velocityTx_B);
        fluidGridCompute.Dispatch(forceKernel, threadGroupsX, threadGroupsY, 1);
        Swap(ref velocityTx_A, ref velocityTx_B);

        lastMousePos = currentMousePos;
    }

    private void ApplyPencilForce(int threadGroupsX, int threadGroupsY)
    {
        PencilData data = PencilReceiver.CurrentData;
        if (data == null) return;

        //iPadから送られてきた座標(0.0 ~ 1.0)をそのままUVとして使用
        Vector2 forcePosUV = new Vector2(data.x, data.y);
        Vector2 pencilVelocity = Vector2.zero;

        if(data.isPressed)
        {
            //ペンの移動速度
            pencilVelocity = (forcePosUV - lastPencilUV) / Time.deltaTime;
            
            //筆圧が強いほどかき混ぜる力が強くなる
            float currentForceMultiplier = forceMultiplier * Mathf.Max(data.pressure, 0.2f);

            //筆圧が強いほど影響を与える半径が広がる
            float currentRadius = forceRadius * (1.0f + data.pressure *0.5f);

            fluidGridCompute.SetVector("_ForcePos", forcePosUV);
            fluidGridCompute.SetVector("_ForceDir", pencilVelocity * currentForceMultiplier);
            fluidGridCompute.SetFloat("_ForceRadius", currentRadius);

            fluidGridCompute.SetTexture(forceKernel, "_VelocityRead", velocityTx_A);
            fluidGridCompute.SetTexture(forceKernel, "_VelocityWrite", velocityTx_B);
            fluidGridCompute.Dispatch(forceKernel, threadGroupsX, threadGroupsY, 1);
            Swap(ref velocityTx_A, ref velocityTx_B);
        }

        lastPencilUV = forcePosUV;
    }

    // テクスチャのAとBを入れ替える
    private void Swap(ref RenderTexture a, ref RenderTexture b)
    {
        RenderTexture temp = a;
        a = b;
        b = temp;
    }

    // 外部から障害物マスクを更新するためのメソッド
    public void UpdateObstacleMask(Texture2D newMask)
    {
        obstacleMask = newMask;
        if (obstacleMask != null)
        {
            fluidGridCompute.SetTexture(advectKernel, "_ObstacleMask", obstacleMask);
            fluidGridCompute.SetTexture(forceKernel, "_ObstacleMask", obstacleMask);
            fluidGridCompute.SetTexture(divergenceKernel, "_ObstacleMask", obstacleMask);
            fluidGridCompute.SetTexture(jacobiKernel, "_ObstacleMask", obstacleMask);
            fluidGridCompute.SetTexture(projectKernel, "_ObstacleMask", obstacleMask);
        }
    }

    void OnDestroy()
    {
        if (velocityTx_A != null) velocityTx_A.Release();
        if (velocityTx_B != null) velocityTx_B.Release();
        if (pressureTx_A != null) pressureTx_A.Release();
        if (pressureTx_B != null) pressureTx_B.Release();
        if (divergenceTx != null) divergenceTx.Release();
    }
}
