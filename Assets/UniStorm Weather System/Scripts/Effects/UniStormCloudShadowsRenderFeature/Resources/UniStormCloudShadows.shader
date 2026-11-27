Shader "UniStorm/URP/UniStormCloudShadows"
{
	Properties
	{
		_MainTex("Color (RGB) Alpha (A)", 2D) = "white" { }
		_CloudShadowIntensity("Cloud Shadow Intensity", Range(0.0, 1.0)) = 1.0
		_CloudMovementSpeed("Cloud Movement Speed", Range(-0.5, 0.5)) = -0.06
	}
		SubShader
		{
			Cull Off ZWrite Off ZTest Always

			Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }

			Pass
			{
				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				#pragma multi_compile_instancing

				#include "UnityCG.cginc"

				struct appdata
				{
					float4 vertex : POSITION;
					float2 uv : TEXCOORD0;

					UNITY_VERTEX_INPUT_INSTANCE_ID //Insert
				};

				struct v2f
				{
					float2 uv : TEXCOORD0;
					float4 vertex : SV_POSITION;

					UNITY_VERTEX_INPUT_INSTANCE_ID //Insert
					UNITY_VERTEX_OUTPUT_STEREO //Insert
				};

				v2f vert(appdata v)
				{
					v2f o;

					UNITY_SETUP_INSTANCE_ID(v); //Insert
					UNITY_INITIALIZE_OUTPUT(v2f, o); //Insert
					UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); //Insert

					o.vertex = UnityObjectToClipPos(v.vertex);
					o.uv = v.uv;

					return o;
				}

				UNITY_DECLARE_SCREENSPACE_TEXTURE(_MainTex);
				//sampler2D _MainTex;
				UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
				//sampler2D _CameraDepthNormalsTexture;
				float4x4 _CamToWorld;

				sampler2D _CloudTex;
				float _CloudTexScale;
				float _CloudHeight;
				float _CloudShadowIntensity;
				float _CloudMovementSpeed;
				float _Fade;
				half  _normalY;

				half4 _ShadowColor;

				fixed _BottomThreshold;
				fixed _TopThreshold;

				uniform float4 _SunDirection;

				float4 _SunCloudOffset;

				half4 frag(v2f i) : SV_Target
				{
					UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i); //Insert

					float2 screenUV = UnityStereoTransformScreenSpaceTex(i.uv);
					float4 source = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, screenUV);

					float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenUV);

					// Do not project cloud shadows onto the skybox/background.
					#if defined(UNITY_REVERSED_Z)
					if (rawDepth <= 0.000001)
					{
						return source;
					}
					#else
					if (rawDepth >= 0.999999)
					{
						return source;
					}
					#endif

					half3 normal = half3(0, _normalY, 0); //0.875
					half ShadowThreshold = normal.g;
					half ShadowThreshold2 = normal.r;

					// Convert raw depth into view-space distance before reconstructing world position.
					// The old version used raw depth directly, which stretched the projection across terrain.
					float eyeDepth = LinearEyeDepth(rawDepth);

					float2 p11_22 = float2(unity_CameraProjection._11, unity_CameraProjection._22);
					float3 vpos = float3((i.uv * 2.0 - 1.0) / p11_22, -1.0) * eyeDepth;

					// w = 1 because this is a position, not a direction.
					float3 worldPos = mul(_CamToWorld, float4(vpos, 1.0)).xyz;

					float2 offset = _Time.x * float2(_CloudMovementSpeed, 0);

					// Stable fake sun-following projection.
					// The old cloud-plane projection divided by sunDir.y, which can flip/explode
					// near the horizon or when the directional light crosses above/below the terrain.
					// Keep the projection straight down in world XZ, then add a bounded UV offset
					// calculated from the sun direction in C#.
					float2 cloudUV = -worldPos.xz * _CloudTexScale + offset + _SunCloudOffset.xy;
					half4 ShadowColor = tex2D(_CloudTex, cloudUV);

					float up = dot(float3(0, 1, 0), normal);
					up = step(ShadowThreshold * ShadowThreshold2, up);

					// Keep fade based on normalized depth instead of world distance, so existing Fade values stay usable.
					half fadeDepth = Linear01Depth(rawDepth);
					half safeFade = max(_Fade, 0.0001);
					half falloff = saturate(fadeDepth / safeFade);

					float4 col = lerp(source, source / max(ShadowColor, 0.0001), -up * _CloudShadowIntensity * ShadowThreshold * (1.0f - falloff));
					return col;
				}
				ENDCG
			}
		}
}
