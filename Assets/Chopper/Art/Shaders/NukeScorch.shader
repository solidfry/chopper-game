// NukeScorch: the burnt ground under the nuke blast. A flat disc (uv 0..1 across; centre 0.5) laid just above the
// ground: dark in the middle with long radial burn streaks (spikes) out to the rim, and a faint hot glow in the
// centre while _Heat is up. _Fade (0..1, NukeBlastSystem) fades it at the end. Alpha blend, no depth write.
Shader "Chopper/NukeScorch"
{
    Properties
    {
        _Colour ("Burn colour", Color) = (0.05, 0.045, 0.04, 0.9)
        [HDR] _Glow ("Hot glow", Color) = (3, 0.9, 0.15, 1)
        _Spikes ("Streak count", Range(8, 120)) = 48
        _Heat ("Heat 0..1", Range(0, 1)) = 1
        _Fade ("Fade 0..1", Range(0, 1)) = 0
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Offset -2, -2

        Pass
        {
            Name "NukeScorch"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Colour;
                float4 _Glow;
                float _Spikes;
                float _Heat;
                float _Fade;
                float _Seed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float Hash(float x) { return frac(sin(x * 91.7 + _Seed * 17.3) * 43758.5453); }

            float AngleNoise(float a, float cells)
            {
                float x = a * cells;
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
                float2 d = input.uv * 2.0 - 1.0;
                float r = length(d);
                float a = atan2(d.y, d.x) / 6.2831853 + 0.5;
                // Each streak has its own reach; sharp spikes, a solid burn in the middle.
                float reach = 0.35 + 0.65 * pow(AngleNoise(a, _Spikes), 2.0);
                float spike = saturate((reach - r) / max(reach, 0.01) * 3.0);
                float core = saturate((0.42 - r) * 4.0);
                float burn = saturate(max(spike, core)) * saturate((1.0 - r) * 8.0);
                float alpha = burn * _Colour.a * (1.0 - _Fade);
                float glow = saturate((0.25 - r) * 6.0) * _Heat;
                float3 colour = _Colour.rgb + _Glow.rgb * glow;
                return half4(colour, saturate(alpha + glow * (1.0 - _Fade)));
            }
            ENDHLSL
        }
    }
}
