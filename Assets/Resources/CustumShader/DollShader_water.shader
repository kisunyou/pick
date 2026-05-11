// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "CustumShader/DollShader_water" {
    Properties {
		_Color ("Color", Color) = (1,1,1,1)
		_SpecCol ("SpecColor", Color) = (1,1,1,1)
    _SpecPower ("SpecPower", Range(0, 32)) = 2
    _AmbCol ("AmbientColor", Color) = (0,0,0,0)
		_MainTex ("Texture", 2D) = "gray" {}
		_BumpMap ("Bumpmap", 2D) = "bump" {}
		// _Detail ("Detail", 2D) = "gray" {}
		_MatCap ("MatCap (RGB)", 2D) = "gray" {}
    //_mcIntensity("MatCap Intnesity", Range(0,1)) = 1
    }

    SubShader {
      Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent"}
      LOD 200

      CGPROGRAM
      #pragma surface surf Dolll vertex:vert noforwardadd alpha:fade
      // #pragma glsl
      // #pragma target 3.0
      // #include "UnityCG.cginc"

      struct Input {
          float2 uv_MainTex;
          float2 uv_BumpMap;
          // float2 uv_Detail;
          float2 matcapUV;
      };

      fixed4 _Color;
      float3 _SpecCol;
      float3 _AmbCol;
      fixed _SpecPower;
      sampler2D _MainTex;
      sampler2D _BumpMap;
      // sampler2D _Detail;
      sampler2D _MatCap;
      //half _mcIntensity;

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

			half3 diff = max (0, dot (s.Normal, lightDir));
			
			float nh = max (0, dot (s.Normal, h));
      diff *= atten; //그림자 뿌릴려면 반드시 있어야함. 자체그림자도.
      diff = lerp (_AmbCol.rgb, _Color.rgb, diff);

			half3 spec = pow (nh, _SpecPower ) * _LightColor0.rgb * _SpecCol.rgb * s.Albedo ;
			
			half4 c;
			c.rgb = s.Albedo * diff   + spec ;

      c.a = s.Alpha;
      
			return c;
		}


      void surf (Input IN, inout SurfaceOutput o)
      {
          //half4 mc = tex2D(_MatCap, IN.matcapUV);
          half4 co = tex2D (_MainTex, IN.uv_MainTex);
          o.Albedo = co  ;
          o.Alpha = _Color.a;
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
