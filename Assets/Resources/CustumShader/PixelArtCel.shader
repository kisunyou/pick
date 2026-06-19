Shader "Custom/URP/PixelArtCel"
{
    // ============================================================
    //  PixelArt Cel - Mobile Optimized URP
    //  GhibliSoft 베이스 → Pixel Art Scene 스타일로 변환
    //
    //  특징:
    //   - Bayer 4x4 Ordered Dithering (명암 경계부 픽셀 패턴)
    //   - Limited Color Palette (posterization)
    //   - Hard Cel Shading (2-3단계 명암)
    //   - Pixel-snapped UV (텍스처 픽셀화)
    //   - Outline (선택)
    //   - SRP Batcher 호환
    // ============================================================
    Properties
    {
        // ── Base ──────────────────────────────────────────────────
        [MainTexture] _BaseMap      ("Albedo Map",          2D)         = "white" {}
        [MainColor]   _BaseColor    ("Base Color",          Color)      = (1,1,1,1)

        // ── Pixel Art ─────────────────────────────────────────────
        [Header(Pixel Art)]
        _PixelSize      ("Pixel Size (UV snap)",    Range(1,64))    = 1.0
        _PaletteSteps   ("Palette Steps",           Range(2,32))    = 8.0
        _DitherScale    ("Dither Scale",            Range(0,1))     = 0.5
        _DitherContrast ("Dither Contrast",         Range(0,1))     = 0.35

        // ── Cel Shading ───────────────────────────────────────────
        [Header(Cel Shading)]
        _LightColor     ("Light Color",             Color)          = (1.00,0.96,0.82,1)
        _MidColor       ("Mid Color",               Color)          = (0.70,0.75,0.90,1)
        _ShadowColor    ("Shadow Color",            Color)          = (0.30,0.32,0.50,1)
        _CelThresh1     ("Cel Threshold Light",     Range(0,1))     = 0.65
        _CelThresh2     ("Cel Threshold Shadow",    Range(0,1))     = 0.15
        _CelHardness    ("Cel Hardness",            Range(0.01,0.2))= 0.03

        // ── Ambient ───────────────────────────────────────────────
        [Header(Ambient)]
        _SkyColor       ("Sky Ambient",             Color)          = (0.55,0.68,0.90,1)
        _GroundColor    ("Ground Ambient",          Color)          = (0.35,0.30,0.28,1)
        _AmbientStr     ("Ambient Strength",        Range(0,1))     = 0.30

        // ── Specular (픽셀아트 스타일 hard spec) ──────────────────
        [Header(Specular)]
        [Toggle(_SPECULAR_ON)] _UseSpecular ("Enable Specular",     Float) = 1
        _SpecColor      ("Specular Color",          Color)          = (1,1,0.8,1)
        _SpecThresh     ("Specular Threshold",      Range(0,1))     = 0.82
        _SpecHardness   ("Specular Hardness",       Range(0.01,0.1))= 0.02
        _SpecStrength   ("Specular Strength",       Range(0,2))     = 0.8

        // ── Outline ───────────────────────────────────────────────
        [Header(Outline)]
        [Toggle(_OUTLINE_ON)] _UseOutline ("Enable Outline",        Float) = 1
        _OutlineColor   ("Outline Color",           Color)          = (0.08,0.08,0.12,1)
        _OutlineWidth   ("Outline Width",           Range(0,0.05))  = 0.01

        // ── Rim ───────────────────────────────────────────────────
        [Header(Rim)]
        [Toggle(_RIM_ON)] _UseRim ("Enable Rim",                    Float) = 0
        _RimColor       ("Rim Color",               Color)          = (0.9,0.95,1.0,1)
        _RimPower       ("Rim Power",               Range(1,8))     = 4.0
        _RimStrength    ("Rim Strength",            Range(0,1))     = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"        = "Opaque"
            "RenderPipeline"    = "UniversalPipeline"
            "Queue"             = "Geometry"
            "IgnoreProjector"   = "True"
            "ShaderModel"       = "3.5"
        }
        LOD 200

        // ── Shared CBUFFER ────────────────────────────────────────
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _PixelSize;
            half   _PaletteSteps;
            half   _DitherScale;
            half   _DitherContrast;
            half4  _LightColor;
            half4  _MidColor;
            half4  _ShadowColor;
            half   _CelThresh1;
            half   _CelThresh2;
            half   _CelHardness;
            half4  _SkyColor;
            half4  _GroundColor;
            half   _AmbientStr;
            half4  _SpecColor;
            half   _SpecThresh;
            half   _SpecHardness;
            half   _SpecStrength;
            half4  _OutlineColor;
            half   _OutlineWidth;
            half4  _RimColor;
            half   _RimPower;
            half   _RimStrength;
        CBUFFER_END
        ENDHLSL

        // ══════════════════════════════════════════════════════════
        // Pass 1 : ForwardLit
        // ══════════════════════════════════════════════════════════
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   PA_Vert
            #pragma fragment PA_Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _SPECULAR_ON
            #pragma shader_feature_local_fragment _RIM_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            // ── Bayer 4x4 Matrix ────────────────────────────────
            //  normalized 0~1 범위
            half GetBayer4x4(int2 pixel)
            {
                // 4x4 Bayer matrix (0~15), /16 → 0~0.9375
                const half bayer[16] = {
                     0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
                    12.0/16.0,  4.0/16.0, 14.0/16.0,  6.0/16.0,
                     3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
                    15.0/16.0,  7.0/16.0, 13.0/16.0,  5.0/16.0
                };
                int idx = (pixel.x % 4) + (pixel.y % 4) * 4;
                return bayer[idx];
            }

            // ── Palette Posterize ────────────────────────────────
            half3 Posterize(half3 col, half steps)
            {
                return floor(col * steps + 0.5h) / steps;
            }

            // ── Hard Band (픽셀아트 느낌 계단) ──────────────────
            half HardBand(half val, half thresh, half hard)
            {
                return smoothstep(thresh - hard, thresh + hard, val);
            }

            // ── 3-step Cel ──────────────────────────────────────
            half3 CelLight(half NdotL, half shadow,
                           half3 lightCol, half3 midCol, half3 shadowCol,
                           half t1, half t2, half hard)
            {
                half lit  = NdotL * shadow;
                half hi   = HardBand(lit, t1, hard);
                half mid  = HardBand(lit, t2, hard) * (1.0h - hi);
                half dark = 1.0h - hi - mid;
                return lightCol * hi + midCol * mid + shadowCol * dark;
            }

            // ── 구조체 ───────────────────────────────────────────
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS      : SV_POSITION;
                float2 uv               : TEXCOORD0;
                float4 positionWSAndFog : TEXCOORD1;
                half3  normalWS         : TEXCOORD2;
                float4 shadowCoord      : TEXCOORD3;
                half3  viewDirWS        : TEXCOORD4;
                half3  ambientColor     : TEXCOORD5;
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                half3  vertexLighting   : TEXCOORD6;
            #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ── Vertex ──────────────────────────────────────────
            Varyings PA_Vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionHCS          = pos.positionCS;
                OUT.positionWSAndFog.xyz = pos.positionWS;
                OUT.positionWSAndFog.w   = ComputeFogFactor(pos.positionCS.z);
                OUT.uv                   = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS             = (half3)nrm.normalWS;
                OUT.shadowCoord          = GetShadowCoord(pos);
                OUT.viewDirWS            = (half3)(GetCameraPositionWS() - pos.positionWS);

                // Hemi + SH ambient (정점에서 계산)
                half  upFactor   = OUT.normalWS.y * 0.5h + 0.5h;
                half3 hemi       = lerp(_GroundColor.rgb, _SkyColor.rgb, upFactor);
                half3 shAmb      = SampleSH(nrm.normalWS);
                OUT.ambientColor = hemi * _AmbientStr + shAmb * (1.0h - _AmbientStr) * 0.5h;

            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                OUT.vertexLighting = VertexLighting(pos.positionWS, nrm.normalWS);
            #endif
                return OUT;
            }

            // ── Fragment ─────────────────────────────────────────
            half4 PA_Frag(Varyings IN) : SV_Target
            {
                // ① Pixel-snapped UV (텍스처 픽셀화)
                float2 uv = IN.uv;
                if (_PixelSize > 1.0h)
                {
                    float2 texSize;
                    _BaseMap.GetDimensions(texSize.x, texSize.y);
                    float2 snappedRes = max(texSize / _PixelSize, 1.0);
                    uv = (floor(uv * snappedRes) + 0.5) / snappedRes;
                }

                // ② 알베도
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb
                             * _BaseColor.rgb;

                // ③ 팔레트 Posterize
                albedo = Posterize(albedo, _PaletteSteps);

                // ④ 법선 / 뷰
                half3 N = normalize(IN.normalWS);
                half3 V = normalize(IN.viewDirWS);

                // ⑤ 메인 라이트
                Light mL     = GetMainLight(IN.shadowCoord);
                half3 L      = (half3)mL.direction;
                half  shadow = (half)mL.shadowAttenuation;
                half3 lightC = (half3)mL.color;

                half NdotL = dot(N, L);
                half NdotV = saturate(dot(N, V)) + 1e-4h;

                // ⑥ 3-step Cel
                half3 celCol = CelLight(
                    NdotL, shadow,
                    _LightColor.rgb  * lightC,
                    _MidColor.rgb,
                    _ShadowColor.rgb,
                    _CelThresh1, _CelThresh2, _CelHardness);

                half3 diffuse = albedo * celCol;

                // ⑦ Bayer Ordered Dithering (명암 경계부에 픽셀 패턴 주입)
                //    화면 픽셀 좌표 기반 → 픽셀아트 느낌
                int2  screenPix  = (int2)(IN.positionHCS.xy);
                half  bayer      = GetBayer4x4(screenPix);
                half  lit        = NdotL * shadow;
                // 경계 근처에서만 dither 적용 (스무스스텝으로 가중치)
                half  edgeWeight = 1.0h - abs(lit - (_CelThresh1 + _CelThresh2) * 0.5h) * 4.0h;
                edgeWeight       = saturate(edgeWeight);
                half  ditherBias = (bayer - 0.5h) * _DitherContrast * edgeWeight;
                diffuse          = lerp(diffuse, diffuse * (1.0h + ditherBias * _DitherScale * 2.0h),
                                        _DitherScale);

                // ⑧ Hard Specular (픽셀아트 스타일 - 날카로운 반짝)
                half3 specular = 0;
            #if defined(_SPECULAR_ON)
                half3 H     = normalize(L + V);
                half  NdotH = saturate(dot(N, H));
                half  spec  = HardBand(NdotH, _SpecThresh, _SpecHardness);
                spec       *= saturate(NdotL * shadow + 0.05h) * _SpecStrength;
                specular    = spec * (_SpecColor.rgb * lightC);
                // Specular도 Posterize
                specular    = Posterize(specular, _PaletteSteps * 0.5h);
            #endif

                // ⑨ Rim
                half3 rimLight = 0;
            #if defined(_RIM_ON)
                half rim    = pow(1.0h - NdotV, _RimPower);
                rim         = HardBand(rim, 0.4h, 0.05h); // hard rim
                rimLight    = rim * _RimStrength * (_RimColor.rgb * lightC);
            #endif

                // ⑩ Ambient (정점 계산값 × 알베도)
                half3 ambient = IN.ambientColor * albedo;

                // ⑪ 추가 광원
                half3 addLight = 0;
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                addLight = IN.vertexLighting * albedo;
            #elif defined(_ADDITIONAL_LIGHTS)
                uint addCnt = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(addCnt)
                    Light aL   = GetAdditionalLight(lightIndex, IN.positionWSAndFog.xyz);
                    half NdL_a = saturate(dot(N, (half3)aL.direction));
                    // 추가광도 cel 처리
                    half celA  = HardBand(NdL_a * aL.distanceAttenuation, 0.4h, 0.05h);
                    addLight  += albedo * (half3)aL.color * celA;
                LIGHT_LOOP_END
            #endif

                // ⑫ SSAO
            #if defined(_SCREEN_SPACE_OCCLUSION)
                float2 ssUV = GetNormalizedScreenSpaceUV(IN.positionHCS);
                AmbientOcclusionFactor aoF = GetScreenSpaceAmbientOcclusion(ssUV);
                ambient *= aoF.indirectAmbientOcclusion;
                diffuse *= aoF.directAmbientOcclusion;
            #endif

                // ⑬ 최종 합산 + 전체 Posterize
                half3 finalColor = diffuse + specular + rimLight + ambient + addLight;
                finalColor       = Posterize(finalColor, _PaletteSteps);
                finalColor       = MixFog(finalColor, (half)IN.positionWSAndFog.w);

                return half4(finalColor, 1.0h);
            }
            ENDHLSL
        }

        // ══════════════════════════════════════════════════════════
        // Pass 2 : Outline (Back-face extrusion)
        // ══════════════════════════════════════════════════════════
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   OL_Vert
            #pragma fragment OL_Frag
            #pragma shader_feature_local _OUTLINE_ON
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OL_Vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

            #if defined(_OUTLINE_ON)
                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                posWS        += normWS * _OutlineWidth;
                OUT.positionHCS = TransformWorldToHClip(posWS);
            #else
                // Outline OFF 시 클립 밖으로 보내서 컬링
                OUT.positionHCS = float4(0,0,-2,1);
            #endif
                return OUT;
            }

            half4 OL_Frag(Varyings IN) : SV_Target
            {
                return half4(_OutlineColor.rgb, 1.0h);
            }
            ENDHLSL
        }

        // ══════════════════════════════════════════════════════════
        // Pass 3 : ShadowCaster
        // ══════════════════════════════════════════════════════════
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   SC_Vert
            #pragma fragment SC_Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionHCS:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            float4 GetShadowHClip(Attributes IN)
            {
                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normWS = TransformObjectToWorldNormal(IN.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - posWS);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, ld));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return posCS;
            }

            Varyings SC_Vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = GetShadowHClip(IN);
                return OUT;
            }
            half4 SC_Frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ══════════════════════════════════════════════════════════
        // Pass 4 : DepthOnly
        // ══════════════════════════════════════════════════════════
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DO_Vert
            #pragma fragment DO_Frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionHCS:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings DO_Vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half4 DO_Frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
