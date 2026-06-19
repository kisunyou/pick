Shader "Custom/URP/GhibliSoft"
{
    // ============================================================
    //  Ghibli Soft - Mobile Optimized (Tier 1 추가 최적화 적용)
    //  변경점:
    //   - SampleSH + Hemisphere Ambient 를 정점에서 한 번에 계산
    //   - View Direction 을 정점에서 보간 (프래그먼트는 재정규화만)
    //   - normalize(mL.direction) 제거 (URP 가 이미 정규화)
    //   - mL.color 캐스트 1회로 캐시
    //   - 보간기 추가 1개 (총 6-7개, 모바일 안전 범위)
    // ============================================================
    Properties
    {
        // ── Base ──────────────────────────────────────────────────────────
        [MainTexture] _BaseMap            ("Albedo Map",              2D)         = "white"  {}
        [MainColor]   _BaseColor          ("Base Color",              Color)      = (1, 1, 1, 1)
        // ── Painterly Diffuse ─────────────────────────────────────────────
        [Header(Painterly Diffuse)]
        _WarmColor          ("Warm Light Color",        Color)      = (1.00, 0.96, 0.82, 1)
        _CoolColor          ("Cool Shadow Color",       Color)      = (0.55, 0.60, 0.78, 1)
        _WarmCoolBalance    ("Warm / Cool Balance",     Range(0,1)) = 0.0
        _ShadowSoftness     ("Shadow Softness",         Range(0.01,1.0)) = 0.092
        _ShadowOffset       ("Shadow Offset",           Range(-1,1))     = 0.04
        _ShadowColorStr     ("Shadow Color Strength",   Range(0,1)) = 0.57
        // ── Ambient ────────────────────────────────────────────────────────
        [Header(Ambient)]
        _SkyColor           ("Sky Ambient",             Color)      = (0.62, 0.76, 0.92, 1)
        _GroundColor        ("Ground Ambient",          Color)      = (0.48, 0.42, 0.36, 1)
        _AmbientStr         ("Ambient Strength",        Range(0,1)) = 0.314
        // ── Painterly Specular ─────────────────────────────────────────────
        [Header(Painterly Specular)]
        [Toggle(_SPECULAR_ON)] _UseSpecular ("Enable Specular", Float) = 1
        _SpecColor          ("Specular Color",          Color)      = (1.0, 0.98, 0.90, 1)
        _SpecSoftness       ("Specular Softness",       Range(0.01,1.0)) = 0.087
        _SpecThresh         ("Specular Threshold",      Range(0,1))      = 0.67
        _SpecStrength       ("Specular Strength",       Range(0,2))      = 0.0
        [Toggle(_SPEC_NOISE_ON)] _UseSpecNoise ("Enable Spec Noise (heavy)", Float) = 0
        _SpecNoiseTiling    ("Spec Noise Tiling",       Float)      = 6.0
        _SpecNoiseStr       ("Spec Noise Strength",     Range(0,1)) = 0.18
        // ── Rim Light ─────────────────────────────────────────────────────
        [Header(Rim Light)]
        [Toggle(_RIM_ON)] _UseRim ("Enable Rim", Float) = 1
        _RimColor           ("Rim Color",               Color)      = (0.85, 0.92, 1.0, 1)
        _RimPower           ("Rim Power",               Range(0.5,8))    = 5.5
        _RimStrength        ("Rim Strength",            Range(0,1))      = 1.0
        _RimSoftness        ("Rim Softness",            Range(0,0.5))    = 0.18
        _RimThresh          ("Rim Threshold",           Range(0,1))      = 0.27
        // ── Color Grading ─────────────────────────────────────────────────
        [Header(Color Grading)]
        [Toggle(_COLORGRADING_ON)] _UseColorGrading ("Enable Color Grading", Float) = 1
        _Saturation         ("Saturation",              Range(0,2)) = 1.47
        _Brightness         ("Brightness",              Range(0.5,1.5)) = 1.0
        _ColorTintShadow    ("Shadow Tint",             Color)      = (0.60, 0.62, 0.80, 1)
        _ColorTintLight     ("Light Tint",              Color)      = (1.0, 0.97, 0.88, 1)
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
        // ───────── Shared HLSL (CBUFFER + utility functions) ─────────
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half4  _WarmColor;       half4  _CoolColor;       half _WarmCoolBalance;
            half   _ShadowSoftness;  half   _ShadowOffset;    half _ShadowColorStr;
            half4  _SkyColor;        half4  _GroundColor;     half _AmbientStr;
            half4  _SpecColor;       half   _SpecSoftness;    half _SpecThresh;
            half   _SpecStrength;    half   _SpecNoiseTiling; half _SpecNoiseStr;
            half4  _RimColor;        half   _RimPower;        half _RimStrength;
            half   _RimSoftness;     half   _RimThresh;
            half   _Saturation;      half   _Brightness;
            half4  _ColorTintShadow; half4  _ColorTintLight;
        CBUFFER_END
        ENDHLSL
        // ══════════════════════════════════════════════════════════════════
        // Pass 1 : ForwardLit
        // ══════════════════════════════════════════════════════════════════
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex   GB_Vert
            #pragma fragment GB_Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _SPECULAR_ON
            #pragma shader_feature_local_fragment _SPEC_NOISE_ON
            #pragma shader_feature_local_fragment _RIM_ON
            #pragma shader_feature_local_fragment _COLORGRADING_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            // ── 유틸 ───────────────────────────────────────────────────
            half GB_Hash(half2 p)
            {
                p = frac(p * half2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            half GB_Noise(half2 uv)
            {
                half2 i = floor(uv);
                half2 f = frac(uv);
                half2 u = f * f * (3.0 - 2.0 * f);
                half a = GB_Hash(i);
                half b = GB_Hash(i + half2(1, 0));
                half c = GB_Hash(i + half2(0, 1));
                half d = GB_Hash(i + half2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }
            half3 AdjustSaturationLum(half3 col, half sat)
            {
                half lum = dot(col, half3(0.299, 0.587, 0.114));
                return lerp(half3(lum, lum, lum), col, sat);
            }
            half SoftBand(half val, half thresh, half soft)
            {
                return smoothstep(thresh - soft, thresh + soft, val);
            }
            half3 WarmCoolLight(half NdotL, half shadow,
                                half3 warmCol, half3 coolCol,
                                half balance, half soft, half offset)
            {
                half t = SoftBand(NdotL * shadow, offset, soft);
                t = saturate(t + (balance - 0.5h) * 0.3h);
                return lerp(coolCol, warmCol, t);
            }
            // ── 정점 ────────────────────────────────────────────────────
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                // xyz = world pos (additional lights / SSAO 용),  w = fog
                float4 positionWSAndFog : TEXCOORD1;
                half3  normalWS    : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                // 정점에서 미리 계산해서 보간으로 처리하는 항목들
                half3  viewDirWS   : TEXCOORD4;   // 정규화 전 (프래그먼트에서 normalize)
                half3  ambientColor: TEXCOORD5;   // hemi+SH 합산 (알베도 곱은 픽셀에서)
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                half3  vertexLighting : TEXCOORD6;
            #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings GB_Vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS         = pos.positionCS;
                OUT.positionWSAndFog.xyz = pos.positionWS;
                OUT.positionWSAndFog.w   = ComputeFogFactor(pos.positionCS.z);
                OUT.uv                   = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS             = (half3)nrm.normalWS;
                OUT.shadowCoord          = GetShadowCoord(pos);
                // V 벡터(정규화 전) - 프래그먼트에서 normalize 만 수행.
                OUT.viewDirWS = (half3)(GetCameraPositionWS() - pos.positionWS);
                // Hemi + SH ambient 를 정점에서 한 번에 계산 (알베도 제외).
                half  upFactor = OUT.normalWS.y * 0.5h + 0.5h;
                half3 hemi     = lerp(_GroundColor.rgb, _SkyColor.rgb, upFactor);
                half3 shAmb    = SampleSH(nrm.normalWS);
                OUT.ambientColor = hemi * _AmbientStr + shAmb * (1.0h - _AmbientStr) * 0.5h;
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                OUT.vertexLighting = VertexLighting(pos.positionWS, nrm.normalWS);
            #endif
                return OUT;
            }
            // ── 프래그먼트 ───────────────────────────────────────────────
            half4 GB_Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float3 posWS = IN.positionWSAndFog.xyz;
                half   fog = (half)IN.positionWSAndFog.w;
                // 알베도
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb
                             * _BaseColor.rgb;
            #if defined(_COLORGRADING_ON)
                albedo = AdjustSaturationLum(albedo, _Saturation) * _Brightness;
            #endif
                // 노말 / 뷰벡터 - V 는 정점에서 보간된 값만 정규화.
                half3 N = normalize(IN.normalWS);
                half3 V = normalize(IN.viewDirWS);
                // 메인 라이트 - direction 은 이미 정규화되어 옴.
                Light mL = GetMainLight(IN.shadowCoord);
                half3 L  = (half3)mL.direction;
                half  shadow = (half)mL.shadowAttenuation;
                half3 lightCol = (half3)mL.color;
                half NdotL = dot(N, L);
                half NdotV = saturate(dot(N, V)) + (half)1e-4;
                half lit   = NdotL * shadow;
                half band  = SoftBand(lit, _ShadowOffset, _ShadowSoftness);
                // Warm/Cool Soft Cel
                half3 warmCoolDiff = WarmCoolLight(
                    NdotL, shadow,
                    _WarmColor.rgb * lightCol,
                    _CoolColor.rgb,
                    _WarmCoolBalance, _ShadowSoftness, _ShadowOffset);
                // 그림자 영역 알베도 틴트
                half3 shadowAlbedo  = albedo * lerp(half3(0.55, 0.58, 0.75), half3(1, 1, 1), band);
                half3 diffuseAlbedo = lerp(shadowAlbedo, albedo, 1.0h - _ShadowColorStr);
                half3 diffuse       = diffuseAlbedo * warmCoolDiff;
                // Light/Shadow 컬러 틴트
            #if defined(_COLORGRADING_ON)
                half  tintT   = SoftBand(lit, _ShadowOffset, _ShadowSoftness * 2.0h);
                half3 tintCol = lerp(_ColorTintShadow.rgb, _ColorTintLight.rgb, tintT);
                diffuse *= tintCol;
            #endif
                // Painterly Specular - H/NdotH 도 Specular OFF 면 계산 생략.
                half3 specular = 0;
            #if defined(_SPECULAR_ON)
                half3 H     = normalize(L + V);
                half  NdotH = saturate(dot(N, H));
                half specBase = NdotH;
                #if defined(_SPEC_NOISE_ON)
                    half specNoise = GB_Noise((half2)uv * _SpecNoiseTiling);
                    specBase *= (1.0h - specNoise * _SpecNoiseStr);
                #endif
                half specBand = SoftBand(specBase, _SpecThresh, _SpecSoftness);
                // 곱셈 순서: 스칼라 먼저 합치고 마지막에 색상 곱 (모바일 ALU 친화적).
                half specScalar = specBand * _SpecStrength * saturate(lit + 0.1h);
                specular = specScalar * (_SpecColor.rgb * lightCol);
            #endif
                // Rim Light
                half3 rimLight = 0;
            #if defined(_RIM_ON)
                half oneMinusNV = saturate(1.0h - NdotV);
                half rim       = exp2(log2(oneMinusNV + 1e-4h) * _RimPower);
                half rimBand   = SoftBand(rim, _RimThresh, _RimSoftness);
                half rimFace   = saturate(NdotL * 0.5h + 0.5h);
                half rimScalar = rimBand * rimFace * _RimStrength;
                rimLight       = rimScalar * (_RimColor.rgb * lightCol);
            #endif
                // Ambient (정점에서 계산된 hemi+SH 를 픽셀에서 알베도만 곱함)
                half3 ambient = IN.ambientColor * albedo;
                // 추가 광원
                half3 addLight = 0;
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                addLight = IN.vertexLighting * albedo;
            #elif defined(_ADDITIONAL_LIGHTS)
                uint addCnt = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(addCnt)
                    Light aL = GetAdditionalLight(lightIndex, posWS);
                    half  aNdotL = saturate(dot(N, (half3)aL.direction));
                    half3 aCol   = (half3)aL.color * aL.distanceAttenuation * aL.shadowAttenuation;
                    addLight += albedo * aCol * aNdotL;
                LIGHT_LOOP_END
            #endif
                // SSAO
            #if defined(_SCREEN_SPACE_OCCLUSION)
                float2 ssUV = GetNormalizedScreenSpaceUV(IN.positionHCS);
                AmbientOcclusionFactor aoF = GetScreenSpaceAmbientOcclusion(ssUV);
                ambient *= aoF.indirectAmbientOcclusion;
                diffuse *= aoF.directAmbientOcclusion;
            #endif
                // 최종 합산
                half3 finalColor = diffuse + specular + rimLight + ambient + addLight;
                finalColor = MixFog(finalColor, fog);
                return half4(finalColor, 1.0h);
            }
            ENDHLSL
        }
        // ══════════════════════════════════════════════════════════════════
        // Pass 2 : ShadowCaster
        // ══════════════════════════════════════════════════════════════════
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex   GB_ShadowVert
            #pragma fragment GB_ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionHCS:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            float4 GB_GetShadowHClip(Attributes IN)
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
            Varyings GB_ShadowVert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = GB_GetShadowHClip(IN);
                return OUT;
            }
            half4 GB_ShadowFrag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
        // ══════════════════════════════════════════════════════════════════
        // Pass 3 : DepthOnly
        // ══════════════════════════════════════════════════════════════════
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex   GB_DOVert
            #pragma fragment GB_DOFrag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionHCS:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings GB_DOVert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half4 GB_DOFrag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}