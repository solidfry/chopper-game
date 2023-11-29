Shader "OccaSoftware/Buto/RenderFog"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        
        ZWrite Off
        Cull Off
        ZTest Always
        
        Pass
        {
            Name "RenderFog"

            HLSLPROGRAM
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Common.hlsl"
            #include "Fog.hlsl"
            
			#pragma vertex Vert
            #pragma fragment Fragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _BUTO_SELF_ATTENUATION_ENABLED
            #pragma multi_compile _ _BUTO_HORIZON_SHADOWING_ENABLED
            #pragma multi_compile _ _BUTO_ANALYTIC_FOG_ENABLED
            #pragma multi_compile _ _LIGHT_COOKIES

            #pragma multi_compile_local_fragment _ _IS_UNITY_WEBGL
            
            float4 Fragment (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                
                float3 color;
                float alpha;
                SampleVolumetricFog(input.texcoord, color, alpha);
                
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}