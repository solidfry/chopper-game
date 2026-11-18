// CityHeightFog: full-screen height fog for the CityForge maps (URP Full Screen Pass Renderer Feature, before post
// processing, needs the depth texture; Setup step 9 adds it). Thick on the low ground, thinning out with height:
// density(y) = d * exp(-(y - base) / falloff), integrated along the view ray from the camera to the depth buffer
// (analytic, exponential height fog). The sky (far plane, 20 km out) gets the same rule, so the horizon hazes while
// the sky straight up stays clear.
//
// Global values (Presentation CityAtmosphereSystem, from the city scene's CityAtmosphere):
//   _CityFogColour  rgb linear
//   _CityFogParams  x density at the base (1/m; 0 = off), y base height (m), z falloff (m), w most opacity
//   _CityFogEnvTint   rgb x fog colour (time of day), w x density (weather)      } from EnvironmentSystem (the match's
//   _CityFogEnvColour rgb weather fog colour, w how much it pulls (0..1)          } conditions, EnvironmentSettings);
//   _CityFogEnvGlow   x how much glow (time of day: 1 night, low by day), y 1 = set (0 = ignore the three)
//   _CityFogGlow    rgb linear fog colour low down (city light from below), w height above the base (m) where the
//                   colour has turned to _CityFogColour. Bright (HDR) emission partly shines through the fog, so bloom
//                   gives neon halos in it (owner's references, 18 Nov 2026).
//
// Written without the editor (4 Nov 2026): check it compiles and tune the numbers in the city JSON.
Shader "Hidden/Chopper/CityHeightFog"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "CityHeightFog"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _CityFogColour;
            float4 _CityFogParams;
            float4 _CityFogGlow;
            float4 _CityFogEnvTint;
            float4 _CityFogEnvColour;
            float4 _CityFogEnvGlow;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 colour = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float density = _CityFogParams.x;
                if (density <= 0.0)
                    return colour;
                bool env = _CityFogEnvGlow.y > 0.5;
                if (env)
                    density *= _CityFogEnvTint.w;
                if (density <= 0.0)
                    return colour;

                float depth = SampleSceneDepth(uv);
            #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
            #endif
                float3 world = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 camera = _WorldSpaceCameraPos;
                float3 ray = world - camera;
                float distance = length(ray);

                float falloff = max(1.0, _CityFogParams.z);
                float k = clamp(ray.y / falloff, -80.0, 80.0);
                float atCamera = exp(-clamp((camera.y - _CityFogParams.y) / falloff, -80.0, 80.0));
                float along = abs(k) > 1e-4 ? (1.0 - exp(-k)) / k : 1.0;
                float amount = density * distance * atCamera * along;
                float fog = min(1.0 - exp(-amount), _CityFogParams.w);
                // Fog colour by the height of the fog the ray crosses (its lowest point weighs most: the fog is
                // densest there): the glow colour low down, the fog colour higher up.
                float low = min(camera.y, world.y) - _CityFogParams.y;
                float glowHeight = max(1.0, _CityFogGlow.w);
                float3 baseColour = _CityFogColour.rgb;
                float3 glowColour = _CityFogGlow.rgb;
                if (env)
                {
                    baseColour = lerp(baseColour * _CityFogEnvTint.rgb, _CityFogEnvColour.rgb, _CityFogEnvColour.w);
                    glowColour = lerp(baseColour, lerp(glowColour * _CityFogEnvTint.rgb, _CityFogEnvColour.rgb, _CityFogEnvColour.w * 0.5),
                                      saturate(_CityFogEnvGlow.x));
                }
                float3 fogColour = lerp(glowColour, baseColour, saturate(low / glowHeight));
                // HDR emission above 1 keeps 60 % of its excess through the fog: lights glow in it instead of fading.
                float3 excess = max(colour.rgb - 1.0, 0.0);
                colour.rgb = lerp(colour.rgb, fogColour, fog) + excess * fog * 0.6;
                return colour;
            }
            ENDHLSL
        }
    }
}
