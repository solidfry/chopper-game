// Chopper skin shader: paints a skin at runtime over a PartForge texture set (Shader Graph Custom Function, File mode,
// name "ChopperSkin"). One material per texture set serves every skin: the skin is a row of the skin table texture,
// chosen by _SkinIndex (per chopper: an Entities Graphics per-instance override or a MaterialPropertyBlock).
//
// The same maths, line for line, is in Tools/PartForge/partforge/runtime.py (shade, pattern): PartForge runs it to
// check the result against the baked skins and to render the runtime skin sheet. Change both together.
//
// Maps (texture set, skin-independent): base colour / metallic-smoothness / emission (the kit's first skin), normal,
// occlusion; SkinMask (R paint, G replacement panels, B warning paint, A tone: 0 dark, 0.5 hull, 1 light);
// SkinWear (R dirt / 3, G soot, B paint blotches, A wear threshold / 2). Table row layout: runtime.encode.

#ifndef CHOPPER_SKIN_INCLUDED
#define CHOPPER_SKIN_INCLUDED

SamplerState chopper_skin_point_clamp_sampler;

static const float3 SKIN_BARE = float3(0.2747, 0.2664, 0.2384);  // #8f8d86, linear
static const float3 SKIN_DIRT = float3(0.0242, 0.0194, 0.0137);  // #2b261f, linear
static const float3 SKIN_SOOT = float3(0.0048, 0.0044, 0.0040);  // #0f0e0d, linear

float SkinHash(int3 c, int seed)
{
    uint h = ((uint)c.x * 0x8DA6B343u) ^ ((uint)c.y * 0xD8163841u) ^ ((uint)c.z * 0xCB1AB31Fu) ^ ((uint)seed * 0x165667B1u);
    h ^= h >> 13;
    h *= 0x85EBCA6Bu;
    h ^= h >> 16;
    h *= 0xC2B2AE35u;
    h ^= h >> 16;
    return (float)h * (1.0 / 4294967296.0);
}

float SkinValueNoise(float3 p, int seed)
{
    float3 cell = floor(p);
    float3 f = p - cell;
    int3 i = (int3)cell;
    float3 u = f * f * (3.0 - 2.0 * f);
    float result = 0.0;
    [unroll] for (int dx = 0; dx < 2; dx++)
    [unroll] for (int dy = 0; dy < 2; dy++)
    [unroll] for (int dz = 0; dz < 2; dz++)
    {
        float w = (dx ? u.x : 1.0 - u.x) * (dy ? u.y : 1.0 - u.y) * (dz ? u.z : 1.0 - u.z);
        result += SkinHash(i + int3(dx, dy, dz), seed) * w;
    }
    return result;
}

float SkinFbm(float3 p, int octaves, int seed)
{
    float total = 0.0, amplitude = 1.0, norm = 0.0;
    for (int octave = 0; octave < octaves; octave++)
    {
        total += amplitude * SkinValueNoise(p * exp2((float)octave), seed + octave * 101);
        norm += amplitude;
        amplitude *= 0.5;
    }
    return total / norm;
}

float3 SkinWarp(float3 p, float amount, int seed)
{
    if (amount <= 0.0) return p;
    float3 offset = float3(SkinFbm(p * 0.5, 3, seed), SkinFbm(p * 0.5, 3, seed + 17), SkinFbm(p * 0.5, 3, seed + 34)) - 0.5;
    return p + offset * amount * 2.0;
}

int SkinLayers(float3 q, float4 thresholds, int seed, float warp, int count)
{
    q = SkinWarp(q, warp, seed);
    int index = 0;
    for (int k = 1; k < min(count, 4); k++)
    {
        float n = SkinFbm(q + k * 13.7, 4, seed + k * 31);
        if (n > thresholds[k - 1]) index = k;
    }
    return index;
}

// Two coordinates on the plane facing the normal's main axis (no blending) and that axis.
void SkinDominantPlane(float3 p, float3 n, out float u, out float v, out int axis)
{
    float3 a = abs(n);
    axis = (a.x >= a.y && a.x >= a.z) ? 0 : (a.y >= a.z ? 1 : 2);
    u = axis == 0 ? p.z : p.x;
    v = axis == 1 ? p.z : p.y;
}

float SkinVoronoiCell(float3 q, int seed)
{
    int3 base = (int3)floor(q);
    float best = 1e9;
    float value = 0.0;
    for (int dx = -1; dx <= 1; dx++)
    for (int dy = -1; dy <= 1; dy++)
    for (int dz = -1; dz <= 1; dz++)
    {
        int3 c = base + int3(dx, dy, dz);
        float3 feature = (float3)c + float3(SkinHash(c, seed), SkinHash(c, seed + 1), SkinHash(c, seed + 2));
        float3 d = q - feature;
        float d2 = dot(d, d);
        if (d2 < best)
        {
            best = d2;
            value = SkinHash(c, seed + 99);
        }
    }
    return value;
}

