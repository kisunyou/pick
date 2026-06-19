Shader "Custom/URP/WaterMobile"
{
    Properties
    {
        _DiffuseTex       ("Diffuse Texture",    2D) = "white" {}
        _NoiseTex         ("Noise Texture",      2D) = "white" {}

        _WaterColor       ("Water Color",        Color) = (0.4, 0.8, 1.0, 0.85)
        _DepthColor       ("Depth Color",        Color) = (0.1, 0.4, 0.7, 1.0)

        // 흐름
        _FlowSpeed        ("Flow Speed",         Float) = 0.05
        _FlowDirection    ("Flow Direction XY",  Vector) = (1.0, 0.5, 0, 0)

        // 노이즈 왜곡 세기
        _NoiseStrength    ("Noise Distortion",   Range(0, 0.1)) = 0.025

        // 디퓨즈 타일링 (Properties의 _DiffuseTex ST와 별개로 추가 배율)
        _DiffuseTiling    ("Diffuse Tiling",     Float) = 1.5

        // 반짝임 (specular)
        _SpecColor2       ("Specular Color",     Color) = (1,1,1,1)
        _Shininess        ("Shininess",          Range(8, 128)) = 64

        // 투명도
        _Alpha            ("Alpha",              Range(0, 1)) = 0.85

        // 거품/하이라이트 밝기
        _BrightThreshold  ("Bright Threshold",  Range(0, 1)) = 0.75
        _BrightIntensity  ("Bright Intensity",  Range(0, 2)) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        LOD 100

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_fog
            // 모바일 최적화: 불필요한 키워드 최소화
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ── 텍스처 & 샘플러 ──────────────────────────────
            TEXTURE2D(_DiffuseTex);  SAMPLER(sampler_DiffuseTex);
            TEXTURE2D(_NoiseTex);    SAMPLER(sampler_NoiseTex);

            // ── CBUFFER (SRP Batcher 호환) ───────────────────
            CBUFFER_START(UnityPerMaterial)
                float4 _DiffuseTex_ST;
                float4 _NoiseTex_ST;

                half4  _WaterColor;
                half4  _DepthColor;

                float  _FlowSpeed;
                float4 _FlowDirection;

                half   _NoiseStrength;
                float  _DiffuseTiling;

                half4  _SpecColor2;
                half   _Shininess;

                half   _Alpha;
                half   _BrightThreshold;
                half   _BrightIntensity;
            CBUFFER_END

            // ── 구조체 ───────────────────────────────────────
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uvDiffuse   : TEXCOORD0;   // 디퓨즈 UV (레이어 A)
                float2 uvDiffuse2  : TEXCOORD1;   // 디퓨즈 UV (레이어 B, 역방향)
                float2 uvNoise     : TEXCOORD2;
                float3 normalWS    : TEXCOORD3;
                float3 viewDirWS   : TEXCOORD4;
                float  fogFactor   : TEXCOORD5;
            };

            // ── Vertex ───────────────────────────────────────
            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS = posInputs.positionCS;
                OUT.normalWS    = nrmInputs.normalWS;
                OUT.viewDirWS   = GetWorldSpaceViewDir(posInputs.positionWS);

                float2 flow = _FlowDirection.xy * _FlowSpeed * _Time.y;

                // 디퓨즈: 두 레이어를 반대 방향으로 흘려서 자연스러운 물결 연출
                float2 baseUV = TRANSFORM_TEX(IN.uv, _DiffuseTex) * _DiffuseTiling;
                OUT.uvDiffuse  = baseUV + flow;
                OUT.uvDiffuse2 = baseUV - flow * 0.6;

                // 노이즈: 약간 다른 타일링으로 주기 깨기
                OUT.uvNoise = TRANSFORM_TEX(IN.uv, _NoiseTex) + flow * 1.3;

                OUT.fogFactor = ComputeFogFactor(posInputs.positionCS.z);

                return OUT;
            }

            // ── Fragment ─────────────────────────────────────
            half4 frag(Varyings IN) : SV_Target
            {
                // 1) 노이즈 샘플 → UV 왜곡 오프셋
                half2 noiseVal = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, IN.uvNoise).rg;
                half2 distort  = (noiseVal - 0.5h) * 2.0h * _NoiseStrength;

                // 2) 디퓨즈 두 레이어 블렌드 (노이즈로 왜곡)
                half4 diffA = SAMPLE_TEXTURE2D(_DiffuseTex, sampler_DiffuseTex, IN.uvDiffuse  + distort);
                half4 diffB = SAMPLE_TEXTURE2D(_DiffuseTex, sampler_DiffuseTex, IN.uvDiffuse2 - distort);
                half4 diff  = (diffA + diffB) * 0.5h;

                // 3) 물 기본 색상 믹스
                //    밝은 픽셀일수록 _WaterColor, 어두울수록 _DepthColor
                half  lum   = dot(diff.rgb, half3(0.299, 0.587, 0.114));
                half4 water = lerp(_DepthColor, _WaterColor, lum);
                water.rgb  *= diff.rgb;

                // 4) 밝은 하이라이트(거품/반짝) 강조
                half bright = smoothstep(_BrightThreshold, 1.0h, lum);
                water.rgb   = lerp(water.rgb, water.rgb * _BrightIntensity, bright);

                // 5) Blinn-Phong Specular (모바일 경량)
                Light mainLight  = GetMainLight();
                half3 normalWS   = normalize(IN.normalWS);
                half3 viewDir    = normalize(IN.viewDirWS);
                half3 halfDir    = normalize(mainLight.direction + viewDir);
                half  NdotH      = saturate(dot(normalWS, halfDir));
                half  spec       = pow(NdotH, _Shininess);
                water.rgb       += _SpecColor2.rgb * spec * mainLight.color;

                // 6) 투명도
                water.a = _WaterColor.a * _Alpha;

                // 7) Fog
                water.rgb = MixFog(water.rgb, IN.fogFactor);

                return water;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
