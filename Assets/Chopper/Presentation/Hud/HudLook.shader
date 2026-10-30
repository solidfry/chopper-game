// Retro 80s HUD look (plan section 3b). Full-screen pass from HudLookRendererFeature: draws the HUD render texture
// over the camera colour with phosphor glow, scanlines, barrel distortion, colour split, vignette, noise, flicker and
// glitch bands, and a phosphor trail (pass 1 keeps a fading history of the HUD). The game view underneath stays clean,
// except for the world feed (cockpit view): static, roll, line jitter, a tracking band, colour split and scanlines on
// the world, masked by depth so the cockpit near the eye is not changed.
// All values are global properties written by HudLook (C#), so the material holds no state.
Shader "Hidden/Chopper/HudLook"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D(_ChopperHudTex);
        TEXTURE2D(_ChopperHudHistory);
        TEXTURE2D_X_FLOAT(_ChopperFeedDepth);
        float4 _ChopperHudSize;     // width, height, 1/width, 1/height
        float4 _ChopperHudParams0;  // glow, glow radius (px), scanlines, scanline count
        float4 _ChopperHudParams1;  // curvature, chromatic (px), vignette, noise
        float4 _ChopperHudParams2;  // brightness, glitch, glitch seed, unused
        float4 _ChopperHudParams3;  // trail decay per frame, trail strength, world feed 0..1, cockpit depth (m)
        ENDHLSL

        // Pass 0: composite the HUD (and its trail) over the camera colour.
        Pass
        {
            Name "HudLook"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // The HUD texture is premultiplied (UI Toolkit draws into a cleared, transparent target).
            float4 SampleHud(float2 uv)
            {
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                return SAMPLE_TEXTURE2D_LOD(_ChopperHudTex, sampler_LinearClamp, uv, 0) * inside;
            }

            float3 SampleScene(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
            }

            // The world seen through a weak camera feed. feed 0 = the scene as it is; 1 = only static.
            float3 WorldFeed(float2 uv, float feed)
            {
                float t = _Time.y;
                float2 pixel = 1.0 / _ScreenParams.xy;
                float2 feedUv = uv;

                // Vertical hold: a weak signal rolls the picture; a dark bar runs with the seam.
                float hold = smoothstep(0.45, 0.95, feed);
                float roll = hold * frac(t * 0.9);
                feedUv.y = frac(feedUv.y + roll);
                float seam = hold * smoothstep(0.03, 0.0, min(feedUv.y, 1.0 - feedUv.y));

                // Line jitter (new pattern 30 times a second) and a tracking band that runs down the screen.
                float frame = floor(t * 30.0);
                float row = floor(uv.y * 240.0);
                feedUv.x += (Hash(float2(row, frame)) - 0.5) * 0.012 * feed;
                float bandY = 1.0 - frac(t * 0.23);
                float band = smoothstep(0.05, 0.0, abs(uv.y - bandY)) * saturate(feed * 2.0);
                feedUv.x += band * (Hash(float2(row, frame + 3.0)) - 0.5) * 0.04;

                // Colour split, wider with the feed.
                float2 split = float2(pixel.x * (1.5 + 7.0 * feed), 0.0);
                float3 colour = float3(SampleScene(feedUv + split).r, SampleScene(feedUv).g, SampleScene(feedUv - split).b);

                // Washed-out, cool camera colour, and scanlines.
                float luma = dot(colour, float3(0.299, 0.587, 0.114));
                colour = lerp(colour, luma * float3(0.78, 1.0, 0.95), 0.55 * feed);
                float scan = 0.5 + 0.5 * cos(uv.y * (_ScreenParams.y / 3.0) * 6.2831853);
                colour *= lerp(1.0, scan, 0.45 * feed);

                // Static: grain everywhere, more in the band; snow takes over the picture when the feed is weak.
                float snow = Hash(uv * _ScreenParams.xy + frac(t) * 91.7);
                colour += (snow - 0.5) * (0.18 * feed + band * 0.5);
                colour = lerp(colour, snow.xxx * 0.6, smoothstep(0.55, 1.0, feed));
                colour *= 1.0 - seam;
                return max(0.0, colour);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float3 scene = SampleScene(uv);

                // World feed: only past the cockpit (depth), so the dashboard and frame stay clean.
                float feed = _ChopperHudParams3.z;
                if (feed > 0.001)
                {
                    float rawDepth = SAMPLE_TEXTURE2D_X_LOD(_ChopperFeedDepth, sampler_PointClamp, uv, 0).r;
                    float outside = saturate(LinearEyeDepth(rawDepth, _ZBufferParams) - _ChopperHudParams3.w);
                    scene = lerp(scene, WorldFeed(uv, feed), outside);
                }

                float glow = _ChopperHudParams0.x;
                float glowRadius = _ChopperHudParams0.y;
                float scanlines = _ChopperHudParams0.z;
                float scanlineCount = _ChopperHudParams0.w;
                float curvature = _ChopperHudParams1.x;
                float chromatic = _ChopperHudParams1.y;
                float vignette = _ChopperHudParams1.z;
                float noise = _ChopperHudParams1.w;
                float brightness = _ChopperHudParams2.x;
                float glitch = _ChopperHudParams2.y;
                float seed = _ChopperHudParams2.z;

                // Barrel distortion: the HUD bulges like a curved tube.
                float2 centred = uv * 2.0 - 1.0;
                float edge = dot(centred, centred);
                float2 hudUv = uv + centred * edge * curvature * 0.5;

                // Glitch: some horizontal bands tear sideways; the band pattern jumps with the seed.
                float band = floor(hudUv.y * 28.0);
                float torn = step(1.0 - glitch * 0.5, Hash(float2(band, seed)));
                hudUv.x += torn * (Hash(float2(band, seed + 17.0)) - 0.5) * glitch * 0.12;

                // Colour split: only towards the edges (the centre stays sharp to read), and with the glitch.
                float2 split = centred * _ChopperHudSize.zw * chromatic * edge;
                float4 middle = SampleHud(hudUv);
                float4 red = SampleHud(hudUv + split);
                float4 blue = SampleHud(hudUv - split);
                float4 hud = float4(red.r, middle.g, blue.b, max(middle.a, max(red.a, blue.a)));

                // Phosphor glow: a round blur (two rings of 8 taps, rotated against each other, outer ring weaker),
                // added on top. A square tap pattern made a boxy halo round each glyph.
                float2 texel = _ChopperHudSize.zw * glowRadius;
                float3 bloom = 0;
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float angle = i * 0.78539816;
                    float2 inner = float2(cos(angle), sin(angle));
                    float2 outer = float2(cos(angle + 0.39269908), sin(angle + 0.39269908));
                    bloom += SampleHud(hudUv + inner * texel).rgb * 0.65;
                    bloom += SampleHud(hudUv + outer * texel * 2.0).rgb * 0.35;
                }
                bloom *= glow / 8.0;

                // Scanlines, vignette, noise and flicker change the HUD only. Lines are at least 3 screen pixels apart:
                // closer lines beat against the pixel grid and make blocky bands through the text.
                float lineCount = min(scanlineCount, _ScreenParams.y / 3.0);
                float scan = 0.5 + 0.5 * cos(hudUv.y * lineCount * 6.2831853);
                float shade = lerp(1.0, scan, scanlines);
                shade *= saturate(1.0 - vignette * edge * 0.5);
                shade *= brightness;
                // Grain only on the glow and thin edges, not on the solid strokes, so text stays clean to read.
                float grain = (Hash(uv * _ChopperHudSize.xy + frac(_Time.y) * 91.7) - 0.5) * noise * (1.0 - hud.a * 0.8);

                // Phosphor trail: only what the HUD drew before and does not draw now (moving parts). Static parts
                // give nothing, so text stays sharp. History holds max(now, old x decay), so history - now = the ghost.
                float3 history = SAMPLE_TEXTURE2D_LOD(_ChopperHudHistory, sampler_LinearClamp, hudUv, 0).rgb;
                float3 trail = max(0.0, history - middle.rgb) * _ChopperHudParams3.y * (1.0 - saturate(hud.a));

                float3 hudColour = max(0.0, hud.rgb * shade + grain * hud.a);
                float3 colour = scene * (1.0 - saturate(hud.a)) + hudColour + (bloom + trail * shade) * brightness;
                return float4(colour, 1.0);
            }
            ENDHLSL
        }

        // Pass 1: history = max(HUD now, last history × decay). Source (_BlitTexture) = last history; both HUD-size.
        Pass
        {
            Name "HudPersistence"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPersistence

            float4 FragPersistence(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 last = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * _ChopperHudParams3.x;
                float4 now = SAMPLE_TEXTURE2D_LOD(_ChopperHudTex, sampler_LinearClamp, uv, 0);
                return max(now, last);
            }
            ENDHLSL
        }
    }
}
