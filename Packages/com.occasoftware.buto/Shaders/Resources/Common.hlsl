#ifndef OCCASOFTWARE_BUTO_COMMON_INCLUDED
#define OCCASOFTWARE_BUTO_COMMON_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"


SamplerState linear_repeat_sampler;
SamplerState linear_clamp_sampler;
SamplerState point_clamp_sampler;


bool IsOrthographicPerspective()
{
	return unity_OrthoParams.w == 1.0;
}

float CheckDepthDirection(float depthRaw)
{
    #if UNITY_REVERSED_Z
        depthRaw = 1.0 - depthRaw;
    #endif
    return depthRaw;
}

float GetDepthEye(float depthRaw)
{
    if(IsOrthographicPerspective())
    {
        depthRaw = CheckDepthDirection(depthRaw);
        return lerp(_ProjectionParams.y, _ProjectionParams.z, depthRaw);
    }
                
    return LinearEyeDepth(depthRaw, _ZBufferParams);
}

float GetDepth01(float depthRaw)
{
    if(IsOrthographicPerspective())
    {
        return CheckDepthDirection(depthRaw);
    }
                
    return Linear01Depth(depthRaw, _ZBufferParams);
}

#endif