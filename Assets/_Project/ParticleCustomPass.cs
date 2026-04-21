using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;

[System.Serializable]
public class ParticleCustomPass : CustomPass
{
    public FluidSimulationManager fluidManager;
    public Material particleMaterial;

    protected override void Execute(CustomPassContext ctx)
    {
        // 必要な参照が揃っていない、またはバッファが未生成の場合はスキップ
        if (fluidManager == null || particleMaterial == null || fluidManager.ParticleBuffer == null) 
            return;

        // シェーダーにバッファを渡す
        particleMaterial.SetBuffer("_ParticleBuffer", fluidManager.ParticleBuffer);

        // GPUに描画命令を直接発行 (Quadとして描画)
        ctx.cmd.DrawProceduralIndirect(
            Matrix4x4.identity, 
            particleMaterial, 
            0, 
            MeshTopology.Triangles, 
            fluidManager.ArgsBuffer
        );
    }
}