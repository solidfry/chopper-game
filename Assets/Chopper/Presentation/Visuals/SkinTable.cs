using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chopper.Presentation.Visuals
{
    /// <summary>Pattern families of the runtime skin shader (same order as PartForge runtime.PATTERNS).</summary>
    public enum SkinPattern
    {
        Solid,
        Camo,
        Digital,
        Splinter,
        Tiger,
        Hex,
        Synthwave,
        Livery,
    }

    /// <summary>
    /// How a skin looks in the runtime skin shader (<c>Art/Shaders/ChopperSkin.hlsl</c>). Colours are picked as normal
    /// (gamma) colours; the table stores them linear. Wear and grime already include the kit style's own wear.
    /// </summary>
    [Serializable]
    public sealed class SkinLook
    {
        [HideInInspector, Tooltip("Filled in (from the kit or by hand); an empty look is filled from the kit on import.")]
        public bool Filled;
        public SkinPattern Pattern;
        [Tooltip("Up to 4. Colour 1 is the base; camo and digital lay the others over it.")]
        public Color[] Colours = { new(0.35f, 0.36f, 0.33f) };
        [Tooltip("Pattern size (m) for camo, digital, splinter.")] public float Scale = 1f;
        public int Seed = 1;
        [Tooltip("Camo and digital: how much the blobs are pushed around.")] public float Warp;
        [Tooltip("Camo and digital: share of the surface for colours 2-4.")] public float[] Cover = Array.Empty<float>();
        [Tooltip("Splinter: weight of each colour.")] public float[] Weights = Array.Empty<float>();
        [Tooltip("Digital pixel, tiger stripe period, hex cell, synthwave grid (m).")] public float Size;
        [Tooltip("Tiger: stripe width (share of the period).")] public float StripeWidth = 0.3f;
        [Tooltip("Splinter: shards stretched along the hull.")] public float Stretch = 2f;
        [Tooltip("Hex: seam colour.")] public Color LineColour = Color.black;
        [Tooltip("Synthwave: sunset band colours, top to bottom (3).")] public Color[] Sun = Array.Empty<Color>();
        [Tooltip("Synthwave: band between these height shares.")] public Vector2 Band = new(0.15f, 0.5f);
        [Tooltip("Livery: top stripes between these |x| (m).")] public Vector2 Stripe = new(0.05f, 0.13f);
        [Tooltip("Livery: side band centre and half height (height share).")] public Vector2 LiveryBand = new(0.42f, 0.06f);
        [Tooltip("Keep the kit's own paint finish (metallic, smoothness).")] public bool KeepFinish = true;
        [Range(0, 1)] public float Metallic;
        [Range(0, 1)] public float Smoothness = 0.3f;
        [Tooltip("Bare metal on the edges (0 = none).")] public float Wear = 1f;
        [Tooltip("Dirt and rain streaks (0 = clean).")] public float Grime = 1f;
        [Tooltip("Replacement panels take the skin too.")] public bool Panels;
        [Tooltip("Warning paint takes the hazard colour.")] public bool RecolourHazard;
        public Color Hazard = new(1f, 0.87f, 0.42f);
        [Tooltip("Glow lines (hex seams, synthwave grid): 1-2 colours, none = no glow.")]
        public Color[] Glow = Array.Empty<Color>();
        [Tooltip("Hex: share of cells with glowing outlines.")] public float GlowShare = 0.15f;
        [Tooltip("Glow line width (m).")] public float GlowWidth = 0.012f;
        [Tooltip("Colour multiplier on dark armour panels.")] public Vector3 DarkTone = new(0.55f, 0.55f, 0.55f);
        [Tooltip("Colour multiplier on light armour panels.")] public Vector3 LightTone = new(1.3f, 1.3f, 1.3f);
    }

    /// <summary>
    /// A skin's runtime values as the PartForge kit manifest writes them (skins[].runtime; field names match the JSON).
    /// Colours are linear RGB, three floats each. Used by the kit import step and the encoder test.
    /// </summary>
    [Serializable]
    public sealed class SkinParams
    {
        public string pattern;
        public float[] colours;
        public float scale;
        public int seed;
        public float warp;
        public float[] cover;
        public float[] weights;
        public float size;
        public float width;
        public float stretch;
        public float[] line;
        public float[] sun;
        public float[] band;
        public float[] stripe;
        public float[] livery_band;
        public float metallic;
        public float smoothness;
        public float wear;
        public float grime;
        public bool panels;
        public float[] hazard;
        public float[] glow;
        public float glow_share;
        public float glow_width;
        public float[] dark;
        public float[] light;

        public SkinLook ToLook()
        {
            var look = new SkinLook
            {
                Filled = true,
                Pattern = Enum.TryParse<SkinPattern>(pattern, true, out var kind) ? kind : SkinPattern.Solid,
                Colours = Colours(colours),
                Scale = scale,
                Seed = seed,
                Warp = warp,
                Cover = cover ?? Array.Empty<float>(),
                Weights = weights ?? Array.Empty<float>(),
                Size = size,
                StripeWidth = width,
                Stretch = stretch,
                LineColour = Colours(line).Length > 0 ? Colours(line)[0] : Color.black,
                Sun = Colours(sun),
                Band = Pair(band, new Vector2(0.15f, 0.5f)),
                Stripe = Pair(stripe, new Vector2(0.05f, 0.13f)),
                LiveryBand = Pair(livery_band, new Vector2(0.42f, 0.06f)),
                KeepFinish = metallic < 0f,
                Metallic = Mathf.Max(metallic, 0f),
                Smoothness = Mathf.Max(smoothness, 0f),
                Wear = wear,
                Grime = grime,
                Panels = panels,
                RecolourHazard = hazard is { Length: >= 3 },
                Hazard = Colours(hazard).Length > 0 ? Colours(hazard)[0] : new Color(1f, 0.87f, 0.42f),
                Glow = Colours(glow),
                GlowShare = glow_share,
                GlowWidth = glow_width,
                DarkTone = Vector(dark, Vector3.one),
                LightTone = Vector(light, Vector3.one),
            };
            return look;
        }

        /// <summary>Linear RGB triples -> picker (gamma) colours.</summary>
        private static Color[] Colours(float[] values)
        {
            if (values == null) return Array.Empty<Color>();
            var result = new Color[values.Length / 3];
            for (var i = 0; i < result.Length; i++)
                result[i] = new Color(Mathf.LinearToGammaSpace(values[i * 3]), Mathf.LinearToGammaSpace(values[i * 3 + 1]),
                    Mathf.LinearToGammaSpace(values[i * 3 + 2]));
            return result;
        }

        private static Vector2 Pair(float[] values, Vector2 fallback) =>
            values is { Length: >= 2 } ? new Vector2(values[0], values[1]) : fallback;

        private static Vector3 Vector(float[] values, Vector3 fallback) =>
            values is { Length: >= 3 } ? new Vector3(values[0], values[1], values[2]) : fallback;
    }

    /// <summary>
    /// The skin table: one row of <see cref="Width"/> float4 texels per skin, read by the skin shader at the chopper's
    /// skin index. Same layout as PartForge <c>runtime.encode</c> (the edit-mode test checks both agree on the kit
    /// manifest). Pure: no Unity objects.
    /// </summary>
    public static class SkinTable
    {
        public const int Width = 20;

        /// <summary>fbm (4 octaves) quantiles at 0, 0.05 ... 1 (PartForge runtime.COVER_TABLE).</summary>
        private static readonly float[] CoverTable =
        {
            0.0681f, 0.3138f, 0.352f, 0.379f, 0.4009f, 0.4202f, 0.438f, 0.4543f, 0.4702f, 0.4854f, 0.5005f, 0.5154f,
            0.5308f, 0.5467f, 0.5631f, 0.5806f, 0.5999f, 0.6217f, 0.6483f, 0.6862f, 0.9283f,
        };

        /// <summary>The noise threshold that leaves <paramref name="cover"/> of the surface above it.</summary>
        public static float CoverThreshold(float cover)
        {
            var x = (1f - Mathf.Clamp01(cover)) * 20f;
            var i = Mathf.Min((int)x, 19);
            return CoverTable[i] + (CoverTable[i + 1] - CoverTable[i]) * (x - i);
        }

        public static Vector4[] Encode(SkinLook look)
        {
            var row = new Vector4[Width];
            var colours = Linear(look.Colours);
            if (colours.Count == 0) colours.Add(Vector3.one * 0.3f);
            var count = colours.Count;
            for (var k = 0; k < 4; k++)
            {
                var c = colours[Mathf.Min(k, count - 1)];
                row[k] = new Vector4(c.x, c.y, c.z, 0f);
            }
            row[0].w = (int)look.Pattern;
            row[1].w = look.Scale;
            row[2].w = look.Seed;
            row[3].w = look.Warp;

            var thresholds = new[] { 2f, 2f, 2f };
            if (look.Pattern == SkinPattern.Splinter)
            {
                var weights = look.Weights is { Length: > 0 } ? look.Weights : Fill(count, 1f);
                var total = 0f;
                foreach (var w in weights) total += w;
                var sum = 0f;
                for (var k = 0; k < weights.Length - 1 && k < 3; k++)
                {
                    sum += weights[k];
                    thresholds[k] = sum / total;
                }
            }
            else if (look.Pattern == SkinPattern.Tiger)
            {
                thresholds = new[] { look.StripeWidth, 0f, 0f };
            }
            else
            {
                for (var k = 0; k < (look.Cover?.Length ?? 0) && k < 3; k++) thresholds[k] = CoverThreshold(look.Cover[k]);
            }
            row[4] = new Vector4(thresholds[0], thresholds[1], thresholds[2], look.Size);
            row[5] = new Vector4(look.KeepFinish ? -1f : look.Metallic, look.KeepFinish ? -1f : look.Smoothness, look.Wear, look.Grime);

            var glow = Linear(look.Glow);
            row[6] = new Vector4(look.Panels ? 1f : 0f, look.RecolourHazard ? 1f : 0f, glow.Count > 0 ? 1f : 0f, count);
            var hazard = look.RecolourHazard ? (Vector3)(Vector4)look.Hazard.linear : Vector3.zero;
            row[7] = new Vector4(hazard.x, hazard.y, hazard.z, look.Stretch);
            var glowA = glow.Count > 0 ? glow[0] : Vector3.zero;
            var glowB = glow.Count > 1 ? glow[1] : glowA;
            row[8] = new Vector4(glowA.x, glowA.y, glowA.z, look.GlowShare);
            row[9] = new Vector4(glowB.x, glowB.y, glowB.z, look.GlowWidth);
            row[10] = new Vector4(look.Band.x, look.Band.y, look.Stripe.x, look.Stripe.y);
            row[11] = new Vector4(look.LiveryBand.x, look.LiveryBand.y, glow.Count, 0f);
            row[12] = look.DarkTone;
            row[13] = look.LightTone;
            if (look.Pattern == SkinPattern.Hex)
            {
                var line = (Vector4)look.LineColour.linear;
                row[14] = new Vector4(line.x, line.y, line.z, 0f);
            }
            var sun = Linear(look.Sun);
            for (var k = 0; k < sun.Count && k < 3; k++) row[14 + k] = new Vector4(sun[k].x, sun[k].y, sun[k].z, 0f);
            return row;
        }

        /// <summary>Texture pixels (row y = skin index, from the bottom: Unity texture order).</summary>
        public static Color[] Pixels(IReadOnlyList<SkinLook> looks)
        {
            var pixels = new Color[Width * Mathf.Max(looks.Count, 1)];
            for (var y = 0; y < looks.Count; y++)
            {
                var row = Encode(looks[y]);
                for (var x = 0; x < Width; x++) pixels[y * Width + x] = row[x];
            }
            return pixels;
        }

        private static List<Vector3> Linear(Color[] colours)
        {
            var result = new List<Vector3>();
            if (colours == null) return result;
            foreach (var colour in colours)
            {
                var linear = colour.linear;
                result.Add(new Vector3(linear.r, linear.g, linear.b));
            }
            return result;
        }

        private static float[] Fill(int count, float value)
        {
            var values = new float[count];
            for (var i = 0; i < count; i++) values[i] = value;
            return values;
        }
    }
}
