// ShockShell: a see-through sphere for the nuke blast (Presentation NukeBlastSystem, prefab built by
// Editor/Setup/NukeBlastAssets). Drawn on mesh particles (a sphere that grows; the particle colour's alpha fades it).
// It bends the scene behind it (URP opaque texture; Setup step 9 turns it on), strongest at the rim where the shell is
// seen edge-on: the shock front. _Tint adds a white shell for the condensation (Wilson) cloud; black = pure distortion.
// Without the opaque texture only the tint shows.
Shader "Chopper/ShockShell"
{
    Properties
    {
        _Distort ("Distortion at the rim (screen share)", Range(0, 0.15)) = 0.04
        [HDR] _Tint ("Tint (condensation white; black = distortion only)", Color) = (0, 0, 0, 0)
        _RimPower ("Rim power (higher = thinner front)", Range(0.5, 12)) = 4
        _Fill ("Fill inside the rim", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "ShockShell"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Distort;
                float4 _Tint;
                float _RimPower;
                float _Fill;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float4 screen : TEXCOORD2;
                float4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(position.positionWS);
                output.screen = ComputeScreenPos(position.positionCS);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = normalize(input.viewWS);
                float rim = pow(saturate(1.0 - abs(dot(n, v))), _RimPower);
                float k = saturate(rim + _Fill) * input.color.a;
                float2 uv = input.screen.xy / input.screen.w;
                float2 bend = mul((float3x3)UNITY_MATRIX_V, n).xy * (_Distort * k);
                float3 scene = SampleSceneColor(uv - bend);
                float3 colour = scene + _Tint.rgb * k;
                // Where the shell is faint, keep the scene as it is (alpha); the bend shows only near the front.
                return half4(colour, saturate(k * 3.0 + _Tint.a * k));
            }
            ENDHLSL
        }
    }
}
