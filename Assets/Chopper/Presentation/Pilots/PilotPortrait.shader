// Pilot portrait (HUD and pilot select screen). PilotPortraitRig draws it into a small render texture each frame with
// one full-screen triangle. The "rig" is a warp: each output pixel reads the art from where the moved face part came
// from, so the art needs no layers. The face points (PilotFace) and the pose come from C#; all points are in UV (y up).
// - Head: turns on the neck (tilt), moves (offset) and fakes a turn (the middle of the face moves more than its sides).
// - Jaw: the part under the lips moves down with the voice; a dark mouth opens between the lips. Masked pilots light
//   the mask in the accent colour instead.
// - Eyes: the upper lids come down (blink). Chest: rises and widens a little (breath).
// Light: the normal map takes the scene's sun and ambient (given in the portrait's own space by C#) and the cockpit
// screen glow (accent, from below). Look: a part accent duotone, scanlines, a rolling scan band, noise, line glitches.
Shader "Chopper/PilotPortrait"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Portrait", 2D) = "black" {}
        [NoScaleOffset][Normal] _NormalTex ("Normal map", 2D) = "bump" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "Portrait"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalTex);
            SAMPLER(sampler_NormalTex);

            float _Aspect;          // art width / height
            float _FlipY;           // 1 = the target's rows run the other way
            float4 _View;           // the part of the art in the picture: uv min xy, size zw (head and shoulders)
            float4 _HeadRig;        // head centre xy, head half size xy
            float4 _NeckMouth;      // neck xy, mouth xy
            float4 _Eyes;           // left eye xy, right eye xy (negative = no eye)
            float4 _FaceSizes;      // eye half width, mouth half width, chin y, masked (0/1)
            float4 _Pose0;          // head offset xy, tilt (radians), turn (-1..1)
            float4 _Pose1;          // breath 0..1, jaw 0..1, blink 0..1, glitch 0..1
            float4 _LightDir;       // to the sun, portrait space (x right, y up, z out of the picture)
            float4 _LightColor;
            float4 _AmbientUp;
            float4 _AmbientMid;
            float4 _AmbientDown;
            float4 _FillDir;
            float4 _FillColor;
            float4 _Accent;
            float4 _Style;          // tint, scanline depth, scanline count, light amount
            float4 _Fx;             // time, hit flash, rim, opacity
            float4 _LightShape;     // sun boost, contrast

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(id);
                output.uv = GetFullScreenTriangleTexCoord(id);
                return output;
            }

            float Lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
            float Hash(float n) { return frac(sin(n) * 43758.5453); }
            float Hash2(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            // Aspect-correct space: x is scaled so that a circle stays round.
            float2 ToS(float2 uv) { return float2(uv.x * _Aspect, uv.y); }
            float2 FromS(float2 s) { return float2(s.x / _Aspect, s.y); }
            float2 Rotate(float2 v, float a) { float c = cos(a), s = sin(a); return float2(v.x * c - v.y * s, v.x * s + v.y * c); }

            float Blink(float2 q, float2 eyeUV, float size, float blink)
            {
                if (eyeUV.x < 0.0) return 0.0;
                float2 e = ToS(eyeUV);
                float w = 1.0 - smoothstep(0.45, 1.0, length((q - e) / float2(size * 1.3, size * 0.75)));
                return blink * size * 0.65 * w;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                if (_FlipY > 0.5) uv.y = 1.0 - uv.y;
                float time = _Fx.x;
                float glitch = _Pose1.w;

                // Line glitch: some bands of rows jump sideways.
                float2 p = uv;
                float band = Hash(floor(p.y * 46.0) + floor(time * 22.0) * 7.0);
                p.x += (band - 0.5) * 0.08 * glitch * step(0.55, band);
                p = _View.xy + p * _View.zw;

                float2 s = ToS(p);
                float2 head = ToS(_HeadRig.xy);
                float2 neck = ToS(_NeckMouth.xy);
                float headWidth = _HeadRig.z * _Aspect;

                // Breath: below the neck the chest rises and widens.
                float body = saturate((neck.y - s.y) / 0.25);
                float breath = _Pose1.x;
                s.y -= breath * 0.007 * body;
                s.x = neck.x + (s.x - neck.x) / (1.0 + breath * 0.008 * body);

                // Head weight: an ellipse round the head that reaches down to the neck point.
                float2 radius = float2(headWidth, s.y < head.y ? max(_HeadRig.w, head.y - neck.y) : _HeadRig.w);
                float weight = 1.0 - smoothstep(0.55, 1.3, length((s - head) / radius));

                // Head pose, inverse: where did this pixel come from?
                float tilt = _Pose0.z * weight;
                float turn = _Pose0.w;
                float2 q = s - _Pose0.xy * weight;
                q = neck + Rotate(q - neck, -tilt);
                float across = (q.x - head.x) / headWidth;
                q.x -= turn * headWidth * 0.13 * saturate(1.0 - across * across) * weight;

                // Jaw: under the lips, the face moves down; a dark gap opens between the lips.
                float2 mouth = ToS(_NeckMouth.zw);
                float mouthWidth = _FaceSizes.y * _Aspect;
                float masked = _FaceSizes.w;
                float jawHeight = max(mouth.y - _FaceSizes.z, 0.01);
                float open = _Pose1.y * jawHeight * 0.3 * (1.0 - masked);
                float2 jawCentre = float2(mouth.x, mouth.y - jawHeight * 0.55);
                float jaw = 1.0 - smoothstep(0.7, 1.1, length((q - jawCentre) / float2(max(mouthWidth, 0.01) * 1.9, jawHeight * 0.9)));
                jaw *= smoothstep(mouth.y + jawHeight * 0.03, mouth.y - jawHeight * 0.12, q.y);
                float2 holeCentre = float2(mouth.x, mouth.y - open * 0.5);
                float hole = (1.0 - smoothstep(0.55, 1.0, length((q - holeCentre) / float2(max(mouthWidth, 0.001) * 0.78, open * 0.55 + 1e-4))))
                    * saturate(_Pose1.y * 4.0) * (1.0 - masked);
                q.y += open * jaw;

                // Blink: the upper lids come down over the eyes.
                float eyeSize = _FaceSizes.x * _Aspect;
                q.y += Blink(q, _Eyes.xy, eyeSize, _Pose1.z) + Blink(q, _Eyes.zw, eyeSize, _Pose1.z);

                float2 src = FromS(q);
                half4 art = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, src);
                art.a *= step(0.0, src.x) * step(src.x, 1.0) * step(0.0, src.y) * step(src.y, 1.0);
                float3 n = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, src));
                n.xy = Rotate(n.xy, tilt);
                n.x += turn * weight * 0.3;
                n = normalize(n);

                // Light: wrapped sun (soft, for a painted look), three-band ambient, screen glow, accent rim.
                float3 toSun = normalize(_LightDir.xyz);
                float sun = saturate((dot(n, toSun) + 0.1) / 1.1);
                float3 ambient = n.y >= 0.0 ? lerp(_AmbientMid.rgb, _AmbientUp.rgb, n.y) : lerp(_AmbientMid.rgb, _AmbientDown.rgb, -n.y);
                float3 fill = _FillColor.rgb * saturate(dot(n, normalize(_FillDir.xyz)));
                // The art is already shaded: the light keeps its direction and colour, but its level is scaled to
                // about 1 (a dark scene is only a little darker), so the face stays readable.
                float3 sunLight = _LightColor.rgb * _LightShape.x;
                float level = max(Lum(_AmbientMid.rgb + sunLight * 0.5 + fill * 0.5), 0.02);
                float3 light = (ambient + sunLight * sun + fill) / level * (0.65 + 0.35 * saturate(level));
                light = min(pow(max(light, 0.0), _LightShape.y), 2.5);
                float3 colour = art.rgb * lerp(1.0, light, _Style.w);
                float rim = pow(1.0 - saturate(n.z), 3.0) * _Fx.z;
                colour += _Accent.rgb * rim * art.a;

                // Masked pilots: the voice lights the mask.
                float2 maskRadius = float2(headWidth * 0.45, jawHeight * 0.9);
                float maskGlow = masked * _Pose1.y * (1.0 - smoothstep(0.2, 1.0, length((q - mouth) / maskRadius)));
                colour += _Accent.rgb * maskGlow * (0.25 + smoothstep(0.0, 0.2, Lum(art.rgb))) * 3.0;
                colour *= 1.0 - hole * 0.92;

                // Look: part duotone in the accent, scanlines, a rolling scan band, noise, hit flash.
                float lum = Lum(colour);
                float3 duo = _Accent.rgb * lum * 1.9 + lum * lum * lum * 0.35;
                colour = lerp(colour, duo, _Style.x);
                float scan = 0.5 + 0.5 * sin(uv.y * _Style.z * 6.2831853);
                colour *= 1.0 - _Style.y * scan;
                float roll = exp(-pow((frac(uv.y - time * 0.11) - 0.5) * 16.0, 2.0));
                colour *= 1.0 + roll * 0.35;
                colour += (Hash2(floor(uv * 300.0) + frac(time * 13.0) * 91.0) - 0.5) * (0.018 + glitch * 0.25);
                colour += _Fx.y * float3(1.0, 0.45, 0.35);

                // Fade out at the bottom edge (the chest), so the picture sits in the HUD with no hard cut.
                float alpha = art.a * smoothstep(0.0, 0.16, uv.y) * _Fx.w;
                return half4(max(colour, 0.0), alpha);
            }
            ENDHLSL
        }
    }
}
