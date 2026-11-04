// BuildingWear: the CityForge building material. Palette colours (one 5 x 5 swatch texture per kit, UV0) graded with
// wear and grime.
//
// The building FBX carries per-vertex masks in its vertex colour (linear; Tools/CityForge/cityforge/wear.py):
// R occlusion, G splash (near the surface below), B drip (something stands above: sills, ledges), A sky exposure.
// The shader adds what low-poly vertices cannot hold: rain streaks down vertical faces from drip spots, fine noise,
// dust on exposed ledges and roofs in the biome's dust colour (the biome table: cover colour, else ground colour),
// rain wetness from the biome. Glass and lit surfaces (smooth or glowing swatches) stay clean.
//
// Building surfaces (4 Nov): windows (PaletteMetalSmooth G = 1: glass and lit-window swatches) look into fake rooms
// ("interior mapping", after Joost van Dongen 2008): a ray from the camera into a grid of rooms in the building's own
// space (so rooms line up with its facades whatever its yaw) finds a wall, the floor or the ceiling; lit windows show
// a lamp-lit room with darker corners and furniture shapes, glass shows a dim room behind the reflection. Every copy
// of a building differs a little (colour, dirt, which rooms are bright): a hash of the object's position. Rough
// walls get panel seams (1.5 m x 3 m, building space) with stains below them. Flat ground and roofs get puddles in
// wet biomes (smooth, dark, flat).
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

