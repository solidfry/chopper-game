// ZoneDome: an additive glow shell for a zone (Presentation NukeZoneSystem: the nuke pick-up range). Hemisphere mesh,
// uv.y 0 at the ground ring to 1 at the top. Bright at the rim (where the shell turns away from the eye), a band where
// it meets the ground (soft against scene depth), faint stripes that climb slowly. No depth write, both sides.
//
// Written without the editor (20 Nov 2026): check it compiles; tune the colour on Data/Rendering/NukeZone.mat.
Shader "Chopper/ZoneDome"
{
    Properties
    {
        [HDR] _Colour ("Colour", Color) = (2.5, 0.4, 1.3, 1)
        _Rim ("Rim power", Range(0.5, 8)) = 2.5
        _Fill ("Fill (inside the rim)", Range(0, 1)) = 0.06
        _Band ("Ground band height (uv)", Range(0.01, 0.5)) = 0.12
        _Stripes ("Stripes", Range(0, 40)) = 14
        _Speed ("Stripe climb speed", Range(-2, 2)) = 0.25
        _Soft ("Soft edge on geometry (m)", Range(0.01, 10)) = 1.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ZoneDome"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Colour;
                float _Rim;
                float _Fill;
                float _Band;
                float _Stripes;
                float _Speed;
                float _Soft;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 screen : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewWS = GetWorldSpaceViewDir(positionWS);
                o.uv = input.uv;
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewWS);
                float rim = pow(1.0 - abs(dot(n, v)), _Rim);
                float band = 1.0 - saturate(input.uv.y / _Band);
                float stripes = 0.5 + 0.5 * sin((input.uv.y - _Time.y * _Speed) * _Stripes * 6.2831853);
                float glow = rim + _Fill * (0.5 + stripes) + band * band * 1.5;

                // Soft where the shell meets solid geometry (the ground under the nuke, walls).
                float2 uv = input.screen.xy / input.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float self = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float soft = saturate((scene - self) / _Soft);
                // A bright line exactly at the contact, then the soft fade.
                float contact = saturate(1.0 - (scene - self) / (_Soft * 0.5));
                glow = glow * soft + contact * 0.8;

                return half4(_Colour.rgb * glow, 1);
            }
            ENDHLSL
        }
    }
}
