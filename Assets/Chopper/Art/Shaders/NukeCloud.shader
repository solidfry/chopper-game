// NukeCloud: the solid, puffy parts of the nuke blast (mushroom cap, stem, base puffs; meshes made by
// Editor/Setup/NukeBlastAssets, animated by Presentation NukeBlastSystem through a MaterialPropertyBlock).
// - Lit by the main light with soft wrap and a rim, so the lumps read as 3D cloud.
// - _Heat 0..1: glowing lava cracks (3D noise veins, scrolling) and a hot body colour; the system cools it over time.
// - _Boil: the surface churns (vertex push along the normal by moving noise).
// - _Fade 0..1: noise dissolve at the end (alpha clip), so it breaks up instead of popping out.
// Opaque with clip: no sorting problems with the smoke and the rings.
Shader "Chopper/NukeCloud"
{
    Properties
    {
        _Colour ("Cool cloud colour", Color) = (0.62, 0.52, 0.5, 1)
        _HotColour ("Hot body colour", Color) = (1, 0.72, 0.62, 1)
        [HDR] _Crack ("Crack glow", Color) = (6, 2.2, 0.4, 1)
        [HDR] _CrackCore ("Crack core (hottest)", Color) = (9, 6.5, 2, 1)
        _Heat ("Heat 0..1", Range(0, 1)) = 1
        _CrackScale ("Crack size (per object unit)", Range(0.5, 12)) = 3
        _CrackWidth ("Crack width", Range(0.02, 0.5)) = 0.16
        _Boil ("Boil (push, object units)", Range(0, 0.3)) = 0.06
        _BoilSpeed ("Boil speed", Range(0, 3)) = 0.6
        _Fade ("Dissolve 0..1", Range(0, 1)) = 0
        _Seed ("Seed", Float) = 0
        _Ambient ("Ambient", Range(0, 1)) = 0.35
        [HDR] _InnerGlow ("Inner glow (fire inside the cloud)", Color) = (4.5, 1.5, 0.3, 1)
        _InnerGlowPower ("Inner glow tightness", Range(0.5, 6)) = 1.8
        _InnerGlowCool ("Inner glow left when cool", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "NukeCloud"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Colour;
                float4 _HotColour;
                float4 _Crack;
                float4 _CrackCore;
                float _Heat;
                float _CrackScale;
                float _CrackWidth;
                float _Boil;
                float _BoilSpeed;
                float _Fade;
                float _Seed;
                float _Ambient;
                float4 _InnerGlow;
                float _InnerGlowPower;
                float _InnerGlowCool;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                return Noise(p) * 0.55 + Noise(p * 2.03 + 7.1) * 0.3 + Noise(p * 4.01 + 3.7) * 0.15;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float t = _Time.y * _BoilSpeed;
                float3 p = input.positionOS.xyz;
                float push = (Fbm(p * 2.2 + float3(_Seed, t, -t * 0.7)) - 0.5) * 2.0 * _Boil;
                p += input.normalOS * push;
                VertexPositionInputs position = GetVertexPositionInputs(p);
                output.positionCS = position.positionCS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(position.positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 p = input.positionOS + _Seed;
                float t = _Time.y * _BoilSpeed * 0.35;

                // Dissolve: noisy holes grow as _Fade goes to 1.
                clip(Fbm(p * 1.7 + 11.0) - _Fade * 1.05);

                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewWS);
                Light sun = GetMainLight();
                float wrap = saturate((dot(n, sun.direction) + 0.45) / 1.45);
                float rim = pow(1.0 - saturate(dot(n, v)), 3.0);
                float3 body = lerp(_Colour.rgb, _HotColour.rgb, saturate(_Heat * 1.3));
                float3 lit = body * (sun.color * wrap * (1.0 - _Ambient) + _Ambient) + body * rim * 0.35;

                // Lava cracks: thin bands where a moving noise field crosses 0.5; wider and brighter when hot.
                float field = Fbm(p * _CrackScale + float3(0, -t, t * 0.5));
                float band = 1.0 - saturate(abs(field - 0.5) / max(_CrackWidth * (0.4 + _Heat), 0.001));
                float pools = saturate((Fbm(p * _CrackScale * 0.45 - t * 0.3) - (0.72 - _Heat * 0.3)) * 6.0);
                float glow = saturate(band * band + pools) * _Heat;
                float3 crack = lerp(_Crack.rgb, _CrackCore.rgb, saturate(band * band * _Heat));
                // Lower faces glow more (lit from the fire below), as in a real mushroom's underside.
                float under = saturate(-n.y * 0.6 + 0.4);
                float3 colour = lit + crack * glow * (0.6 + under);
                // Fresh fire glows through the whole body while it is very hot.
                colour += _Crack.rgb * 0.25 * saturate(_Heat - 0.6) * 2.5;
                // Inner glow: fire seen through the cloud, strongest where the surface faces the viewer (the thick
                // middle), patchy by a slow noise; it stays on a little after the cracks have cooled.
                float facing = pow(saturate(dot(n, v)), _InnerGlowPower);
                float patches = 0.55 + 0.9 * Fbm(p * 1.3 + float3(t * 0.4, -t * 0.6, 0));
                float inner = facing * patches * lerp(_InnerGlowCool, 1.0, _Heat) * (1.0 - _Fade);
                colour += _InnerGlow.rgb * inner;
                return half4(colour, 1);
            }
            ENDHLSL
        }

        // Depth for the fog, soft particles and the depth prepass; same churn and dissolve as the colour pass.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Colour;
                float4 _HotColour;
                float4 _Crack;
                float4 _CrackCore;
                float _Heat;
                float _CrackScale;
                float _CrackWidth;
                float _Boil;
                float _BoilSpeed;
                float _Fade;
                float _Seed;
                float _Ambient;
                float4 _InnerGlow;
                float _InnerGlowPower;
                float _InnerGlowCool;
            CBUFFER_END

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            float Fbm(float3 p)
            {
                return Noise(p) * 0.55 + Noise(p * 2.03 + 7.1) * 0.3 + Noise(p * 4.01 + 3.7) * 0.15;
            }

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                float t = _Time.y * _BoilSpeed;
                float3 p = input.positionOS.xyz;
                p += input.normalOS * (Fbm(p * 2.2 + float3(_Seed, t, -t * 0.7)) - 0.5) * 2.0 * _Boil;
                output.positionCS = TransformObjectToHClip(p);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                clip(Fbm((input.positionOS + _Seed) * 1.7 + 11.0) - _Fade * 1.05);
                return 0;
            }
            ENDHLSL
        }
    }
}
