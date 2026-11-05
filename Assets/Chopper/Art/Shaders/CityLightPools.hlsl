// Light pools: the glow of every city lamp on the ground below it, from one map-wide texture (Setup step 8 bakes
// <kit>_LightPools.exr from the CityForge light list). Real point lights are capped (Forward+, 256), so most lamps
// have none; this gives every lamp its pool for the cost of one sample. Used by TerrainBiome and BuildingWear.
//   rgb = lamp light reaching the ground (linear, summed), a = height (m) of the strongest lamp at that texel.
// Only up-facing surfaces from the lamp height down to PoolDepth below it are lit: tower roofs above a street stay
// dark, and so do walls.
#ifndef CITY_LIGHT_POOLS_INCLUDED
#define CITY_LIGHT_POOLS_INCLUDED

#define CLP_POOL_DEPTH 18.0

float3 CLP_Pool(float3 PositionWS, float3 NormalWS, UnityTexture2D LightPools, float MapSize)
{
    float size = max(MapSize, 1.0);
    float2 uv = PositionWS.xz / size + 0.5;
    float2 edge = min(uv, 1.0 - uv) * size;
    float inside = saturate(min(edge.x, edge.y) / 6.0) * step(1.0, MapSize);
    float4 pool = SAMPLE_TEXTURE2D(LightPools.tex, LightPools.samplerstate, uv);
    float below = pool.a - PositionWS.y;                       // m from the lamp down to this surface
    float band = saturate((below + 0.5) * 2.0) * saturate(1.0 - below / CLP_POOL_DEPTH);
    float up = saturate(NormalWS.y * 1.25 - 0.1);
    return max(pool.rgb, 0.0) * (band * up * inside);
}

#endif