// Pointy hex grid, unit spacing: cell id and the distance to the cell edge (0 on the rim, 0.5 in the middle).
void SkinHexGrid(float u, float v, out int2 id, out float edge)
{
    const float s = 1.7320508;
    float x = u * s / 3.0 - v / 3.0;
    float z = v * 2.0 / 3.0;
    float y = -x - z;
    float rx = round(x), ry = round(y), rz = round(z);
    float dx = abs(rx - x), dy = abs(ry - y), dz = abs(rz - z);
    bool fixX = dx > dy && dx > dz;
    bool fixY = !fixX && dy > dz;
    if (fixX) rx = -ry - rz;
    if (fixY) ry = -rx - rz;
    if (!fixX && !fixY) rz = -rx - ry;
    float cu = s * (rx + rz / 2.0);
    float cv = 1.5 * rz;
    float lu = abs(u - cu), lv = abs(v - cv);
    edge = (1.0 - max(lu * 2.0 / s, lu / s + lv)) * 0.5;
    id = int2((int)rx, (int)rz);
}

float4 SkinRow(UnityTexture2D table, float row, int texel)
{
    float2 uv = float2((texel + 0.5) * table.texelSize.x, (row + 0.5) * table.texelSize.y);
    return SAMPLE_TEXTURE2D_LOD(table.tex, chopper_skin_point_clamp_sampler, uv, 0);
}

// Paint colour and glow index (0 none, 1 glow A, 2 glow B) of the skin's pattern at model-space point p.
float3 SkinPattern(UnityTexture2D table, float row, float3 p, float3 n, float height, out int glow)
{
    float4 c0 = SkinRow(table, row, 0), c1 = SkinRow(table, row, 1), c2 = SkinRow(table, row, 2), c3 = SkinRow(table, row, 3);
    float4 t4 = SkinRow(table, row, 4), t6 = SkinRow(table, row, 6);
    float3 colours[4] = { c0.rgb, c1.rgb, c2.rgb, c3.rgb };
    int kind = (int)round(c0.a);
    float scale = c1.a;
    int seed = (int)round(c2.a);
    float warp = c3.a;
    int count = max((int)round(t6.w), 1);
    float size = t4.w;
    bool glowOn = t6.z > 0.5;
    glow = 0;
    int index = 0;
    // One exit point: early returns inside [branch] blocks give "potentially uninitialized" warnings on Vulkan / GL.
    float3 result = 0;
    bool painted = false;

    [branch] if (kind == 1)  // camo
    {
        index = SkinLayers(p / scale, t4, seed, warp, count);
    }
    else if (kind == 2)  // digital
    {
        float u, v; int axis;
        SkinDominantPlane(p, n, u, v, axis);
        float depth = axis == 0 ? p.x : (axis == 1 ? p.y : p.z);
        float3 snapped = float3(floor(u / size), floor(v / size), floor(depth / (size * 4.0))) * size;
        index = SkinLayers(snapped / scale, t4, seed, warp, count);
    }
    else if (kind == 3)  // splinter
    {
        float stretch = SkinRow(table, row, 7).a;
        float3 q = p / scale * float3(1.0, 1.0, 1.0 / stretch);
        q += (SkinFbm(q * 0.7, 2, seed) - 0.5) * 0.25;
        float cell = SkinVoronoiCell(q, seed);
        index = min((cell >= t4.x ? 1 : 0) + (cell >= t4.y ? 1 : 0) + (cell >= t4.z ? 1 : 0), count - 1);
    }
    else if (kind == 4)  // tiger
    {
        float warped = p.z / size + (SkinFbm(p * 0.8, 3, seed) - 0.5) * warp * 2.0 + p.y * 0.35;
        float phase = frac(warped);
        float width = t4.x * (0.6 + 0.8 * SkinFbm(p * 1.7 + 5.0, 2, seed + 3));
        index = phase < width ? 1 : (phase < width * 1.45 ? (count > 2 ? 2 : 1) : 0);
    }
    else if (kind == 5)  // hex
    {
        float u, v; int axis;
        SkinDominantPlane(p, n, u, v, axis);
        int2 id; float edge;
        SkinHexGrid(u / size, v / size, id, edge);
        index = min((int)(SkinHash(int3(id.x, id.y, axis), seed) * count), count - 1);
        float3 colour = colours[index];
        if (edge < 0.06) colour = SkinRow(table, row, 14).rgb;
        if (glowOn)
        {
            float4 t8 = SkinRow(table, row, 8), t9 = SkinRow(table, row, 9), t11 = SkinRow(table, row, 11);
            bool lit = SkinHash(int3(id.x + 7, id.y - 3, axis), seed + 5) < t8.a;
            bool nearEdge = edge < t9.a / size * 2.5;
            int pick = clamp((int)(SkinHash(int3(id.x, id.y, axis + 9), seed) * t11.z), 0, 1);
            glow = (nearEdge && lit) ? pick + 1 : 0;
        }
        result = colour;
        painted = true;
    }
    else if (kind == 6)  // synthwave
    {
        index = SkinFbm(p * 0.6, 2, seed) > 0.62 ? 1 : 0;
        float3 colour = colours[index];
        float4 t10 = SkinRow(table, row, 10);
        float t = saturate((height - t10.x) / (t10.y - t10.x));
        float stripe = frac(height * 22.0);
        bool inBand = height > t10.x && height < t10.y && stripe > 0.15 + 0.55 * (1.0 - t);
        float3 sun[3] = { SkinRow(table, row, 14).rgb, SkinRow(table, row, 15).rgb, SkinRow(table, row, 16).rgb };
        float tc = (1.0 - t) * 2.0;
        int k = clamp((int)floor(tc), 0, 1);
        float f = tc - k;
        if (inBand) colour = sun[k] * (1.0 - f) + sun[k + 1] * f;
        if (glowOn)
        {
            float u, v; int axis;
            SkinDominantPlane(p, n, u, v, axis);
            float width = SkinRow(table, row, 9).a / size;
            float du = abs(u / size - round(u / size));
            float dv = abs(v / size - round(v / size));
            glow = ((du < width || dv < width) && !inBand && height < 0.75) ? 1 : 0;
        }
        result = colour;
        painted = true;
    }
    else if (kind == 7)  // livery
    {
        float4 t10 = SkinRow(table, row, 10), t11 = SkinRow(table, row, 11);
        float ax = abs(p.x);
        if (n.y > 0.45 && ax > t10.z && ax < t10.w) index = 1;
        bool side = abs(n.x) > 0.4;
        if (side && abs(height - t11.x) < t11.y) index = 1;
        if (side && abs(height - t11.x - t11.y * 1.7) < t11.y * 0.35) index = 2;
        if (n.y < -0.55) index = 2;
    }
    return painted ? result : colours[min(index, 3)];
}

