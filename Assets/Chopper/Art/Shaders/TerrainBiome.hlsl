// TerrainBiome: procedural ground for CityForge terrain chunks, one shader for every biome.
//
// The terrain FBX carries biome-free facts in its vertex colour (linear): R flow (gullies, river beds), G hollow
// (concave ground), B made ground (city floor, roads, channel), A scree (loose rock below cliffs). A biome is one row
// of 16 pixels in _BiomeTable (Tools/CityForge/cityforge/terrain_biomes.py writes it; the same rules make the Blender
// previews). Colours come from world position and noise; CC0 detail maps (rock on three axes, sand and mud from
// above) only shade them and add normal detail, so cliffs do not stretch. Output normal: world space.
//
// Table row (sRGB colours, decoded here): 0 groundA, 1 groundB, 2 sediment, 3-6 rock strata, 7 cliff, 8 scree,
// 9 cover (a = amount), 10 glow (a = strength / 10), 11 concrete, 12 (band / 50, cover slope, (cover height + 100) /
// 1000, glow flow), 13 (wet, flow wet, roughness, has cover).
//
// Written without the editor (4 Nov 2026): compile and check in Unity. TerrainBiome.shadergraph wraps it
// (Tools/CityForge/make_city_graphs.py writes the graph).

#ifndef TERRAIN_BIOME_INCLUDED
#define TERRAIN_BIOME_INCLUDED

SamplerState terrain_biome_point_clamp_sampler;

float TB_Hash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float TB_Noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = TB_Hash(i);
    float b = TB_Hash(i + float2(1.0, 0.0));
    float c = TB_Hash(i + float2(0.0, 1.0));
    float d = TB_Hash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float TB_Fbm(float2 p, int octaves)
{
    float total = 0.0, amplitude = 0.5, norm = 0.0;
    for (int k = 0; k < octaves; k++)
    {
        total += amplitude * TB_Noise(p);
        norm += amplitude;
        p = p * 2.03 + 17.1;
        amplitude *= 0.5;
    }
    return total / norm;
}

float3 TB_Linear(float3 c)
{
    float3 low = c / 12.92;
    float3 high = pow((c + 0.055) / 1.055, 2.4);
    return lerp(high, low, step(c, 0.04045));
}

float4 TB_Row(UnityTexture2D table, float row, float rows, int texel)
{
    // PNG row 0 (the first biome) is the top of the texture: v = 1 at the top.
    float2 uv = float2((texel + 0.5) * table.texelSize.x, 1.0 - (row + 0.5) / rows);
    return SAMPLE_TEXTURE2D_LOD(table.tex, terrain_biome_point_clamp_sampler, uv, 0);
}


// Detail maps (CC0, Assets/Chopper/Art/ThirdParty/CC0): sampled with their own sampler (bilinear, repeat).
float3 TB_Tri(UnityTexture2D t, float3 p, float3 blend, float scale)
{
    return SAMPLE_TEXTURE2D(t.tex, t.samplerstate, p.zy * scale).rgb * blend.x
         + SAMPLE_TEXTURE2D(t.tex, t.samplerstate, p.xz * scale).rgb * blend.y
         + SAMPLE_TEXTURE2D(t.tex, t.samplerstate, p.xy * scale).rgb * blend.z;
}

// Three-axis normal map, "whiteout" blend (after Ben Golus, "Normal Mapping for a Triplanar Shader"); world space.
float3 TB_TriNormal(UnityTexture2D t, float3 p, float3 n, float3 blend, float scale)
{
    float3 tx = UnpackNormal(SAMPLE_TEXTURE2D(t.tex, t.samplerstate, p.zy * scale));
    float3 ty = UnpackNormal(SAMPLE_TEXTURE2D(t.tex, t.samplerstate, p.xz * scale));
    float3 tz = UnpackNormal(SAMPLE_TEXTURE2D(t.tex, t.samplerstate, p.xy * scale));
    tx = float3(tx.xy + n.zy, abs(tx.z) * n.x);
    ty = float3(ty.xy + n.xz, abs(ty.z) * n.y);
    tz = float3(tz.xy + n.xy, abs(tz.z) * n.z);
    return normalize(tx.zyx * blend.x + ty.xzy * blend.y + tz.xyz * blend.z);
}

float3 TB_Blend(float3 n)
{
    float3 blend = pow(abs(n), 4.0);
    return blend / max(1e-5, dot(blend, 1.0));
}

// Luminance of a detail colour as a factor round 1 (0.55..1.45): the detail tints nothing, it only shades.
float TB_Detail(float3 c)
{
    return clamp(dot(c, float3(0.2126, 0.7152, 0.0722)) * 2.0 + 0.2, 0.55, 1.45);
}

float3 TB_Colour(UnityTexture2D table, float row, float rows, int texel)
{
    return TB_Linear(TB_Row(table, row, rows, texel).rgb);
}

