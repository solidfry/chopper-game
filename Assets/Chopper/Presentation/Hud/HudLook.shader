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
        float4 _ChopperHudParams4;  // feed boot 0..1, biggest pixel size, colour steps, dropout
        float4 _ChopperHudParams5;  // honeycomb 0..1 now, strength, hexagon size, reach from the corners
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

            // Honeycomb in the screen corners: a repeating grid of hexagon outlines. Each whole cell has its own strength
            // (from its distance to the nearest corner), so the edge of the frame breaks up cell by cell, and cells go out
            // one by one as "now" falls to 0. Some cells are lit, and blink like tech that is starting up.
            float3 HexGrid(float2 uv, float now, float size, float reach)
            {
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 p = uv * float2(aspect, 1.0) / size + 8.0;

                // Two offset rectangular grids make the hexagon tiling: the nearer centre is this pixel's cell.
                const float2 tile = float2(1.0, 1.7320508);
                float2 a = fmod(p, tile) - tile * 0.5;
                float2 b = fmod(p - tile * 0.5, tile) - tile * 0.5;
                float2 local = dot(a, a) < dot(b, b) ? a : b;
                float2 id = p - local;

                float2 centreUv = (id - 8.0) * size / float2(aspect, 1.0);
                float2 toCorner = min(centreUv, 1.0 - centreUv) * float2(aspect, 1.0);
                float corner = saturate(1.0 - length(toCorner) / max(reach, 0.001));
                float cellSeed = Hash(id * 0.0731);
                if (corner <= cellSeed * 0.45 || cellSeed > now * 1.3) return 0.0;

                float2 edge = abs(local);
                float side = max(dot(edge, float2(0.5, 0.8660254)), edge.x);
                float lineWidth = 1.6 / (size * _ScreenParams.y);
                float outline = smoothstep(lineWidth, 0.0, 0.5 - side);

                float blink = floor(_Time.y * 6.0 + cellSeed * 37.0);
                float lit = step(0.84, Hash(id * 0.0417 + blink * 0.13)) * 0.3;
                return (outline * 0.8 + lit) * corner * float3(0.25, 1.0, 0.68);
            }

            // The world seen through a weak camera feed. feed 0 = the scene as it is; 1 = only static.
            // The boot-up part of it (Params4.x) is digital: big pixels that resolve in patches, few colours and dead blocks.
            // What is left of the feed (hits) is the analog noise.
            float3 WorldFeed(float2 uv, float rawFeed)
            {
                float t = _Time.y;
                float2 pixel = 1.0 / _ScreenParams.xy;
                float2 feedUv = uv;

                float boot = _ChopperHudParams4.x;
                float feed = saturate(rawFeed - boot);
                float digital = 0.0;
                float blockSize = 1.0;
                float2 block = 0.0;
                float2 blockLocal = 0.5;
                float digitalFrame = floor(t * 15.0);
                if (boot > 0.0)
                {
                    float biggest = max(_ChopperHudParams4.y, 2.0);
                    float2 screen = uv * _ScreenParams.xy;
                    float2 tile = floor(screen / biggest);
                    // Each patch resolves at its own time; pixel size halves in steps, so it reads as a digital signal.
                    digital = saturate(boot * lerp(0.55, 1.6, Hash(tile * 0.37 + 3.1)));
                    blockSize = pow(2.0, ceil(digital * log2(biggest)));
                    // Rows of blocks slide sideways while the signal is weak.
                    screen.x += (Hash(float2(tile.y * 0.41, digitalFrame * 0.07)) - 0.5) * biggest * boot * 2.0;
                    block = floor(screen / blockSize);
                    blockLocal = frac(screen / blockSize);
                    feedUv = (block + 0.5) * blockSize * pixel;
                }

                // Vertical hold: a weak signal rolls the picture; a dark bar runs with the seam.
                float hold = smoothstep(0.45, 0.95, feed);
                float roll = hold * frac(t * 0.9);
                if (roll > 0.0) feedUv.y = frac(feedUv.y + roll);
                float seam = hold * smoothstep(0.03, 0.0, min(feedUv.y, 1.0 - feedUv.y));

                // Line jitter (new pattern 30 times a second) and a tracking band that runs down the screen.
                float frame = floor(t * 30.0);
                float row = floor(uv.y * 240.0);
                feedUv.x += (Hash(float2(row, frame)) - 0.5) * 0.012 * feed;
                float bandY = 1.0 - frac(t * 0.23);
                float band = smoothstep(0.05, 0.0, abs(uv.y - bandY)) * saturate(feed * 2.0);
                feedUv.x += band * (Hash(float2(row, frame + 3.0)) - 0.5) * 0.04;

                // Colour split, wider with the feed.
                float2 split = float2(pixel.x * (1.5 * saturate(feed * 8.0) + 7.0 * feed + blockSize * 0.5 * digital), 0.0);
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

                if (boot > 0.0)
                {
                    // Few colours, in the HUD's green, while the patch is rough.
                    float steps = max(_ChopperHudParams4.z, 2.0);
                    float3 banded = floor(max(0.0, colour) * steps + 0.5) / steps * float3(0.7, 1.0, 0.85);
                    colour = lerp(colour, banded, saturate(digital * 1.5));

                    // Dead blocks: dark data, some lit, new every 1/15 s. All of them at the start of the boot-up.
                    float2 seed = block * 0.31 + blockSize * 0.7;
                    if (Hash(seed + digitalFrame * 0.11) < digital * digital * _ChopperHudParams4.w)
                    {
                        float data = Hash(seed + digitalFrame * 0.17 + 0.7);
                        colour = float3(0.01, 0.05, 0.035) + step(0.8, data) * data * float3(0.0, 0.65, 0.45);
                    }

                    // A dark line round the bigger blocks, so they read as pixels.
                    float outline = step(0.92, max(blockLocal.x, blockLocal.y)) * step(8.0, blockSize);
                    colour *= 1.0 - outline * 0.4 * digital;
                }
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

                // Honeycomb: over the world and the cockpit, under the HUD.
                if (_ChopperHudParams5.x > 0.001 && _ChopperHudParams5.y > 0.0)
                    scene += HexGrid(uv, _ChopperHudParams5.x, _ChopperHudParams5.z, _ChopperHudParams5.w) * _ChopperHudParams5.y;

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
