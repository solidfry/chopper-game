# MaterialWorks — how it can improve chopper and building materials

Owner, 24 Nov 2026: "MaterialWorks could take our materials on the choppers and buildings to the next level."
Status: assessment only. Nothing is changed yet.

## What it is

- Blender add-on by Blender Bros, version 2.0, GPL-3. Installed in Blender 5.2
  (`%APPDATA%\Blender Foundation\Blender\5.2\scripts\addons\MaterialWorks`). Panel: 3D view → N → MaterialWorks.
- Layered PBR materials: a source PBR set (colour, metallic, roughness, normal) plus wear layers (dents, particles,
  edge wear, rust, scratches), each with a paintable mask. Edge wear from Bevel + AO nodes, triplanar wear, trim
  sheets, decals, wear presets. It has bake support.
- It needs a PBR material library on disk ("BB Mats" works out of the box). Its config folder is empty, so no
  library is set yet.

## The main limit

MaterialWorks makes **Blender shader node trees**. Unity cannot read those (FBX carries no node graph). Every
result must be **baked to textures** before Unity can use it. Our pipelines are already built round bakes, so the
question is where its output goes in.

## Choppers (PartForge): good fit

PartForge already unwraps every part and bakes texture sets (`partforge/texture.py`: `unwrap`, `_bake_material`,
`bake`, `write`) to `Assets/Chopper/Art/Kits/<kit>`: normal, occlusion, emission per set, plus `_SkinMask` and
`_SkinWear` that the runtime skin shader uses for paint, dirt, soot and wear.

Plan:
1. Owner: set the MaterialWorks library (BB Mats or another PBR set). Pick 2-3 base metals (painted steel, bare
   aluminium, carbon / rubber).
2. In `Kit02.blend`, try MaterialWorks by hand on one chassis (Gunship): base metal + edge wear + scratches + dents.
   Render a close-up next to the current skin sheet. Owner judges.
3. If it wins: add an optional bake source in `texture.py` — bake MaterialWorks' **normal detail** (dents,
   scratches) into `_Normal`, and its **edge wear / scratch masks** into the `_SkinWear` channels. The runtime skin
   shader keeps doing the paint, so skins stay data (no per-skin textures, no memory growth).
4. Keep the bake headless-safe: the export runs from the PartForge panel; MaterialWorks must be enabled in that
   Blender. If it is missing, fall back to today's bake.

Cost: same texture count and size as now (about 60 MB per kit). Bake time goes up.

## Buildings (CityForge): limited fit

Buildings use one 5 x 5 **palette atlas** with point filtering (every face's UV is a swatch), vertex-colour wear
masks (`cityforge/wear.py`) and a shader (`BuildingWear.hlsl`) with CC0 detail textures. There are hundreds of
buildings and no unique UVs, so a per-building MaterialWorks bake does not fit (memory, bake time).

Where it can help:
- Author **tileable detail textures** (concrete, panel metal, rust) in MaterialWorks and bake them to 1k colour /
  normal tiles. They replace or add to the CC0 detail set in `Assets/Chopper/Art/ThirdParty/CC0`, which the building
  shader already projects. Small, safe change.
- **Hero props** with their own UVs (team base, nuke, pad turrets, landmark towers): a full MaterialWorks bake.

## Recommended order

1. Chopper test (steps 1-2 above): biggest visual gain, the player sees their chopper all the time.
2. Team base and nuke (Capture the Nuke hero props).
3. Building detail tiles.
