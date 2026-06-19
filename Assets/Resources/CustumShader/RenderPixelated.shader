Shader "Custom/URP/RenderPixelated"
{
    Properties
    {
        _MainTex            ("Beauty (Low-res)",     2D)         = "white" {}
        _DepthTex           ("Depth Texture",        2D)         = "white" {}
        _NormalTex          ("Normal Texture",       2D)         = "white" {}
        _NormalEdgeStrength ("Normal Edge Strength", Range(0,1)) = 0.3
        _DepthEdgeStrength  ("Depth Edge Strength",  Range(0,1)) = 0.4
        _Resolution         ("Resolution",           Vector)     = (320,180,0.003125,0.005556)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "RenderPixelated"

            HLSLPROGRAM
            #pragma vertex   RP_Vert
            #pragma fragment RP_Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            TEXTURE2D(_DepthTex);
            TEXTURE2D(_NormalTex);

            // Unity 네이밍 컨벤션: sampler_point_clamp 는 자동으로 Point+Clamp 로 생성됨
            SamplerState sampler_point_clamp;
            SAMPLER(sampler_DepthTex);
            SAMPLER(sampler_NormalTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Resolution;
                half   _NormalEdgeStrength;
                half   _DepthEdgeStrength;
            CBUFFER_END

            struct RP_Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct RP_Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            RP_Varyings RP_Vert(RP_Attributes IN)
            {
                RP_Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = IN.uv;
                return OUT;
            }

            float SampleDepth(float2 uv, int2 offset)
            {
                float2 uvOff = uv + float2(offset) * _Resolution.zw;
                return SAMPLE_TEXTURE2D(_DepthTex, sampler_DepthTex, uvOff).r;
            }

            half3 SampleNormal(float2 uv, int2 offset)
            {
                float2 uvOff = uv + float2(offset) * _Resolution.zw;
                half3 n = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uvOff).rgb;
                return n * 2.0h - 1.0h;
            }

            float DepthEdgeIndicator(float2 uv, float depth)
            {
                float diff = 0.0;
                diff += clamp(SampleDepth(uv, int2( 1,  0)) - depth, 0.0, 1.0);
                diff += clamp(SampleDepth(uv, int2(-1,  0)) - depth, 0.0, 1.0);
                diff += clamp(SampleDepth(uv, int2( 0,  1)) - depth, 0.0, 1.0);
                diff += clamp(SampleDepth(uv, int2( 0, -1)) - depth, 0.0, 1.0);
                return floor(smoothstep(0.01, 0.02, diff) * 2.0) / 2.0;
            }

            float NeighborNormalEdgeIndicator(float2 uv, int2 offset, float depth, half3 normal)
            {
                float depthDiff       = SampleDepth(uv, offset) - depth;
                half3 neighborNormal  = SampleNormal(uv, offset);
                half  normalDiff      = dot(normal - neighborNormal, half3(1.0h, 1.0h, 1.0h));
                float normalIndicator = clamp(smoothstep(-0.01, 0.01, (float)normalDiff), 0.0, 1.0);
                float depthIndicator  = clamp(sign(depthDiff * 0.25 + 0.0025), 0.0, 1.0);
                return (1.0 - (float)dot(normal, neighborNormal)) * depthIndicator * normalIndicator;
            }

            float NormalEdgeIndicator(float2 uv, float depth, half3 normal)
            {
                float indicator = 0.0;
                indicator += NeighborNormalEdgeIndicator(uv, int2( 0, -1), depth, normal);
                indicator += NeighborNormalEdgeIndicator(uv, int2( 0,  1), depth, normal);
                indicator += NeighborNormalEdgeIndicator(uv, int2(-1,  0), depth, normal);
                indicator += NeighborNormalEdgeIndicator(uv, int2( 1,  0), depth, normal);
                return step(0.1, indicator);
            }

            half4 RP_Frag(RP_Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // Point Clamp 샘플링 → 선명한 픽셀화
                half4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_point_clamp, uv);

                float depth  = 0.0;
                half3 normal = half3(0.0h, 0.0h, 0.0h);

                if (_DepthEdgeStrength > 0.0h || _NormalEdgeStrength > 0.0h)
                {
                    depth  = SampleDepth(uv, int2(0, 0));
                    normal = SampleNormal(uv, int2(0, 0));
                }

                float dei = 0.0;
                if (_DepthEdgeStrength > 0.0h)
                    dei = DepthEdgeIndicator(uv, depth);

                float nei = 0.0;
                if (_NormalEdgeStrength > 0.0h)
                    nei = NormalEdgeIndicator(uv, depth, normal);

                float Strength = (dei > 0.0)
                    ? (1.0 - (float)_DepthEdgeStrength * dei)
                    : (1.0 + (float)_NormalEdgeStrength * nei);

                return texel * Strength;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
