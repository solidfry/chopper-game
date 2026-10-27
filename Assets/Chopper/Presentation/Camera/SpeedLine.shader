// Speed line streaks (stretched particle billboards, see SpeedEffects). Additive, unlit; HDR vertex colour feeds bloom.
// Soft at both ends and both edges, so a streak reads as a thin glowing line.
Shader "Hidden/Chopper/SpeedLine"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Across the streak: a sharp core. Along it: fade in, long tail.
                half across = 1.0h - abs(input.uv.y * 2.0h - 1.0h);
                half along = sin(saturate(input.uv.x) * PI);
                half shape = pow(across, 2.0h) * along;
                return half4(input.color.rgb * input.color.a * shape, 0.0h);
            }
            ENDHLSL
        }
    }
}