// Fake room behind a window. p: building-space position, dir: building-space view ray (camera to point), seed: per
// copy. Returns (shade 0..1.3 for the room light, depth in rooms); furniture is dark, the ceiling lamp bright.
float2 BW_Room(float3 p, float3 dir, float seed)
{
    const float3 size = float3(4.0, 3.4, 4.0);
    float3 q = p / size;
    float3 cell = floor(q - dir * 1e-3);
    float3 local = q - cell;
    float3 d = dir / size;
    d += 1e-5 * (1.0 - step(1e-5, abs(d)));          // no division by zero
    float3 t3 = (step(0.0, d) - local) / d;          // ray length to the far plane on each axis
    float t = min(t3.x, min(t3.y, t3.z));
    float3 hit = local + d * t;
    float room = BW_Hash(cell.xz + cell.y * 7.31 + seed);
    float shade;
    if (t3.y <= t3.x && t3.y <= t3.z)
        shade = d.y > 0.0 ? 1.1 + 0.2 * (1.0 - length(hit.xz - 0.5) * 2.0) : 0.35;   // ceiling (lamp) / floor
    else
    {
        float2 wall = t3.x < t3.z ? hit.zy : hit.xy;   // across the wall, up
        shade = 0.55 + 0.3 * room;
        // Furniture: a desk or sofa band low on the wall, a cupboard on one side, by the room's hash.
        float desk = step(wall.y, 0.32) * step(abs(wall.x - frac(room * 7.0) * 0.6 - 0.2), 0.22);
        float cupboard = step(wall.y, 0.7) * step(frac(room * 13.0), 0.5) * step(abs(wall.x - 0.85), 0.1);
        shade *= 1.0 - 0.7 * saturate(desk + cupboard);
    }
    shade *= 0.55 + 0.45 * room;                    // some rooms brighter than others
    return float2(shade / (1.0 + 0.35 * t), t);
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
    float window = step(0.5, metalSmooth.g);

    // Per copy: a hash of the object's position (0..1), and building space for rooms and panels.
    float3 origin = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float seed = BW_Hash(origin.xz * 0.013 + 0.37);
    float3 positionOS = TransformWorldToObject(PositionWS);
    float3 normalOS = normalize(TransformWorldToObjectDir(NormalWS));

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
    dirty *= 0.7 + 0.6 * seed;                       // some copies dirtier than others

    float streakNoise = BW_Fbm(float2((PositionWS.x + PositionWS.z) * 1.2, PositionWS.y * 0.04), 2);
    float streaks = vertical * (drip * 0.8 + 0.2) * saturate((streakNoise - 0.45) * 3.0) * sky;
    float fine = BW_Fbm(PositionWS.xz * 0.6 + PositionWS.y * 0.3, 3);

    // CC0 concrete detail on three axes, rough surfaces only. Two scales (3 m, and 7.7 m turned 0.6 rad about Y)
    // blended by noise so the tiling does not repeat, and a slow light/dark drift (same as the Blender preview).
    float3 blend = BW_Blend(n);
    float rough = saturate((1.0 - metalSmooth.a - 0.25) * 1.6);
    float3 turned = float3(PositionWS.x * 0.825 - PositionWS.z * 0.565, PositionWS.y, PositionWS.x * 0.565 + PositionWS.z * 0.825);
    float2 slant = float2(PositionWS.x + PositionWS.y * 0.7, PositionWS.z - PositionWS.y * 0.7);
    float pick = smoothstep(0.35, 0.65, BW_Fbm(slant / 18.0, 2));
    float detailRaw = lerp(BW_Detail(BW_Tri(ConcreteColour, PositionWS, blend, 1.0 / 3.0)),
                           BW_Detail(BW_Tri(ConcreteColour, turned, blend, 1.0 / 7.7)), pick);
    detailRaw *= 0.85 + 0.3 * BW_Fbm(slant / 30.0, 2);
    float detailF = lerp(1.0, detailRaw, 0.6 * rough);

    float3 colour = base * detailF;
    // Per-copy tint: up to 6 % lighter or darker, a little warmer or cooler (not on windows).
    float3 tint = 1.0 + (seed - 0.5) * float3(0.14, 0.12, 0.08) + (BW_Hash(float2(seed, 3.1)) - 0.5) * float3(0.04, 0.0, -0.04);
    colour *= lerp(tint, 1.0, window);

    // Panel seams on rough vertical walls: joints every 1.5 m across and 3 m up (building space), a stain below each
    // horizontal joint.
    float across = abs(normalOS.x) > abs(normalOS.z) ? positionOS.z : positionOS.x;
    float2 panel = float2(across / 1.5, positionOS.y / 3.0);
    float2 joint = abs(frac(panel) - 0.5);
    float seam = (smoothstep(0.47, 0.495, joint.x) + smoothstep(0.475, 0.497, joint.y)) * vertical;
    float stain = vertical * smoothstep(0.0, 0.35, frac(panel.y)) * (1.0 - smoothstep(0.35, 0.5, frac(panel.y)))
                * step(0.5, BW_Hash(floor(panel) + seed));
    colour *= 1.0 - saturate(seam) * 0.22 * rough * (1.0 - window);
    colour = lerp(colour, grime, stain * 0.12 * rough * (1.0 - window) * Wear);
    colour *= lerp(1.0, 0.6, saturate(occlusion - 0.25) * 0.6 * dirty);
    colour = lerp(colour, grime, saturate(splash * 0.6 * dirty));
    colour = lerp(colour, grime, saturate(streaks * 0.75 * dirty));
    colour = lerp(colour, dust, saturate(upward * sky * fine * 0.8 * dirty));
    colour *= 1.0 - 0.4 * rain * sky;

    // Puddles: on flat open ground and roofs in wet biomes; dark, mirror-smooth, flat.
    float puddle = upward * sky * smoothstep(1.0 - rain * 0.9, 1.05 - rain * 0.9, BW_Fbm(PositionWS.xz / 6.0, 3))
                 * step(0.02, rain);

    // Fake rooms behind the windows.
    float3 cameraOS = TransformWorldToObject(_WorldSpaceCameraPos);
    float3 viewOS = normalize(positionOS - cameraOS);
    float2 room = BW_Room(positionOS - normalOS * 0.02, viewOS, seed * 17.0);
    float3 roomGlow = glow * room.x;
    colour = lerp(colour, lerp(colour, base * room.x * 0.5, 0.6), window);

    Albedo = lerp(colour, colour * 0.35, puddle);
    Metallic = metalSmooth.r;
    Smoothness = saturate(lerp(metalSmooth.a + rain * sky * 0.4 * (1.0 - metalSmooth.a), 0.96, puddle));
    NormalOut = normalize(lerp(n, BW_TriNormal(ConcreteNormal, PositionWS, n, blend, 1.0 / 3.0), 0.6 * rough * (1.0 - puddle)));
    Emission = lerp(glow, roomGlow, window) * EmissionStrength;
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
