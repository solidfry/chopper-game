// BuildingWear: the CityForge building material. Palette colours (one 5 x 5 swatch texture per kit, UV0) graded with
// wear and grime.
//
// The building FBX carries per-vertex masks in its vertex colour (linear; Tools/CityForge/cityforge/wear.py):
// R occlusion, G splash (near the surface below), B drip (something stands above: sills, ledges), A sky exposure.
// The shader adds what low-poly vertices cannot hold: rain streaks down vertical faces from drip spots, fine noise,
// dust on exposed ledges and roofs in the biome's dust colour (the biome table: cover colour, else ground colour),
// rain wetness from the biome. Glass and lit surfaces (smooth or glowing swatches) stay clean.
//
// Written without the editor (4 Nov 2026): compile and check in Unity. BuildingWear.shadergraph wraps it
// (Tools/CityForge/make_city_graphs.py writes the graph). Same rules as the Blender preview (wear.add_preview_nodes).

#ifndef BUILDING_WEAR_INCLUDED
#define BUILDING_WEAR_INCLUDED

SamplerState building_wear_point_clamp_sampler;

float BW_Hash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float BW_Noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(BW_Hash(i), BW_Hash(i + float2(1.0, 0.0)), u.x),
                lerp(BW_Hash(i + float2(0.0, 1.0)), BW_Hash(i + float2(1.0, 1.0)), u.x), u.y);
}

float BW_Fbm(float2 p, int octaves)
{
    float total = 0.0, amplitude = 0.5, norm = 0.0;
    for (int k = 0; k < octaves; k++)
    {
        total += amplitude * BW_Noise(p);
        norm += amplitude;
        p = p * 2.03 + 17.1;
        amplitude *= 0.5;
    }
    return total / norm;
}

float3 BW_Linear(float3 c)
{
    return lerp(pow((c + 0.055) / 1.055, 2.4), c / 12.92, step(c, 0.04045));
}


// Detail maps (CC0, Assets/Chopper/Art/ThirdParty/CC0): sampled with their own sampler (bilinear, repeat).
float3 BW_Tri(UnityTexture2D t, float3 p, float3 blend, float scale)
{
    return SAMPLE_TEXTURE2D(t.tex, t.samplerstate, p.zy * scale).rgb * blend.x
         + SAMPLE_TEXTURE2D(t.tex, t.samplerstate, p.xz * scale).rgb * blend.y
         + SAMPLE_TEXTURE2D(t.tex, t.samplerstate, p.xy * scale).rgb * blend.z;
}

// Three-axis normal map, "whiteout" blend (after Ben Golus, "Normal Mapping for a Triplanar Shader"); world space.
float3 BW_TriNormal(UnityTexture2D t, float3 p, float3 n, float3 blend, float scale)
{
    float3 tx = UnpackNormal(SAMPLE_TEXTURE2D(t.tex, t.samplerstate, p.zy * scale));
    float3 ty = UnpackNormal(SAMPLE_TEXTURE2D(t.tex, t.samplerstate, p.xz * scale));
    float3 tz = UnpackNormal(SAMPLE_TEXTURE2D(t.tex, t.samplerstate, p.xy * scale));
    tx = float3(tx.xy + n.zy, abs(tx.z) * n.x);
    ty = float3(ty.xy + n.xz, abs(ty.z) * n.y);
    tz = float3(tz.xy + n.xy, abs(tz.z) * n.z);
    return normalize(tx.zyx * blend.x + ty.xzy * blend.y + tz.xyz * blend.z);
}

float3 BW_Blend(float3 n)
{
    float3 blend = pow(abs(n), 4.0);
    return blend / max(1e-5, dot(blend, 1.0));
}

// Luminance of a detail colour as a factor round 1 (0.55..1.45): the detail tints nothing, it only shades.
float BW_Detail(float3 c)
{
    return clamp(dot(c, float3(0.2126, 0.7152, 0.0722)) * 2.0 + 0.2, 0.55, 1.45);
}

float4 BW_BiomeTexel(UnityTexture2D table, float row, float rows, int texel)
{
    float2 uv = float2((texel + 0.5) * table.texelSize.x, 1.0 - (row + 0.5) / rows);
    return SAMPLE_TEXTURE2D_LOD(table.tex, building_wear_point_clamp_sampler, uv, 0);
}

