// Retro 80s HUD look (plan section 3b). Full-screen pass from HudLookRendererFeature: draws the HUD render texture
// over the camera colour with phosphor glow, scanlines, barrel distortion, colour split, vignette, noise, flicker and
// glitch bands, and a phosphor trail (pass 1 keeps a fading history of the HUD). Only the HUD is changed; the game
// view underneath stays clean.
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
        float4 _ChopperHudSize;     // width, height, 1/width, 1/height
        float4 _ChopperHudParams0;  // glow, glow radius (px), scanlines, scanline count
        float4 _ChopperHudParams1;  // curvature, chromatic (px), vignette, noise
        float4 _ChopperHudParams2;  // brightness, glitch, glitch seed, unused
        float4 _ChopperHudParams3;  // trail decay per frame, trail strength, unused, unused
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

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float3 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

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

                // Colour split: grows towards the edges, and with the glitch.
                float2 split = centred * _ChopperHudSize.zw * chromatic * (0.35 + edge);
                float4 middle = SampleHud(hudUv);
                float4 red = SampleHud(hudUv + split);
                float4 blue = SampleHud(hudUv - split);
                float4 hud = float4(red.r, middle.g, blue.b, max(middle.a, max(red.a, blue.a)));

                // Phosphor glow: a cheap 8-tap blur of the HUD at two radii, added on top.
                float2 texel = _ChopperHudSize.zw * glowRadius;
                float3 bloom = 0;
                bloom += SampleHud(hudUv + float2( 1,  0) * texel).rgb;
                bloom += SampleHud(hudUv + float2(-1,  0) * texel).rgb;
                bloom += SampleHud(hudUv + float2( 0,  1) * texel).rgb;
                bloom += SampleHud(hudUv + float2( 0, -1) * texel).rgb;
                bloom += SampleHud(hudUv + float2( 1,  1) * texel * 1.6).rgb;
                bloom += SampleHud(hudUv + float2(-1,  1) * texel * 1.6).rgb;
                bloom += SampleHud(hudUv + float2( 1, -1) * texel * 1.6).rgb;
                bloom += SampleHud(hudUv + float2(-1, -1) * texel * 1.6).rgb;
                bloom *= glow / 8.0;

                // Scanlines, vignette, noise and flicker change the HUD only.
                float scan = 0.5 + 0.5 * cos(hudUv.y * scanlineCount * 6.2831853);
                float shade = lerp(1.0, scan, scanlines);
                shade *= saturate(1.0 - vignette * edge * 0.5);
                shade *= brightness;
                float grain = (Hash(uv * _ChopperHudSize.xy + frac(_Time.y) * 91.7) - 0.5) * noise;

                // Phosphor trail: the fading history, only where the HUD is not drawn now (premultiplied, additive).
                float3 trail = SAMPLE_TEXTURE2D_LOD(_ChopperHudHistory, sampler_LinearClamp, hudUv, 0).rgb
                    * _ChopperHudParams3.y * (1.0 - saturate(hud.a));

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
