// LightBeam: additive light for the city's moving lights (Presentation CityTrafficSystem): searchlight beams (an open
// cone mesh) and the tail-light trails of the flying cars (crossed quads). GPU instanced (Graphics.RenderMeshInstanced),
// no depth write, both sides. Fades out along the mesh (uv.y 0 = source, 1 = far end), softly where it meets solid
// geometry (scene depth) and near the camera. _Facing 1 (beams): bright where the surface faces the eye, so a cone
// reads as a lit volume; _Facing 0 (trails): bright along the middle of the strip (uv.x).
//
// Written without the editor (6 Nov 2026): check it compiles; tune colours on the two materials step 8 makes.
Shader "Chopper/LightBeam"
{
    Properties
    {
        [HDR] _Colour ("Colour", Color) = (2, 2, 2, 1)
        _Falloff ("Fade along (power)", Range(0.2, 6)) = 1.5
        _Facing ("Beam (1) or strip (0)", Range(0, 1)) = 1
        _Soft ("Soft edge on geometry (m)", Range(0.01, 20)) = 4
        _Near ("Fade near the camera (m)", Range(0, 100)) = 20
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "LightBeam"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Colour;
                float _Falloff;
                float _Facing;
                float _Soft;
                float _Near;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 screen : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.screen = ComputeScreenPos(position.positionCS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float along = pow(saturate(1.0 - input.uv.y), _Falloff);
                float3 toEye = _WorldSpaceCameraPos - input.positionWS;
                float eyeDistance = length(toEye);
                float facing = abs(dot(normalize(input.normalWS), toEye / max(eyeDistance, 1e-4)));
                float across = 1.0 - abs(input.uv.x * 2.0 - 1.0);
                float shape = lerp(across * across, facing * facing, _Facing);

                float2 screenUV = input.screen.xy / max(input.screen.w, 1e-5);
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float soft = saturate((sceneDepth - input.screen.w) / max(_Soft, 0.01));
                float near = _Near > 0.0 ? saturate(eyeDistance / _Near) : 1.0;

                float amount = along * shape * soft * near;
                return half4(_Colour.rgb * amount, 0.0);
            }
            ENDHLSL
        }
    }
}
