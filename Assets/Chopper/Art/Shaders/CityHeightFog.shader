// CityHeightFog: full-screen height fog for the CityForge maps (URP Full Screen Pass Renderer Feature, before post
// processing, needs the depth texture; Setup step 9 adds it). Thick on the low ground, thinning out with height:
// density(y) = d * exp(-(y - base) / falloff), integrated along the view ray from the camera to the depth buffer
// (analytic, exponential height fog). The sky (far plane, 20 km out) gets the same rule, so the horizon hazes while
// the sky straight up stays clear.
//
// Global values (Presentation CityAtmosphereSystem, from the city scene's CityAtmosphere):
//   _CityFogColour  rgb linear
//   _CityFogParams  x density at the base (1/m; 0 = off), y base height (m), z falloff (m), w most opacity
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

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 colour = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float density = _CityFogParams.x;
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
                colour.rgb = lerp(colour.rgb, _CityFogColour.rgb, fog);
                return colour;
            }
            ENDHLSL
        }
    }
}
