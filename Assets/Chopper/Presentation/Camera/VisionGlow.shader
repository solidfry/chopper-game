// The glow on a target in a special view (thermal, kinetic, deep scan; see VisionEffects). A soft round additive
// blob, unlit and with no fog, so it shows through haze. HDR colour feeds bloom. _ZTest Always = seen through walls.
Shader "Hidden/Chopper/VisionGlow"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+60" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest [_ZTest]
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _ZTest;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Round and soft: full in the middle, nothing at the quad's edge.
                float2 fromCentre = input.uv * 2.0 - 1.0;
                half falloff = saturate(1.0h - dot(fromCentre, fromCentre));
                return half4(_Color.rgb * falloff * falloff, 0.0h);
            }
            ENDHLSL
        }
    }
}
