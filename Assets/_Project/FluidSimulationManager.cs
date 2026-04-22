using UnityEngine;
using System.Runtime.InteropServices;
using UnityEngine.InputSystem;

public class FluidSimulationManager : MonoBehaviour
{
    [Header("Compute Requirements")]
    public ComputeShader fluidComputeShader;
    public Texture2D sourceImage; //任意の画像を設定
    public float canvasSize = 10.0f; //画像の表示スケール

    [Header("Interaction(Mouse)")]
    public float interactionRadius = 2.0f;
    public float interactionForce = 50.0f;

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
        //新しいInputSystemによるマウス制御
        if(Mouse.current == null || Camera.main == null) return;

        //簡易的なマウス座標取得(Z=0の平面)
        Vector3 mousePos2D = Mouse.current.position.ReadValue();
        Vector3 mouseScreenPos = new Vector3(mousePos2D.x, mousePos2D.y, Mathf.Abs(Camera.main.transform.position.z));
        Vector3 currentMousePos = Camera.main.ScreenToWorldPoint(mouseScreenPos);

        //マウスの移動量(速度)
        Vector3 mouseVelocity = Vector3.zero;
        if(Mouse.current.leftButton.isPressed){
            mouseVelocity = (currentMousePos - lastMousePos) / Time.deltaTime;
        }

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

    void OnDestroy()
    {
        ParticleBuffer?.Release();
        ArgsBuffer?.Release();
    }
}