void BuildingWear_float(float2 UV, float3 PositionWS, float3 NormalWS, float4 Masks,
    UnityTexture2D PaletteColour, UnityTexture2D PaletteEmission, UnityTexture2D PaletteMetalSmooth,
    UnityTexture2D BiomeTable, float BiomeIndex, float Wear, float EmissionStrength,
    UnityTexture2D ConcreteColour, UnityTexture2D ConcreteNormal,
    out float3 Albedo, out float Metallic, out float Smoothness, out float3 NormalOut, out float3 Emission,
    out float AmbientOcclusion)
{
    float3 base = SAMPLE_TEXTURE2D(PaletteColour.tex, building_wear_point_clamp_sampler, UV).rgb;
    float3 glow = SAMPLE_TEXTURE2D(PaletteEmission.tex, building_wear_point_clamp_sampler, UV).rgb;
    float4 metalSmooth = SAMPLE_TEXTURE2D(PaletteMetalSmooth.tex, building_wear_point_clamp_sampler, UV);

    float rows = max(1.0, round(1.0 / BiomeTable.texelSize.y));
    float row = clamp(round(BiomeIndex), 0.0, rows - 1.0);
    float4 cover = BW_BiomeTexel(BiomeTable, row, rows, 9);
    float4 p13 = BW_BiomeTexel(BiomeTable, row, rows, 13);
    float3 dust = BW_Linear(p13.w > 0.5 ? cover.rgb : BW_BiomeTexel(BiomeTable, row, rows, 0).rgb);
    float rain = p13.x;
    float3 grime = BW_Linear(float3(0.231, 0.204, 0.169));   // #3b342b

    float occlusion = Masks.r, splash = Masks.g, drip = Masks.b, sky = Masks.a;
    float3 n = normalize(NormalWS);
    float vertical = 1.0 - abs(n.y);
    float upward = saturate((n.y - 0.6) * 2.5);
    float dirty = saturate((1.0 - metalSmooth.a - 0.25) * 1.6) * (1.0 - saturate(dot(glow, 1.0) * 4.0)) * Wear;

    float streakNoise = BW_Fbm(float2((PositionWS.x + PositionWS.z) * 1.2, PositionWS.y * 0.04), 2);
    float streaks = vertical * (drip * 0.8 + 0.2) * saturate((streakNoise - 0.45) * 3.0) * sky;
    float fine = BW_Fbm(PositionWS.xz * 0.6 + PositionWS.y * 0.3, 3);

    // CC0 concrete detail on three axes (3 m), rough surfaces only.
    float3 blend = BW_Blend(n);
    float rough = saturate((1.0 - metalSmooth.a - 0.25) * 1.6);
    float detailF = lerp(1.0, BW_Detail(BW_Tri(ConcreteColour, PositionWS, blend, 1.0 / 3.0)), 0.6 * rough);

    float3 colour = base * detailF;
    colour *= lerp(1.0, 0.6, saturate(occlusion - 0.25) * 0.6 * dirty);
    colour = lerp(colour, grime, saturate(splash * 0.6 * dirty));
    colour = lerp(colour, grime, saturate(streaks * 0.75 * dirty));
    colour = lerp(colour, dust, saturate(upward * sky * fine * 0.8 * dirty));
    colour *= 1.0 - 0.4 * rain * sky;

    Albedo = colour;
    Metallic = metalSmooth.r;
    Smoothness = saturate(metalSmooth.a + rain * sky * 0.4 * (1.0 - metalSmooth.a));
    NormalOut = normalize(lerp(n, BW_TriNormal(ConcreteNormal, PositionWS, n, blend, 1.0 / 3.0), 0.6 * rough));
    Emission = glow * EmissionStrength;
    AmbientOcclusion = 1.0 - 0.5 * occlusion;
}

void BuildingWear_half(float2 UV, float3 PositionWS, float3 NormalWS, float4 Masks,
    UnityTexture2D PaletteColour, UnityTexture2D PaletteEmission, UnityTexture2D PaletteMetalSmooth,
    UnityTexture2D BiomeTable, float BiomeIndex, float Wear, float EmissionStrength,
    UnityTexture2D ConcreteColour, UnityTexture2D ConcreteNormal,
    out float3 Albedo, out float Metallic, out float Smoothness, out float3 NormalOut, out float3 Emission,
    out float AmbientOcclusion)
{
    BuildingWear_float(UV, PositionWS, NormalWS, Masks, PaletteColour, PaletteEmission, PaletteMetalSmooth, BiomeTable,
                       BiomeIndex, Wear, EmissionStrength, ConcreteColour, ConcreteNormal, Albedo, Metallic, Smoothness,
                       NormalOut, Emission, AmbientOcclusion);
}

#endif
