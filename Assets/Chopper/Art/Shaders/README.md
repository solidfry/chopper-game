# ChopperSkin shader graph (runtime skins)

`ChopperSkin.hlsl` paints a skin at runtime over a PartForge texture set. It needs a small Shader Graph around it.
Shader Graph files cannot be written outside the editor, so make it once by hand (about 10 minutes). The kit import
step (Chopper → Setup → 5) then makes one material per texture set with it.

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

## 4. Materials and A/B

1. Chopper → Setup → **5. Import part kits**. It makes `Art/Kits/<kit>/Materials/<kit>_<Set>_Runtime.mat` and the skin
   table (a sub-asset of `Art/Kits/<kit>/<kit>_Skins.asset`).
2. Drag `Art/Kits/Kit02/Models/Chassis_Gunship.fbx` (and a module or two) into a scene. Add **Skin Preview** to it,
   set its Library to `Kit02_Skins`.
3. Pick a skin and Baked / Runtime. Right-click the component for **Next skin** and **Swap Baked / Runtime**; in play
   mode, **Flip every** (e.g. 1 s) swaps them on a timer. Both should look almost the same (PartForge checks the maths:
   the runtime result is within about 0.5 % of the baked one).
4. New skin, data only: in `Kit02_Skins`, add an entry (new id), set its Look, press Setup step 5 (rebuilds the table).
   No Blender, no new textures.

## Files

- `ChopperSkin.hlsl` — the shader maths. Its twin is `Tools/PartForge/partforge/runtime.py` (`shade`, `pattern`): keep
  them the same. PartForge uses the Python copy to render `previews/<kit>/skins_runtime_sheet.jpg`.
- Table layout: `runtime.encode` (Python) = `SkinTable.Encode` (C#); the edit-mode test `SkinTableTests` checks they
  agree on the Kit02 manifest.
