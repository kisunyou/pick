Shader "Custom/URP/NormalCapture"
{
    // ============================================================
    //  Three.js MeshNormalMaterial 대응 셰이더
    //  월드 노멀을 RGB [0,1] 로 인코딩해서 RT 에 출력
    //  RenderPixelatedPass 의 Normal Pass 에서 override 용으로 사용
    // ============================================================
    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "NormalCapture"

            HLSLPROGRAM
            #pragma vertex   NC_Vert
            #pragma fragment NC_Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half3  normalWS    : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings NC_Vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                // Three.js MeshNormalMaterial 과 동일하게 월드 노멀 사용
                OUT.normalWS    = (half3)TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 NC_Frag(Varyings IN) : SV_Target
            {
                half3 n = normalize(IN.normalWS);
                // Three.js getNormal: texture.rgb * 2.0 - 1.0 로 디코딩하므로
                // 인코딩은 n * 0.5 + 0.5
                return half4(n * 0.5h + 0.5h, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
