// HeatHaze: shimmer over the lava (Presentation CityTrafficSystem draws it from the map's lava paths). Each quad is an
// upright billboard that turns round the vertical axis to face the camera: the mesh keeps the quad's foot point in
// POSITION and the corner in UV1 (x across in m, y up in m); UV0 = 0..1 across and up. The fragment reads the screen
// behind (URP opaque texture: Setup step 9 turns it on) at a rising, wobbling offset, strongest low down and close by,
// and adds a faint warm glow. Without the opaque texture the haze is only the faint glow.
//
// Written without the editor (6 Nov 2026): check it compiles; tune strength on Data/Rendering/HeatHaze.mat.
Shader "Chopper/HeatHaze"
{
    Properties
    {
        _Strength ("Distortion (screen share at 10 m)", Range(0, 0.05)) = 0.012
        _Scale ("Ripple size (1/m)", Range(0.05, 3)) = 0.6
        _Rise ("Rise speed (m/s)", Range(0, 10)) = 2.5
        [HDR] _Glow ("Warm glow", Color) = (0.08, 0.025, 0.005, 1)
        _Far ("Fade out by (m)", Range(50, 3000)) = 900
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "HeatHaze"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Strength;
                float _Scale;
                float _Rise;
                float4 _Glow;
                float _Far;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 corner : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 screen : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 foot = TransformObjectToWorld(input.positionOS.xyz);
                float3 toCamera = _WorldSpaceCameraPos - foot;
                toCamera.y = 0.0;
                float3 right = normalize(cross(float3(0.0, 1.0, 0.0), toCamera + float3(1e-4, 0.0, 0.0)));
                float3 world = foot + right * input.corner.x + float3(0.0, input.corner.y, 0.0);
                output.positionCS = TransformWorldToHClip(world);
                output.positionWS = world;
                output.uv = input.uv;
                output.screen = ComputeScreenPos(output.positionCS);
                return output;
            }

            float Ripple(float2 p)
            {
                return sin(p.x * 1.7 + sin(p.y * 1.3)) * 0.5 + sin(p.y * 2.3 - p.x * 0.7) * 0.35 + sin((p.x + p.y) * 3.1) * 0.15;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float distance = length(_WorldSpaceCameraPos - input.positionWS);
                // Strongest low down, gone at the top and the sides; fades with distance.
                float across = 1.0 - abs(input.uv.x * 2.0 - 1.0);
                float mask = saturate(across * 1.5) * saturate(1.0 - input.uv.y) * saturate(input.uv.y * 6.0);
                mask *= saturate(1.0 - distance / _Far);

                float2 screenUV = input.screen.xy / max(input.screen.w, 1e-5);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                mask *= saturate((sceneDepth - input.screen.w) / 2.0);     // soft where it meets the ground
                if (mask <= 0.001)
                    discard;

                float2 p = float2(input.positionWS.x + input.positionWS.z, input.positionWS.y - _Time.y * _Rise) * _Scale;
                float2 wobble = float2(Ripple(p), Ripple(p.yx + 17.0));
                float amount = _Strength * mask * sqrt(saturate(10.0 / max(distance, 1.0)));
                float3 behind = SampleSceneColor(screenUV + wobble * amount);
                return half4(behind + _Glow.rgb * mask, mask);
            }
            ENDHLSL
        }
    }
}
