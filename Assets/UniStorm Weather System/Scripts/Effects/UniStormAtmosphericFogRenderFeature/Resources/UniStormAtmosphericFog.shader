Shader "UniStorm/URP/UniStormAtmosphericFog"
{
	Properties
	{
		_MainTex("Base (RGB)", 2D) = "black" {}
		_NoiseTex("Noise Texture", 2D) = "white" {}
		_UpperColor("-", Color) = (.5, .5, .5, .5)
		_BottomColor("-", Color) = (.5, .5, .5, .5)
		_FogBlendHeight("-", Range(0, 1)) = 1.0
		_FogGradient("-", Range(0, 1)) = 1.0
		_SpecColor("Specular Color", Color) = (1.0,1.0,1.0,1.0)
		_Shininess("Shininess", Float) = 10
		_Color("Color Tint", Color) = (1.0,1.0,1.0,1.0)

		_SunColor("Sun Color", Color) = (1, 0.99, 0.87, 1)
		_MoonColor("Moon Color", Color) = (1, 0.99, 0.87, 1)
		_SunIntensity("Sun Intensity", float) = 2.0
		_MoonIntensity("Moon Intensity", float) = 1.0

		_SunAlpha("Sun Alpha", float) = 550
		_SunBeta("Sun Beta", float) = 0.95

		_SunVector("Sun Vector", Vector) = (0.269, 0.615, 0.740, 0)
		_MoonVector("Moon Vector", Vector) = (0.269, 0.615, 0.740, 0)

		_SunControl("Sun Alpha", float) = 1
		_MoonControl("Moon Alpha", float) = 1

		[Toggle] _EnableDithering("Enable Dithering", Float) = 0
		[Toggle] _VRSinglePassEnabled("VR Enabled", Float) = 0
	}

		CGINCLUDE

#include "UnityCG.cginc"
#include "AutoLight.cginc"
#include "Lighting.cginc"
#pragma multi_compile DIRECTIONAL POINT SPOT
#pragma multi_compile_instancing
#pragma enable_d3d11_debug_symbols

			UNITY_DECLARE_SCREENSPACE_TEXTURE(_MainTex);
		UNITY_DECLARE_TEX2D(_NoiseTex);
		UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

		uniform float4 _HeightParams;
		uniform float4 _DistanceParams;

		int4 _SceneFogMode;
		float4 _SceneFogParams;

#ifndef UNITY_APPLY_FOG
		half4 unity_FogColor;
		half4 unity_FogDensity;
#endif

		half4 _UpperColor;
		half4 _BottomColor;
		float _FogBlendHeight;
		float _FogGradientHeight;
		uniform float _Shininess;
		uniform float4 _Color;
		half3 _SunVector;
		half3 _MoonVector;
		uniform float4 _MainTex_TexelSize;
		uniform float4x4 _InvViewProj;
		uniform float4 _CameraWS;

		half3 _SunColor;
		half3 _MoonColor;
		half _SunIntensity;
		half _MoonIntensity;
		half _SunAlpha;
		half _SunBeta;

		uniform float _EnableDithering;
		uniform float _VRSinglePassEnabled;

		float _SunControl;
		float _MoonControl;

		struct appdata
		{
			float4 vertex : POSITION;
			float3 uv : TEXCOORD0;
			float3 normal : NORMAL;

			UNITY_VERTEX_INPUT_INSTANCE_ID
		};

		struct v2f
		{
			float4 pos : SV_POSITION;
			float2 uv1 : TEXCOORD0;
			float2 uv_depth : TEXCOORD1;
			float3 worldPos : TEXCOORD2;
			float3 normalDir : TEXCOORD3;

			UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
		};

		float HenyeyGreenstein(float sundotrd, float g)
		{
			float gg = g * g;
			return (1.0f - gg) / pow(1.0f + gg - 2.0f * g * sundotrd, 1.5f);
		}

		v2f vert(appdata v)
		{
			v2f o;

			UNITY_SETUP_INSTANCE_ID(v);
			UNITY_INITIALIZE_OUTPUT(v2f, o);
			UNITY_TRANSFER_INSTANCE_ID(v, o);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

			o.worldPos = mul(unity_ObjectToWorld, v.vertex);
			o.normalDir = UnityObjectToWorldNormal(v.normal);

			v.vertex.z = 0.1;
			o.pos = UnityObjectToClipPos(v.vertex);

			o.uv1 = v.uv.xy;
			o.uv_depth = v.uv.xy;

#if UNITY_UV_STARTS_AT_TOP
			if (_MainTex_TexelSize.y < 0)
			{
				o.uv1.y = 1 - o.uv1.y;
				o.uv_depth.y = 1 - o.uv_depth.y;
			}
#endif

			return o;
		}

		half ComputeFogFactor(float coord)
		{
			float fogFac = 0.0;
			if (_SceneFogMode.x == 1)
			{
				fogFac = coord * _SceneFogParams.z + _SceneFogParams.w;
			}
			if (_SceneFogMode.x == 2)
			{
				fogFac = _SceneFogParams.y * coord;
				fogFac = exp2(-fogFac);
			}
			if (_SceneFogMode.x == 3)
			{
				fogFac = _SceneFogParams.x * coord;
				fogFac = exp2(-fogFac * fogFac);
			}
			return saturate(fogFac);
		}

		float ComputeDistance(float3 camDir, float zdepth)
		{
			float dist;
			if (_SceneFogMode.y == 1)
				dist = length(camDir);
			else
				dist = zdepth * _ProjectionParams.z;

			dist -= _ProjectionParams.y;
			return dist;
		}

		float ComputeHalfSpace(float3 wsDir)
		{
			float3 wpos = _CameraWS + wsDir;
			float FH = _HeightParams.x;
			float3 V = wsDir;
			float3 P = wpos;
			float3 aV = _HeightParams.w * V;
			float FdotC = _HeightParams.y;
			float k = _HeightParams.z;
			float FdotP = P.y - FH;
			float FdotV = wsDir.y;
			float c1 = k * (FdotP + FdotC);
			float c2 = (1 - 2 * k) * FdotP;
			float g = min(c2, 0.0);
			g = -length(aV) * (c1 - g * g / abs(FdotV + 1.0e-5f));
			return g;
		}

		float3 getNoise(float2 uv)
		{
			float3 noise = UNITY_SAMPLE_TEX2D(_NoiseTex, uv * 100 + _Time * 50);
			noise = mad(noise, 2.0, -0.5);
			return noise / 255;
		}

		float3 ReconstructWorldPos(float2 uv, float rawDepth)
		{
			float4 clipPos = float4(uv * 2.0 - 1.0, rawDepth, 1.0);
#if UNITY_UV_STARTS_AT_TOP
			clipPos.y *= -1.0;
#endif
			float4 worldPos = mul(_InvViewProj, clipPos);
			return worldPos.xyz / max(worldPos.w, 1.0e-5f);
		}

		half4 ComputeFog(v2f i, bool distance, bool height) : SV_Target
		{
			UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

			float2 sampleUV = UnityStereoTransformScreenSpaceTex(i.uv_depth);

			float3 sceneColor = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_MainTex, sampleUV).rgb;
			float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampleUV);
			float dpth = Linear01Depth(rawDepth);

			float3 wsPos = ReconstructWorldPos(sampleUV, rawDepth);
			float3 wsDir = wsPos - _CameraWS.xyz;

			float g = _DistanceParams.x;
			if (distance)
				g += ComputeDistance(wsDir, dpth);
			if (height)
				g += ComputeHalfSpace(wsDir);

			half fogFac = ComputeFogFactor(max(0.0, g));

			float3 v = normalize(wsDir);
			half4 c_sky = _BottomColor;

			float rddotup = dot(float3(0, 1, 0), v);
			float HorizonStep = smoothstep(-0.1, 0.18, rddotup);

			half3 c_sun =
				_SunColor *
				min(pow(max(0, dot(v, normalize(_SunVector.xyz))), _SunAlpha) * _SunBeta, 1) *
				clamp(_SunControl, 0.0, 1.0);

			half4 c_moon =
				half4(
					_MoonColor *
					min(pow(max(0, dot(v, normalize(_MoonVector.xyz))), _SunAlpha) * _SunBeta, 1) *
					clamp(_MoonControl, 0.0, 1.0),
					1
				);

			half4 finalColor;

			if (_EnableDithering == 1)
			{
				finalColor.rgb =
					c_sky.rgb +
					(c_sun * HorizonStep * _SunIntensity) +
					(c_moon.rgb * HorizonStep * _MoonIntensity) +
					(getNoise(sampleUV) * 1.5);

				finalColor.rgb *= c_sky.a;
				finalColor.a = c_sky.a;
			}
			else
			{
				finalColor = half4(
					c_sky.rgb +
					(c_sun * _SunIntensity) +
					(c_moon.rgb * _MoonIntensity),
					0
				);
			}

			return half4(
				lerp(
					finalColor.rgb,
					sceneColor,
					lerp(
						saturate(_FogBlendHeight * (wsPos.y - _CameraWS.y) * 0.007),
						1,
						fogFac
					)
				),
				0
			);
		}

			ENDCG

		SubShader
		{
			ZTest Always Cull Off ZWrite Off Fog{ Mode Off }

			Pass
			{
				Tags { "LightMode" = "ForwardBase" }
				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				half4 frag(v2f i) : SV_Target { return ComputeFog(i, true, true); }
				ENDCG
			}

			Pass
			{
				Tags { "LightMode" = "ForwardBase" }
				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				half4 frag(v2f i) : SV_Target { return ComputeFog(i, true, false); }
				ENDCG
			}

			Pass
			{
				Tags { "LightMode" = "ForwardBase" }
				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag
				half4 frag(v2f i) : SV_Target { return ComputeFog(i, false, true); }
				ENDCG
			}
		}

	Fallback Off
}