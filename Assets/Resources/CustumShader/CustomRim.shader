// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "CustumShader/CustomRim" 
{
	Properties 
	{
		_Color("Rim Color", color) = (0,0,0,1)
	    //_MainTex("Main Tex", 2D) = "white" {}
		
		//_RimColor("Rimcolor", Color) = (1.0,0.9,0.0)
        _RimPower("Rimpower", Range(0,1)) = 0
		_RimFactor("RimFactor", Range(0,4)) = 0
		//_BumpMap ("Normal map", 2D) = "bump" {}
		_Emissive ("Emissive", Color) = (0,0,0,1)
	}
 
	SubShader 
	{
		Tags 
		{ 
			"RenderType"="Opaque"
		}
			
       		Cull back

			CGPROGRAM
			#pragma surface surf CustomRim addshadow
			#pragma glsl
			#pragma target 3.0
			#pragma skip_variants LIGHTPROBE_SH POINT_COOKIE DIRECTIONAL_COOKIE SHADOWS_DEPTH SHADOWS_CUBE 

			//half _RampSmooth;
			//half _RampThreshold;
			//half _Detail;


			fixed3 _Color;
			//sampler2D _MainTex;
			//fixed3 _RimColor;
			fixed _RimPower;
			fixed _RimFactor;

			float _Shininess;
			half _SpecSmooth;
			fixed _SpecDir;

			fixed3 _Emissive;

			uniform half4 unity_FogStart;
			uniform half4 unity_FogEnd;

			struct Input {
					half2 uv_MainTex;
					half fog;
				};

		
			void vert(inout appdata_full v, out Input data) {
				UNITY_INITIALIZE_OUTPUT(Input, data);
				float pos = length(mul(UNITY_MATRIX_MV, v.vertex).xyz);
				float diff = unity_FogEnd.x - unity_FogStart.x;
				float invDiff = 1.0f / diff;
				data.fog = clamp((unity_FogEnd.x - pos) * invDiff, 0, 1.0);
			}
		
			void color(Input IN, SurfaceOutput o, inout fixed4 color) {

				UNITY_APPLY_FOG_COLOR(IN.fog, color, unity_FogColor);
			}

			inline half4 LightingCustomRim (SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
			{
                
				//fixed ndl = max(0.0, dot(s.Normal, lightDir)*0.5 + 0.5); 
				//float ndl = saturate(dot(s.Normal, lightDir) * 0.5 + 0.5); 
				/* fixed4 dta = saturate(s.Gloss + 0.6) - 0.5	; 
				ndl = ndl - (1.1- dta - 0.5 ) * _Detail * 0.7 ; */
				//fixed3 ramp = smoothstep(_RampThreshold-_RampSmooth*0.5, _RampThreshold+_RampSmooth*0.5, ndl); 


				half3 rim = 1.0f - saturate(dot(viewDir - half3(0, -0.1, 0), s.Normal));
				rim = smoothstep(_RimPower, 1, rim * _RimFactor * _Color ) ;
				fixed4 final ;
				final.a = 1;
				
				//half3 fh = normalize(lightDir - half3(0, 0.3, 0) - viewDir * 2);
				//float fre = max(0, dot(s.Normal, fh ));
				//fre = smoothstep(0.5 , 1, fre) ;

				fixed3 em = _Emissive ; 
                //final.rgb =  _MainColor * ndl *  rim * _RimColor * atten + em * atten;
				final.rgb = rim + em;// *_LightColor0 + fixed3(0.02, 0.03, 0);//* unity_FogColor* unity_FogColor ;
				//fixed4 cutwidth2f = 1 - s.Alpha - _CutEmWidth ;
				//final.a = 1 ;//* _Opac; 임시
				
				//final.rgb = ndl;
				//final.rgb = lerp(final , final + _CutEmCol * 32 * _CutEmPow, pow(cutwidth2f, 50) );
				return final;
			}

			void surf (Input IN, inout SurfaceOutput o) {
				//fixed3 mt = tex2D(_MainTex, IN.uv_MainTex);
				o.Albedo = /*mt * */_Color ;
				}
			ENDCG
        
	} // subshader 끝

	//Fallback "VertexLit"
}



