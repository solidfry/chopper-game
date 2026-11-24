// NukeRing: flat ring strokes for the nuke blast (Editor/Setup/NukeBlastAssets builds the ring mesh: uv.x goes once
// round the ring, uv.y from the inner edge 0 to the outer edge 1). Glowing shock rings round the mushroom cap
// (additive, HDR) and white wind strokes on the ground (alpha blend). Several thin streaks inside the band, broken
// into arcs by noise; _Fade (0..1, set by NukeBlastSystem per renderer) fades it out and eats the arcs away.
Shader "Chopper/NukeRing"
{
    Properties
    {
        [HDR] _Colour ("Colour", Color) = (2.4, 0.8, 0.1, 1)
        _Streaks ("Streaks across the band", Range(1, 8)) = 3
        _Thin ("Streak thinness", Range(1, 40)) = 10
        _Gaps ("Arc gaps (0 = full ring)", Range(0, 0.9)) = 0.25
        _Arcs ("Arc count round the ring", Range(1, 24)) = 7
        _Fade ("Fade 0..1", Range(0, 1)) = 0
        _Seed ("Seed", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            Name "NukeRing"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Colour;
                float _Streaks;
                float _Thin;
                float _Gaps;
                float _Arcs;
                float _Fade;
                float _Seed;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float Hash(float x) { return frac(sin(x * 127.1 + _Seed * 13.7) * 43758.5453); }

            // Smooth 1D noise that wraps round the ring (period = cells).
            float RingNoise(float u, float cells)
            {
                float x = u * cells;
                float i = floor(x);
                float f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(Hash(fmod(i, cells)), Hash(fmod(i + 1.0, cells)), f);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float u = input.uv.x;
                float v = input.uv.y;
                // Thin streaks across the band, each wobbling in and out along the ring.
                float streaks = 0.0;
                for (int s = 0; s < 8; s++)
                {
                    if (s >= (int)_Streaks) break;
                    float centre = (s + 0.5) / _Streaks + (RingNoise(u + s * 0.37, 9.0) - 0.5) * 0.35 / _Streaks;
                    float w = 1.0 - saturate(abs(v - centre) * _Thin * _Streaks * 0.5);
                    streaks = max(streaks, w * w * (0.6 + 0.4 * RingNoise(u * 3.0 + s, 17.0)));
                }
                // The band is brightest along its middle and soft at its edges.
                float band = sin(saturate(v) * 3.14159);
                // Break the ring into arcs; the gaps widen as it fades.
                float arcs = saturate((RingNoise(u, _Arcs) - (_Gaps + _Fade * 0.6)) * 6.0);
                float a = saturate(streaks * band * arcs * (1.0 - _Fade));
                return half4(_Colour.rgb * a, a * _Colour.a);
            }
            ENDHLSL
        }
    }
}
