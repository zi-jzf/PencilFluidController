Shader "CustomRenderTexture/ParticleRender"
{
    Properties
    {
        _ParticleSize("Particle Size", Float) = 0.05
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="HDRenderPipeline" }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            
            Cull Off
            ZWrite Off
            ZTest Always
            // 色の加算（光る表現）にしたい場合は以下を有効化
            // Blend SrcAlpha One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct ParticleData {
                float3 position;
                float3 velocity;
                float2 initialUV;
                float4 color;
            };

            StructuredBuffer<ParticleData> _ParticleBuffer;
            float _ParticleSize;

            struct Attributes {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
            };

            Varyings vert(Attributes input) {
                Varyings output;
                
                // バッファから該当パーティクルのデータを取得
                ParticleData p = _ParticleBuffer[input.instanceID];

                // 四角形（Quad）の6頂点を生成するためのUV定義
                float2 quadUVs[6] = { float2(0,0), float2(1,0), float2(0,1), float2(0,1), float2(1,0), float2(1,1) };
                float2 uv = quadUVs[input.vertexID];
                
                // 中心を原点とするローカル座標系の生成
                float3 localPos = float3((uv - 0.5) * _ParticleSize, 0);

                // ビルボード処理: カメラの右方向・上方向ベクトルを取得して適用（常にカメラの方向を向く）
                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 camUp    = UNITY_MATRIX_V[1].xyz;
                float3 worldPos = p.position + camRight * localPos.x + camUp * localPos.y;

                //HDRPのCRRに対応するためカメラ相対座標に変換
                float3 cameraRelativePos = GetCameraRelativePositionWS(worldPos);

                // ワールド座標からクリップ空間座標へ変換（HDRP用関数）
                output.positionCS = TransformWorldToHClip(cameraRelativePos);
                output.color = p.color;

                return output;
            }

            float4 frag(Varyings input) : SV_Target {
                return input.color;
            }
            ENDHLSL
        }
    }
}
