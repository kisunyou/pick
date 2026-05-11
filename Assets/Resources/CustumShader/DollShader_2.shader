// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "CustumShader/DollShader2" {
	Properties{
		//_Color ("Color", Color) = (1,1,1,1)
		//_SpecCol ("SpecColor", Color) = (1,1,1,1)
		//_SpecPower ("SpecPower", Range(0, 32)) = 2
		//_AmbCol ("AmbientColor", Color) = (0,0,0,0)
		_MainTex("Texture", 2D) = "gray" {}
		_BumpMap("Bumpmap", 2D) = "bump" {}
		// _Detail ("Detail", 2D) = "gray" {}
		_MatCap("MatCap (RGB)", 2D) = "gray" {}
		//_ShadowBiasFactor("Bias", Range(0,100)) = 1
			//_RimColor ("RimColor", Color) = (1,1,1,1)
	//		_RimPower ("RimPower", Range(0.2,10)) = 5
		_TwincleFactor("Twincle Factor", Range(0,1)) = 0.0
		_TwincleColor("Twincle Color", Color) = (1,1,1,1)

			//_mcIntensity("MatCap Intnesity", Range(0,1)) = 1
	}

		SubShader{
		  Tags { "RenderType" = "Opaque" }
		  LOD 200

		  CGPROGRAM
		  #pragma surface surf Dolll vertex:vert noforwardadd addshadow
		  #pragma glsl
		  #pragma target 3.0
		  #include "UnityCG.cginc"

		  struct Input {
			  float2 uv_MainTex;
			  float2 uv_BumpMap;
			  // float2 uv_Detail;
			  float2 matcapUV;
			  float3 viewDir;
			  float3 worldNormal;
			  float3 normal;
		  };

		//fixed3 _Color;
		//float3 _SpecCol;
		//float3 _AmbCol;
		//fixed _SpecPower;
		sampler2D _MainTex;
		sampler2D _BumpMap;
		// sampler2D _Detail;
		sampler2D _MatCap;
		//float3 _RimColor;
		//float _RimPower;
		//half _mcIntensity;

		half _TwincleFactor;
		fixed3 _TwincleColor;

		void vert(inout appdata_full v, out Input o)
		  {
			  UNITY_INITIALIZE_OUTPUT(Input,o); // 초기화. 이거 안하면 o 못씀.

			  // 공식은 제대로 이해 안되지만 스케일 해도 무방한 matcap 코드. 
			  float3 worldNorm = normalize(unity_WorldToObject[0].xyz * v.normal.x + unity_WorldToObject[1].xyz * v.normal.y + unity_WorldToObject[2].xyz * v.normal.z);
						worldNorm = mul((float3x3)UNITY_MATRIX_V, worldNorm);
			  o.matcapUV = worldNorm.xy* 0.5 + 0.5;

			  // 한줄짜리 matcap. 이건 모델 스케일링 하면 병신됨. 스케일이 커질수록 맷캡이 타일처럼 증가된다.
			  // o.matcapUV = float2(dot(UNITY_MATRIX_IT_MV[0].xyz,v.normal),dot(UNITY_MATRIX_IT_MV[1].xyz,v.normal)) * 0.5 + 0.5;  
		  }

	  inline half4 LightingDolll(SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
		  {
			  half3 h = normalize(lightDir + viewDir);

			  half3 ndl = max(0, dot(s.Normal, lightDir)) * 0.5 + 0.5; //내적

			  float nh = max(0, dot(s.Normal, h)); //스펙
			  ndl = smoothstep(0.5, 1.0, ndl * 1.3 ) *  atten;
			  half3 diff = lerp(fixed3(0.5, 0.15, 0.05) * 1.2/*_Ambcol*/, /*_Color.rgb */ 1, ndl);

			  half3 spec = pow(nh, 2/*_SpecPower*/) * _LightColor0.rgb * fixed3(0.6, 0.5, 0.2)/* _SpecCol.rgb */* s.Albedo * s.Specular  * 0.7;
			  //half rim = 1- saturate(dot(normalize(viewDir), s.Normal));
			  half rim = 1 - nh;
			  fixed3 r = pow(rim, 3) * fixed3(0.2, 0.2, 0.4)/* _RimColor.rgb*/;  //rim aka. fresnel


			  half4 c;
			  c.rgb = s.Albedo.rgb * diff * 1.5 + spec + r;

			  return c;
		  }


		void surf(Input IN, inout SurfaceOutput o)
		{
			half3 mc = tex2D(_MatCap, IN.matcapUV);
			//half3 co = (tex2D (_MainTex, IN.uv_MainTex).rgb * 1.0 - _TwincleFactor) + ( _TwincleColor * _TwincleFactor );
  half3 co = tex2D(_MainTex, IN.uv_MainTex).rgb * mc;
			o.Albedo = (co.rgb * (1.0 - _TwincleFactor)) + (_TwincleColor * _TwincleFactor);
			o.Specular = tex2D(_MainTex, IN.uv_MainTex).a;
			o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
		}
		ENDCG

			//Pass{
			//Name "ShadowCaster"
			//Tags {
			//	"LightMode" = "ShadowCaster"
			//}

			//Offset 1, 1

			////Cull[_DoubleSided]

			//	CGPROGRAM
			//	#pragma vertex vert
			//	#pragma fragment frag
			//	#include "UnityCG.cginc"
			//	#include "Lighting.cginc"
			//	#pragma fragmentoption ARB_precision_hint_fastest
			//	#pragma multi_compile_shadowcaster
			//	#pragma target 3.0

			//uniform half _ShadowBiasFactor;


			//struct VertexInput
			//{
			//	float4 vertex : POSITION;
			//	float3 normal : NORMAL;
			//};

			//struct VertexOutput
			//{
			//	V2F_SHADOW_CASTER;
			//};

			//VertexOutput vert(VertexInput v) {

			//	VertexOutput o = (VertexOutput)0;

			//	o.pos = UnityObjectToClipPos(v.vertex.xyz);
			//	o.pos.z += max(-1, min(unity_LightShadowBias.x / o.pos.w, 0)) * (_ShadowBiasFactor * 0.1);

			//	return o;

			//}

			//float4 frag(VertexOutput i) : COLOR{

			//	return 0;
			//}
			//	ENDCG
	  //}

		} //subshader end
} //shader end
