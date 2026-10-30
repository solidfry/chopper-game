// Live cockpit screens. The display faces of the kit cockpits (material <kit>_Screen) show the screen pages that the
// cockpit screens view draws into one render texture (_ScreenTex, three square pages side by side; the faces' UVs pick
// their page). Self-lit: the page glows (HDR, for bloom) over dark glass, with faint scanlines. Writes depth, so the
// world-feed pass (HudLook.shader) sees the screens as cockpit and leaves them clean.
Shader "Chopper/CockpitScreen"
{
    Properties
    {
        [NoScaleOffset] _ScreenTex ("Pages (render texture)", 2D) = "black" {}
        _Glass ("Glass", Color) = (0.012, 0.03, 0.032, 1)
        _Glow ("Glow", Float) = 2.2
        _Scanlines ("Scanlines", Range(0, 1)) = 0.25
        _LineCount ("Scanlines per page height", Float) = 170
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        // The screen quads are single faces: draw both sides.
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Glass;
            half _Glow;
            half _Scanlines;
            float _LineCount;
        CBUFFER_END

        TEXTURE2D(_ScreenTex);
        SAMPLER(sampler_ScreenTex);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.uv = input.uv;
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            half4 Frag(Varyings input) : SV_Target
            {
                // The pages are premultiplied (UI Toolkit draws into a cleared, transparent texture).
                half4 page = SAMPLE_TEXTURE2D(_ScreenTex, sampler_ScreenTex, input.uv);
                half scan = 0.5h + 0.5h * cos(input.uv.y * _LineCount * 6.2831853);
                half3 lit = page.rgb * _Glow * lerp(1.0h, scan, _Scanlines);
                return half4(_Glass.rgb * (1.0h - page.a) + lit, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing

            half FragDepth(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
