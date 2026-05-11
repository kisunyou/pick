// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "CustumShader/DollShader_alpha" {
    Properties {
		_Color ("Color", Color) = (1,1,1,1)
		_SpecCol ("SpecColor", Color) = (1,1,1,1)
    _SpecPower ("SpecPower", Range(0, 32)) = 2
    _AmbCol ("AmbientColor", Color) = (0,0,0,0)
		_MainTex ("Texture", 2D) = "gray" {}
		_BumpMap ("Bumpmap", 2D) = "bump" {}
		// _Detail ("Detail", 2D) = "gray" {}
		_MatCap ("MatCap (RGB)", 2D) = "gray" {}
    _RimColor ("RimColor", Color) = (1,1,1,1)
    _RimPower ("RimPower", Range(0.2,10)) = 5
		_TwincleFactor("Twincle Factor", Range(0,1)) = 0.0
		_TwincleColor("Twincle Color", Color) = (1,1,1,1)
    //_mcIntensity("MatCap Intnesity", Range(0,1)) = 1
    }

    SubShader {
      Tags { "Queue" = "Transparent" "RenderType"="Transparent" }
      LOD 200

      CGPROGRAM
      #pragma surface surf Dolll vertex:vert noforwardadd alpha
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

      fixed4 _Color;
      float3 _SpecCol;
      float3 _AmbCol;
      fixed _SpecPower;
      sampler2D _MainTex;
      sampler2D _BumpMap;
      // sampler2D _Detail;
      sampler2D _MatCap;
      float3 _RimColor;
      float _RimPower;
      //half _mcIntensity;

	  half _TwincleFactor;
	  fixed3 _TwincleColor;

      void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input,o); // 초기화. 이거 안하면 o 못씀.
            
            // 공식은 제대로 이해 안되지만 스케일 해도 무방한 matcap 코드. 
            float3 worldNorm = normalize(unity_WorldToObject[0].xyz * v.normal.x + unity_WorldToObject[1].xyz * v.normal.y + unity_WorldToObject[2].xyz * v.normal.z);
					  worldNorm = mul((float3x3)UNITY_MATRIX_V, worldNorm) ;
            o.matcapUV = worldNorm.xy* 0.5 + 0.5;
            
            // 한줄짜리 matcap. 이건 모델 스케일링 하면 병신됨. 스케일이 커질수록 맷캡이 타일처럼 증가된다.
            // o.matcapUV = float2(dot(UNITY_MATRIX_IT_MV[0].xyz,v.normal),dot(UNITY_MATRIX_IT_MV[1].xyz,v.normal)) * 0.5 + 0.5;  
        }

	inline half4 LightingDolll (SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
		{
			half3 h = normalize (lightDir + viewDir); 

			half3 ndl = max (0, dot (s.Normal, lightDir)); //내적
			
			float nh = max (0, dot (s.Normal, h)); //스펙
			
      half3 diff = lerp (_AmbCol.rgb * atten, _Color.rgb, ndl);

			half3 spec = pow (nh, _SpecPower ) * _LightColor0.rgb * _SpecCol.rgb * 5;
      
      
	  half rim = 1- saturate(dot(normalize(viewDir), s.Normal));
	  //rim += 1 - nh;
      fixed3 r = pow(rim, 0.5 * _RimPower) * _RimColor.rgb * 3;  //rim aka. fresnel
      
			
			half4 c;
			c.rgb = s.Albedo * diff * 1.3 + spec + r;

      c.a = s.Alpha * c.r;
      
			return c;
		}


      void surf (Input IN, inout SurfaceOutput o)
      {
          half4 mc = tex2D(_MatCap, IN.matcapUV);
          half3 co = (tex2D (_MainTex, IN.uv_MainTex).rgb * 1.0 - _TwincleFactor) + ( _TwincleColor * _TwincleFactor );
          o.Albedo = co * mc * 1.6;
          o.Alpha = _Color.a;
          
          //half rim = 1.0 - saturate(dot(normalize(IN.viewDir), o.Normal));
          
          //o.Emission = pow((1-rim),_RimPower) * _RimColor.rgb;
          
          //o.Emission = fixed3(1,0,0) * tex2D(_MainTex, IN.uv_MainTex).a;;
          
          //matcap 이 0이면 안적용, 1이면 보이게 하려고 했는데..
          // half3 mcStr = 1 - _mcIntensity;
          //o.Albedo = co * mcStr  + co * mc * _mcIntensity  ; 

          o.Normal = UnpackNormal (tex2D (_BumpMap, IN.uv_BumpMap));
      }
      ENDCG
    } 
    Fallback "Diffuse" // 이것도 그림자 때문에 꼭 있어야함.
}
