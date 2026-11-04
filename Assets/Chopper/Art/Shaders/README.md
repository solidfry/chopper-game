# ChopperSkin shader graph (runtime skins)

`ChopperSkin.hlsl` paints a skin at runtime over a PartForge texture set: the game's skin method (owner decision,
29 Oct, after an A/B against baked skins). `ChopperSkin.shadergraph` (URP Lit) wraps it; it is made by
`Tools/PartForge/make_skin_graph.py` (run it again when the function's inputs change). The steps below are the same
graph by hand, for reference. The kit import step (Chopper → Setup → 5) makes one material per texture set with it,
and the kit models use it.

Why Shader Graph: it supports Entities Graphics (DOTS instancing) by itself, so each chopper can have its own skin
index without its own material.

## 1. Make the graph

1. Project window, this folder → **Create → Shader Graph → URP → Lit Shader Graph**. Name it **ChopperSkin** (the
   shader is then `Shader Graphs/ChopperSkin`; Setup looks for that name).
2. Open it. **Graph Inspector → Graph Settings:** Material **Lit**, Workflow **Metallic**, Surface **Opaque**,
   Fragment Normal Space **Tangent**, Precision **Single**.

## 2. Properties (Blackboard)

Reference names must be exactly these (the Name can be anything).

| Type | Reference | Default | Note |
|---|---|---|---|
| Texture2D | `_BaseColourMap` | white | |
| Texture2D | `_BaseMetalSmooth` | black | |
| Texture2D | `_BaseEmission` | black | |
| Texture2D | `_NormalMap` | **Normal Map** (mode "bump") | |
| Texture2D | `_OcclusionMap` | white | |
| Texture2D | `_SkinMask` | black | |
| Texture2D | `_SkinWear` | black | |
| Texture2D | `_SkinTable` | black | |
| Float | `_SkinIndex` | 0 | Node Settings: **Override Property Declaration** on, Shader Declaration **Hybrid Per Instance** |
| Vector2 | `_HeightRange` | (0, 1) | |
| Float | `_EmissionStrength` | 3 | |

## 3. Nodes

1. **Custom Function** node. Node Settings: Type **File**, Source **ChopperSkin.hlsl** (this folder), Name
   **ChopperSkin**.
2. Inputs, in this order:

   | Name | Type |
   |---|---|
   | PositionOS | Vector 3 |
   | NormalOS | Vector 3 |
   | UV | Vector 2 |
   | BaseColour | Texture 2D |
   | BaseMetalSmooth | Texture 2D |
   | BaseEmission | Texture 2D |
   | NormalMap | Texture 2D |
   | Occlusion | Texture 2D |
   | SkinMask | Texture 2D |
   | SkinWear | Texture 2D |
   | SkinTable | Texture 2D |
   | SkinIndex | Float |
   | HeightRange | Vector 2 |
   | EmissionStrength | Float |

3. Outputs, in this order: Albedo (Vector 3), Metallic (Float), Smoothness (Float), NormalTS (Vector 3),
   Emission (Vector 3), AmbientOcclusion (Float).
4. Connect: a **Position** node (Space **Object**) → PositionOS; a **Normal Vector** node (Space **Object**) →
   NormalOS; a **UV** node (UV0) → UV; each property → the input of the same name (`_BaseColourMap` → BaseColour,
   `_OcclusionMap` → Occlusion, the rest by name).
5. Connect the outputs to the Fragment block: Albedo → **Base Color**, Metallic → **Metallic**, Smoothness →
   **Smoothness**, NormalTS → **Normal (Tangent Space)**, Emission → **Emission**, AmbientOcclusion →
   **Ambient Occlusion**.
6. **Save Asset.** Fix any compile error that the node shows (send it to Claude).

## 4. Materials and preview

1. Chopper → Setup → **5. Import part kits**. It makes `Art/Kits/<kit>/Materials/<kit>_<Set>_Runtime.mat` and the skin
   table (a sub-asset of `Art/Kits/<kit>/<kit>_Skins.asset`).
2. Chopper → Setup → **Open skin preview scene** (`Art/Kits/SkinPreview.unity`): every Kit02 chassis with modules on
   its sockets. Select **Kit Preview**: its **Skin Preview** switches all of them. (Or add Skin Preview to any kit
   model and set its Library to `Kit02_Skins`.)
3. Pick a skin (right-click the component: **Next skin**). Baked skins exist only for the first skin, unless the kit
   is exported with the PartForge style key `"baked_skins": true` (then Baked / Runtime, **Swap**, **Flip every** A/B
   them again).
4. New skin, data only: in `Kit02_Skins`, add an entry (new id), set its Look, press Setup step 5 (rebuilds the table).
   No Blender, no new textures.

## Files

- `ChopperSkin.hlsl` — the shader maths. Its twin is `Tools/PartForge/partforge/runtime.py` (`shade`, `pattern`): keep
  them the same. PartForge uses the Python copy to render `previews/<kit>/skins_sheet.jpg`.
- Table layout: `runtime.encode` (Python) = `SkinTable.Encode` (C#); the edit-mode test `SkinTableTests` checks they
  agree on the Kit02 manifest.


# TerrainBiome shader graph (procedural terrain ground, every biome)

Written without the editor (4 Nov 2026): open it once in Unity and check it compiles.

- `TerrainBiome.hlsl`: the custom function. Inputs: world position, world normal, vertex colour (the biome masks the
  CityForge terrain FBX carries: R flow, G hollow, B made ground, A scree), `_BiomeTable`, `_BiomeIndex`.
- `TerrainBiome.shadergraph`: URP Lit graph around it, written by `Tools/CityForge/make_terrain_graph.py` (run it
  again when the function's inputs change).
- The biome table (`Assets/Chopper/Art/City/<kit>/Terrain/<kit>_BiomeTable.png`, one row of 16 pixels per biome:
  desert, rain_desert, arctic, jungle, volcanic, toxic) comes from `Tools/CityForge/cityforge/terrain_biomes.py`. The
  same rules make the Blender previews (`Tools/CityForge/previews/<kit>/biomes_sheet.jpg`).

Editor steps (until a Setup step does them):
1. Biome table texture: Texture Type Default, **sRGB off**, Filter **Point**, **no mipmaps**, Compression **None**,
   Wrap Clamp.
2. Material: Create → Material, shader `Shader Graphs/TerrainBiome`; set `_BiomeTable` to the table, `_BiomeIndex` to
   the biome row (0 desert ... 5 toxic).
3. Terrain chunk FBX (`Terrain/Terrain_*.fbx`): Model import, keep vertex colours on; remap the `City01_Terrain`
   material to the new material.
4. Check: flow lines darker and wet in the gullies, strata bands on cliffs, concrete on the city floor and roads.
