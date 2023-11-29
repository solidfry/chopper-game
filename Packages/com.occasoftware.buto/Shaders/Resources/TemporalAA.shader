Shader "OccaSoftware/Buto/TemporalAA"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        
        ZWrite Off
        Cull Off
        ZTest Always
        
        Pass
        {
            Name "TemporalAA"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Common.hlsl"
            #include "TemporalAA.hlsl"

            #pragma vertex Vert
            #pragma fragment Fragment
            
            Texture2D _BUTO_HISTORY_TEX;
            Texture2D _ButoUpsampleTargetId;
            Texture2D _MotionVectorTexture;
            float _IntegrationRate;
            
            float4 Fragment (Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                
                float3 Color;
                float Alpha;
                float2 motionVector = _MotionVectorTexture.SampleLevel(point_clamp_sampler, input.texcoord, 0).xy;
                TAA(_BUTO_HISTORY_TEX, _ButoUpsampleTargetId, input.texcoord, _IntegrationRate, motionVector, GetDepth01(SampleSceneDepth(input.texcoord)), Color, Alpha);
                
                return float4(Color, Alpha);
            }
            ENDHLSL
        }
    }
}