void TerrainBiome_float(float3 PositionWS, float3 NormalWS, float4 Masks, UnityTexture2D BiomeTable, float BiomeIndex,
    UnityTexture2D RockColour, UnityTexture2D RockNormal, UnityTexture2D SandColour, UnityTexture2D SandNormal,
    UnityTexture2D MudColour, UnityTexture2D CoverColour, float DetailStrength,
    out float3 Albedo, out float Metallic, out float Smoothness, out float3 NormalOut, out float3 Emission,
    out float AmbientOcclusion)
{
    float rows = max(1.0, round(1.0 / BiomeTable.texelSize.y));
    float row = clamp(round(BiomeIndex), 0.0, rows - 1.0);
    float4 p12 = TB_Row(BiomeTable, row, rows, 12);
    float4 p13 = TB_Row(BiomeTable, row, rows, 13);
    float band = max(1.0, p12.x * 50.0);
    float coverSlope = p12.y;
    float coverHeight = p12.z * 1000.0 - 100.0;
    float glowFlow = p12.w;
    float wetBase = p13.x, flowWet = p13.y, roughness = p13.z, hasCover = p13.w;

    float flow = Masks.r, hollow = Masks.g, made = Masks.b, scree = Masks.a;
    float slope = 1.0 - saturate(normalize(NormalWS).y);
    float2 xz = PositionWS.xz;
    float macro = TB_Fbm(xz / 180.0, 3);
    float detail = TB_Fbm(xz / 9.0, 4);
    float shade = 0.85 + 0.3 * detail;

    float3 n = normalize(NormalWS);
    float3 blend = TB_Blend(n);
    float sandF = lerp(1.0, TB_Detail(SAMPLE_TEXTURE2D(SandColour.tex, SandColour.samplerstate, xz / 4.0).rgb), DetailStrength);
    float mudF = lerp(1.0, TB_Detail(SAMPLE_TEXTURE2D(MudColour.tex, MudColour.samplerstate, xz / 5.0).rgb), DetailStrength);
    float rockF = lerp(1.0, TB_Detail(TB_Tri(RockColour, PositionWS, blend, 1.0 / 8.0)), DetailStrength);
    float3 ground = lerp(TB_Colour(BiomeTable, row, rows, 0), TB_Colour(BiomeTable, row, rows, 1),
                         smoothstep(0.35, 0.65, macro)) * shade * sandF;
    // Strata: 4 colours by height band, warped by noise.
    float warp = (TB_Fbm(xz / 90.0, 2) - 0.5) * 12.0;
    int strata = (int)floor(frac((PositionWS.y + warp) / (band * 4.0)) * 4.0);
    float3 rock = TB_Colour(BiomeTable, row, rows, 3 + clamp(strata, 0, 3)) * shade * rockF;
    rock = lerp(rock, TB_Colour(BiomeTable, row, rows, 7), smoothstep(0.7, 0.95, slope));
    float cliff = smoothstep(0.30, 0.55, slope);
    float3 colour = lerp(ground, rock, cliff);
    float open = 1.0 - cliff;
    colour = lerp(colour, TB_Colour(BiomeTable, row, rows, 8), scree * open);
    colour = lerp(colour, TB_Colour(BiomeTable, row, rows, 2) * mudF, saturate(hollow * 0.8 + flow * 0.6) * open);

    float4 cover = TB_Row(BiomeTable, row, rows, 9);
    float coverMask = (1.0 - smoothstep(coverSlope - 0.08, coverSlope + 0.02, slope))
                    * smoothstep(coverHeight - 10.0, coverHeight + 10.0, PositionWS.y)
                    * smoothstep(0.35, 0.6, TB_Fbm(xz / 25.0, 3)) * cover.a * hasCover;
    float coverF = lerp(1.0, TB_Detail(SAMPLE_TEXTURE2D(CoverColour.tex, CoverColour.samplerstate, xz / 6.0).rgb),
                        DetailStrength * 0.7);
    colour = lerp(colour, TB_Linear(cover.rgb) * coverF, coverMask);

    float wet = saturate(wetBase + flow * flowWet);
    colour *= 1.0 - 0.45 * wet;
    colour = lerp(colour, TB_Colour(BiomeTable, row, rows, 11), smoothstep(0.3, 0.7, made));

    float4 glow = TB_Row(BiomeTable, row, rows, 10);
    float glowMask = smoothstep(glowFlow, glowFlow + 0.15, flow) * (1.0 - saturate(made));
    Emission = TB_Linear(glow.rgb) * glow.a * 10.0 * glowMask;

    Albedo = colour;
    Metallic = 0.0;
    Smoothness = 1.0 - saturate(roughness - wet * 0.55);
    // Detail normals: sand from above on open ground, rock on three axes on cliffs; none on made ground.
    float3 sandN = UnpackNormal(SAMPLE_TEXTURE2D(SandNormal.tex, SandNormal.samplerstate, xz / 4.0));
    sandN = normalize(float3(sandN.x + n.x, abs(sandN.z) * n.y, sandN.y + n.z));
    float3 rockN = TB_TriNormal(RockNormal, PositionWS, n, blend, 1.0 / 8.0);
    float3 detailN = normalize(lerp(sandN, rockN, cliff));
    NormalOut = normalize(lerp(n, detailN, DetailStrength * 0.8 * (1.0 - smoothstep(0.3, 0.7, made))));
    AmbientOcclusion = 1.0 - 0.35 * hollow;
}

void TerrainBiome_half(float3 PositionWS, float3 NormalWS, float4 Masks, UnityTexture2D BiomeTable, float BiomeIndex,
    UnityTexture2D RockColour, UnityTexture2D RockNormal, UnityTexture2D SandColour, UnityTexture2D SandNormal,
    UnityTexture2D MudColour, UnityTexture2D CoverColour, float DetailStrength,
    out float3 Albedo, out float Metallic, out float Smoothness, out float3 NormalOut, out float3 Emission,
    out float AmbientOcclusion)
{
    TerrainBiome_float(PositionWS, NormalWS, Masks, BiomeTable, BiomeIndex, RockColour, RockNormal, SandColour,
                       SandNormal, MudColour, CoverColour, DetailStrength, Albedo, Metallic, Smoothness, NormalOut, Emission,
                       AmbientOcclusion);
}

#endif
