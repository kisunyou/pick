// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "CustumShader/DollShader" {
	Properties{
		_MainTex("Base (RGB)", 2D) = "white" {}

		_AmbientFactor("AmbientFactor", Range(0.0, 1.0)) = 0.985
	
		_TwincleFactor("TwincleFactor", Range(0.0, 3.0)) = 0.0
		
			_Diffuse("Diffuse", Color) = (1,1,1,1)
		//_MaskColor("MaskColor", Color) = (0,0,0,0)

		// Specular
		_Shininess("Specular Shininess", Range(0.3, 100)) = 0.067
		//_SpecularColor("Specular Color", Color) = (1,1,1,1)
		_SpecularIntensity("Specular Intensity", Range(0.0, 4.0)) = 0.2

		// RIM LIGHT
		//_RimColor("Rim Color", Color) = (0.8,0.8,0.8,0.6)
		_RimMin("Rim Min", Range(0, 1)) = 0.743
		_RimMax("Rim Max", Range(0, 1)) = 0.105
		_RimPower("Rim Power", Range(0, 10)) = 0.04

		// MATCAP
		_MatCapTex("MatCap (RGB)", 2D) = "white" {}
		_MatCapFactor("MatCap Factor", Range(1.0,10.0)) = 1.0

		// EMISSIVE
		_EmissiveTex("Emissive (RGBA),", 2D) = "black" {}
		_EmissiveFactor("Emissive Factor", Range(0.1, 4.0)) = 1.0

		// MASK (R:SPEC, G:MATCAP, B:DIFFUSE, A:RESERVE)
		_MaskTex("Mask (R:SP,G:MAT,B:NONE,A:NONE)", 2D) = "white" {}
	}

	SubShader 
	{
		Tags
		{
			"LightMode" = "ForwardBase"
			"Queue" = "Transparent"
			"IgnoreProjector" = "True"
			"RenderType" = "Transparent"
		}

		// Pixel lights
		Pass {
			ZWrite On
			Blend SrcAlpha OneMinusSrcAlpha

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_fog

			#pragma multi_compile DIFFUSE_ON DIFFUSE_OFF
			//#pragma multi_compile MASKCOLOR_ON MASKCOLOR_OFF
			#pragma multi_compile SPECULAR_ON SPECULAR_OFF
			#pragma multi_compile RIM_LIGHT_ON RIM_LIGHT_OFF
			#pragma multi_compile MATCAP_ON MATCAP_OFF
			#pragma multi_compile EMISSIVE_ON EMISSIVE_OFF
			#pragma multi_compile TWINCLE_ON TWINCLE_OFF

			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "AutoLight.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float2 uv : TEXCOORD0;
			};

			struct v2f {
				float4 vertex : POSITION;
				float3 normal : TEXCOORD0;
				float4 uv : TEXCOORD1;
				float4 lightColor : TEXCOORD2;
				float3 viewDir : TEXCOORD3;
#if SPECULAR_ON
				float3 reflect : TEXCOORD4;
#endif // SPECULAR_ON
				UNITY_FOG_COORDS(5)
			};

			uniform sampler2D _MainTex;
			uniform float4 _MainTex_ST;

			half _AmbientFactor;
			half _TwincleFactor;

#if DIFFUSE_ON
			half4 _Diffuse;
#endif // DIFFUSE_ON

#if MASKCOLOR_ON
			//half4 _MaskColor;
#endif // MASKCOLOR_ON

#if SPECULAR_ON
			half _Shininess;
			//half4 _SpecularColor;
			half _SpecularIntensity;
#endif // SPECULAR_ON

#if RIM_LIGHT_ON
			//half4 _RimColor;
			half _RimMin;
			half _RimMax;
			half _RimPower;
#endif // RIM_LIGHT_ON

#if MATCAP_ON
			sampler2D _MatCapTex;
			half _MatCapFactor;
#endif // MATCAP_ON

#if EMISSIVE_ON
			sampler2D _EmissiveTex;
			half _EmissiveFactor;
#endif // EMISSIVE_ON

			sampler2D _MaskTex;

			inline half3 WrappedLight(half3 lightDir, half3 normal, half atten)
			{
				half diffuse = max(0.3, dot(normal, lightDir)) + 0.5 * _AmbientFactor ;
				//half diffuse = dot(normal, lightDir);
				//half3 light_color = _LightColor0.rgb * diffuse ;// *atten * 1.2;
				half3 light_color = _LightColor0.rgb * diffuse + half3(_TwincleFactor, _TwincleFactor, _TwincleFactor);
				return light_color ;
			}

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv.xy = TRANSFORM_TEX(v.uv, _MainTex);
				float3 L = normalize(_WorldSpaceLightPos0);
				o.normal = normalize(mul(unity_ObjectToWorld, float4(v.normal, 0.0)).xyz);
				o.lightColor.rgb = WrappedLight(L, o.normal, LIGHT_ATTENUATION(i));
				o.lightColor.a = dot(L, o.normal) + _TwincleFactor;
				o.viewDir = normalize(_WorldSpaceCameraPos - mul(unity_ObjectToWorld, v.vertex).xyz);

#if SPECULAR_ON
				o.reflect = normalize(reflect(L, o.normal));
#endif // SPECULAR_ON

#if MATCAP_ON
				float3 worldNorm = mul((float3x3)UNITY_MATRIX_V, o.normal);
				o.uv.zw = worldNorm.xy * 0.5 + 0.5;
#endif // MATCAP_ON
				UNITY_TRANSFER_FOG(o, o.vertex);

				return o;	
			}

			float4 frag(v2f i) : COLOR
			{
				half4 main_color = tex2D(_MainTex, i.uv.xy);
				half4 mask_color = tex2D(_MaskTex, i.uv.xy);	// R:SPECULAR, G:MATCAP, B:MADKCOLOR(D:0), A:RESERVE

#if EMISSIVE_ON
				half4 emissive_color = tex2D(_EmissiveTex, i.uv.xy);
				emissive_color.rgb *= _EmissiveFactor;
#endif // EMISSIVE_ON

				float3 viewDir = normalize(-i.viewDir);

#if MATCAP_ON
				float4 final_color = 1;
				half3 matcap_color = tex2D(_MatCapTex, i.uv.zw).rgb;
				half3 final_matcap = matcap_color * mask_color.ggg;

				//final_color.rgb = matcap_color * main_color.rgb * _MatCapFactor;
				//final_matcap = clamp(final_matcap, 0, 0.25);
				final_color.rgb = main_color.rgb * (final_matcap * _MatCapFactor);
				

				final_color.rgb += main_color.rgb * (1 - mask_color.ggg);
				//final_color.rgb = lerp(main_color.rgb, final_color.rgb, mask_color.ggg);

				//final_color.rgb = lerp(main_color.rgb, matcap_color, mask_color.ggg);
				//final_color.rgb = matcap_color * mask_color.ggg;
				//final_color.rgb += main_color.rgb * (1 - mask_color.ggg);
				//final_color.rgb = main_color.rgb * matcap_color.rgb;
#else
				float4 final_color = 1;
				final_color.rgb = main_color;
#endif // MATCAP_ON

#if EMISSIVE_ON
				final_color.rgb *= (i.lightColor.rgb * (1 - emissive_color.aaa));
#else
				final_color.rgb *= i.lightColor.rgb;
#endif // EMISSIVE_ON

				
#if DIFFUSE_ON
				final_color.rgb *= _Diffuse;
#endif // DIFFUSE_ON

#if SPECULAR_ON
				float3 specular = saturate(dot(normalize(i.reflect), viewDir));
				specular = _SpecularIntensity * main_color * pow(specular, _Shininess);
				specular *= mask_color.rrr * i.lightColor.aaa;
				final_color.rgb += specular ;
#endif // SPECULAR_ON

#if RIM_LIGHT_ON
				//float3 L = normalize(_WorldSpaceLightPos0);
				//final_color.rgb += main_color * saturate(dot(L, i.normal)) * smoothstep(_RimMin, _RimMax, 1 - dot(-viewDir, i.normal)) * _RimPower;
				final_color.rgb += clamp(main_color, 0.5, 2 ) * smoothstep(_RimMin, _RimMax, 1 - dot(-viewDir, i.normal)) * _RimPower * mask_color.rrr;
#endif // RIM_LIGHT_ON

#ifdef EMISSIVE_ON
				final_color.rgb += emissive_color.rgb;
#endif // EMISSIVE_ON

				final_color.a = main_color.a;
				UNITY_APPLY_FOG(i.fogCoord, final_color);

				return final_color;
			}
					
			ENDCG
		}
	}

	Fallback "VertexLit"
	CustomEditor "CharacterCustomMaterialEditor"
}