// Blotches, bare metal where the skin's wear is above the threshold, dirt, soot, a little occlusion.
void SkinFinish(float4 t5, float4 wear, float ao, bool emissive, inout float3 c, inout float metal, inout float smooth,
    inout float3 emission)
{
    float blotch = wear.b;
    c *= 0.92 + 0.16 * blotch;
    bool bare = t5.z > 0.0 && t5.z > wear.a * 2.0;
    if (bare)
    {
        c = SKIN_BARE * (0.8 + 0.4 * blotch);
        metal = 0.85;
        smooth = 0.42;
        emission = 0.0;
        emissive = false;
    }
    float amount = emissive ? 0.0 : clamp(wear.r * 3.0 * t5.w, 0.0, 0.85);
    c = c * (1.0 - amount) + SKIN_DIRT * amount * 0.6;
    smooth *= 1.0 - amount * 0.6;
    float soot = emissive ? 0.0 : wear.g;
    c = c * (1.0 - soot * 0.85) + SKIN_SOOT * soot * 0.85;
    smooth *= 1.0 - soot * 0.7;
    metal *= 1.0 - soot * 0.8;
    c *= 0.72 + 0.28 * ao;
}

void ChopperSkin_float(float3 PositionOS, float3 NormalOS, float2 UV,
    UnityTexture2D BaseColour, UnityTexture2D BaseMetalSmooth, UnityTexture2D BaseEmission, UnityTexture2D NormalMap,
    UnityTexture2D Occlusion, UnityTexture2D SkinMask, UnityTexture2D SkinWear, UnityTexture2D SkinTable,
    float SkinIndex, float2 HeightRange, float EmissionStrength,
    out float3 Albedo, out float Metallic, out float Smoothness, out float3 NormalTS, out float3 Emission,
    out float AmbientOcclusion)
{
    float3 base = SAMPLE_TEXTURE2D(BaseColour.tex, BaseColour.samplerstate, UV).rgb;
    float4 baseMS = SAMPLE_TEXTURE2D(BaseMetalSmooth.tex, BaseMetalSmooth.samplerstate, UV);
    float3 baseGlow = SAMPLE_TEXTURE2D(BaseEmission.tex, BaseEmission.samplerstate, UV).rgb;
    float occlusion = SAMPLE_TEXTURE2D(Occlusion.tex, Occlusion.samplerstate, UV).r;
    float4 mask = SAMPLE_TEXTURE2D(SkinMask.tex, SkinMask.samplerstate, UV);
    float4 wear = SAMPLE_TEXTURE2D(SkinWear.tex, SkinWear.samplerstate, UV);
    NormalTS = UnpackNormal(SAMPLE_TEXTURE2D(NormalMap.tex, NormalMap.samplerstate, UV));
    AmbientOcclusion = occlusion;

    float row = floor(SkinIndex + 0.5);
    float4 t5 = SkinRow(SkinTable, row, 5), t6 = SkinRow(SkinTable, row, 6);
    float ao = saturate((occlusion - 0.35) / 0.65);
    float zone = saturate(mask.r + t6.x * mask.g);
    float hazardZone = t6.y * mask.b;
    float a = mask.a;
    float3 dark = SkinRow(SkinTable, row, 12).rgb, light = SkinRow(SkinTable, row, 13).rgb;
    float3 tone = (dark + (1.0 - dark) * saturate(a * 2.0)) * (1.0 - saturate(a * 2.0 - 1.0)) + light * saturate(a * 2.0 - 1.0);
    float height = (PositionOS.y - HeightRange.x) / max(HeightRange.y - HeightRange.x, 1e-3);

    float3 paint = base;
    float metal = baseMS.r, smooth = baseMS.a;
    float3 emission = 0.0;
    [branch] if (zone > 0.0)
    {
        int glow;
        paint = SkinPattern(SkinTable, row, PositionOS, normalize(NormalOS), height, glow) * tone;
        float3 glowColour = glow == 2 ? SkinRow(SkinTable, row, 9).rgb : SkinRow(SkinTable, row, 8).rgb;
        bool glowing = glow > 0;
        if (glowing) paint = glowColour;
        if (t5.x >= 0.0) { metal = t5.x; smooth = t5.y; }
        if (glowing) { smooth = 0.8; emission = glowColour; }
        SkinFinish(t5, wear, ao, glowing, paint, metal, smooth, emission);
    }
    Albedo = lerp(base, paint, zone);
    Metallic = lerp(baseMS.r, metal, zone);
    Smoothness = lerp(baseMS.a, smooth, zone);
    float3 glowOut = lerp(baseGlow, emission, zone);

    [branch] if (hazardZone > 0.0)
    {
        float3 marks = SkinRow(SkinTable, row, 7).rgb;
        float markMetal = baseMS.r, markSmooth = baseMS.a;
        float3 none = 0.0;
        bool lit = max(baseGlow.r, max(baseGlow.g, baseGlow.b)) > 0.05;
        SkinFinish(t5, wear, ao, lit, marks, markMetal, markSmooth, none);
        Albedo = lerp(Albedo, marks, hazardZone);
        Metallic = lerp(Metallic, markMetal, hazardZone);
        Smoothness = lerp(Smoothness, markSmooth, hazardZone);
        glowOut *= 1.0 - hazardZone;
    }
    Emission = glowOut * EmissionStrength;
}

