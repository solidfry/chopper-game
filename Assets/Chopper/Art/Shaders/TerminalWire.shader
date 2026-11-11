// TerminalWire: the home screen city as a glowing terminal wireframe (Setup step 11 puts it on every renderer of
// HomeCity). Dark fill; grid lines laid on every face in world space (triplanar: each face takes the two axes along
// it), bright major lines and faint minor lines, anti-aliased with screen derivatives so they stay thin at any range;
// a bright rim where a surface turns away from the eye (silhouette outlines); a few lit "window" cells that blink
// slowly; and a scan band that climbs the towers. Unlit, opaque, with fog. HDR line colour: bloom makes it glow.
Shader "Chopper/TerminalWire"
{
    Properties
    {
        [HDR] _LineColour ("Line colour", Color) = (0.25, 1.6, 0.9, 1)
        _FillColour ("Fill colour", Color) = (0, 0.025, 0.012, 1)
        _GridSize ("Minor grid (m)", Float) = 4
        _MajorEvery ("Major line every n minor cells", Float) = 4
        _LineWidth ("Line width (px)", Range(0.3, 4)) = 1
        _MinorStrength ("Minor line strength", Range(0, 1)) = 0.15
        _RimPower ("Rim power", Range(0.5, 8)) = 5
        _RimStrength ("Rim strength", Range(0, 3)) = 0.45
        _WindowChance ("Lit window share", Range(0, 1)) = 0.12
        _WindowStrength ("Window strength", Range(0, 2)) = 0.35
        _ScanSpeed ("Scan speed (m/s)", Float) = 60
        _ScanHeight ("Scan cycle height (m)", Float) = 600
        _ScanWidth ("Scan width (m)", Float) = 18
        _ScanStrength ("Scan strength", Range(0, 3)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "TerminalWire"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _LineColour;
                float4 _FillColour;
                float _GridSize;
                float _MajorEvery;
                float _LineWidth;
                float _MinorStrength;
                float _RimPower;
                float _RimStrength;
                float _WindowChance;
                float _WindowStrength;
                float _ScanSpeed;
                float _ScanHeight;
                float _ScanWidth;
                float _ScanStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fog : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // 1 on a grid line of the given cell size, 0 between; width in pixels. Fades out where cells get smaller
            // than about 8 px on screen, so far grids do not merge into a solid glow.
            float GridLine(float2 p, float cell, float width)
            {
                float2 q = p / cell;
                float2 fw = max(fwidth(q), 1e-5);
                float2 d = abs(frac(q - 0.5) - 0.5) / fw;
                float fade = saturate(1 - (max(fw.x, fw.y) - 0.06) * 12);
                return (1 - saturate(min(d.x, d.y) - width * 0.5)) * fade;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 p = input.positionWS;
                float3 w = pow(abs(n), 6);
                w /= max(w.x + w.y + w.z, 1e-5);

                float major = _GridSize * _MajorEvery;
                // Plane across X (uses yz), across Y (xz), across Z (xy).
                float minorLines = GridLine(p.yz, _GridSize, _LineWidth) * w.x + GridLine(p.xz, _GridSize, _LineWidth) * w.y +
                                   GridLine(p.xy, _GridSize, _LineWidth) * w.z;
                float majorLines = GridLine(p.yz, major, _LineWidth * 1.6) * w.x + GridLine(p.xz, major, _LineWidth * 1.6) * w.y +
                                   GridLine(p.xy, major, _LineWidth * 1.6) * w.z;

                // Lit windows: some minor cells on walls glow, each on its own slow blink.
                float3 cell = floor(p / _GridSize);
                float wall = 1 - w.y;
                float h = Hash(cell);
                float blink = step(0.25, frac(_Time.y * 0.05 + h * 7.0));
                float window = step(1 - _WindowChance, h) * blink * wall;

                float3 view = normalize(GetWorldSpaceViewDir(p));
                float rim = pow(1 - saturate(abs(dot(n, view))), _RimPower) * _RimStrength;

                float scanY = fmod(_Time.y * _ScanSpeed, _ScanHeight);
                float scan = exp(-abs(fmod(p.y, _ScanHeight) - scanY) / max(_ScanWidth, 0.01)) * _ScanStrength;

                float glow = saturate(minorLines * _MinorStrength + majorLines) + rim + window * _WindowStrength + scan * (0.25 + majorLines);
                float3 colour = _FillColour.rgb + _LineColour.rgb * glow;
                colour = MixFog(colour, input.fog);
                return half4(colour, 1);
            }
            ENDHLSL
        }

        // Depth only, so the depth texture (and screen effects) see the city.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };

            V DepthVert(A input)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half DepthFrag(V input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