// Half-precision graphs call this one; the work is done in float.
void ChopperSkin_half(float3 PositionOS, float3 NormalOS, float2 UV,
    UnityTexture2D BaseColour, UnityTexture2D BaseMetalSmooth, UnityTexture2D BaseEmission, UnityTexture2D NormalMap,
    UnityTexture2D Occlusion, UnityTexture2D SkinMask, UnityTexture2D SkinWear, UnityTexture2D SkinTable,
    float SkinIndex, float2 HeightRange, float EmissionStrength,
    out half3 Albedo, out half Metallic, out half Smoothness, out half3 NormalTS, out half3 Emission,
    out half AmbientOcclusion)
{
    float3 albedo, normalTS, emission;
    float metallic, smoothness, ambientOcclusion;
    ChopperSkin_float(PositionOS, NormalOS, UV, BaseColour, BaseMetalSmooth, BaseEmission, NormalMap, Occlusion, SkinMask,
        SkinWear, SkinTable, SkinIndex, HeightRange, EmissionStrength, albedo, metallic, smoothness, normalTS, emission,
        ambientOcclusion);
    Albedo = albedo; Metallic = metallic; Smoothness = smoothness; NormalTS = normalTS; Emission = emission;
    AmbientOcclusion = ambientOcclusion;
}

#endif
