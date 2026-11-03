# CityForge — procedural city buildings (Blender)

Generates the city buildings for the game maps (plan section 7a2). Same idea as PartForge: everything is code +
numbers, so a change is a re-run, not a re-model. CityForge uses PartForge's geometry builder and palette material
(`cityforge/pf.py`), so buildings and choppers share one pipeline and one look.

Status: first version (3 Nov 2026). Built and checked with `bpy` in a cloud session; **not yet imported in Unity**
(no Setup step yet, see "Next").

## Run

Blender as a Python module (no Blender install needed):

    python -m venv Tools/CityForge/.venv && Tools/CityForge/.venv/bin/pip install bpy==5.2.3 numpy   # Python 3.13
    Tools/CityForge/.venv/bin/python Tools/CityForge/generate.py             # style "dystopia" -> City01
    Tools/CityForge/.venv/bin/python Tools/CityForge/generate.py --audit     # building check only (1 to 2 s)

or a normal Blender install (4.2 or newer):

    blender --background --python Tools/CityForge/generate.py -- [options]

Options: `--style NAME`, `--kit NAME` (default: the style's kit), `--out DIR` (default
`Assets/Chopper/Art/City/<kit>`), `--previews DIR` (default `Tools/CityForge/previews/<kit>`), `--only A,B` (some
buildings), `--no-render`, `--no-sheet` (skip the 13 single-building renders), `--no-blend`, `--samples 32`.
Build and export: about 3 s. Previews: about 10 min on 4 CPU cores.

## What it makes

13 buildings in 4 types (`cityforge/specs.py`). The front faces +Z. The origin is the plot centre on the ground.

| Type | Buildings | What |
|---|---|---|
| tower | Tower_Spire, Tower_Pad, Tower_Slab, Tower_Mast | Podium with shop fronts, tiers that step back, ribbon or grid glass facade, crown (spire, landing pad, billboard, antennas). 60 to 115 m. |
| block | Block_Housing, Block_Corner, Block_Court | Rect, L or U plan, 5 to 8 floors, punched windows, balconies, fire escape, water tanks or a pad on the roof. |
| slum | Slum_Stack, Slum_Tower, Slum_Low | Old concrete base with shacks piled on top, overhangs on stilts (found by ray casts), ladders, cables, small neon. |
| industrial | Ind_Hall, Ind_Tanks, Ind_Stacks | Sawtooth hall, office, chimneys (up to 52 m), tanks, pipe runs, floodlights. |

Each building has:
- **3 LODs**: `LOD0` full detail (2 to 7 k triangles), `LOD1` (no small props; windows become one band per floor,
  as dark on average as the windows), `LOD2` (one box per mass, 24 to 160 triangles).
- **Box colliders** (for Unity Physics): main masses, roof huts, tanks, pads, sign boards, pipe runs. Round things
  get 3 boxes (at most 3 % outside the circle).
- **Markers**: `RoofPad` (landing pad: spawns, Lifter objectives) and `Roof` (a clear circle on a flat roof:
  objective zones). Position = the surface point; radius = the clear circle.

Height and ground cues (test lesson: dark ground and too much glow made height hard to read): a storey line on every
floor, a lit band of shop fronts and sodium strips at street level, red obstruction lights on tops above 30 m and on
masts, few lit windows (style `lit` = 14 %).

## Output

| File | What |
|---|---|
| `Models/<Name>.fbx` | `<Name>_LOD0`, `_LOD1`, `_LOD2` meshes (Unity makes the LOD Group from the names) and `MARKER_<Kind>_<n>` empties under LOD0. One material `City01_Kit`. |
| `Textures/City01_Colour.png`, `_Emission.png`, `_MetalSmooth.png` | The palette maps (5 x 5 swatches, point filtering). UV0 = UV1 = the swatch. |
| `City01.manifest.json` | Per building: type, seed, file, plot, bounds, triangles per LOD, colliders (centre, size, yaw = Unity euler Y), markers (kind, position, radius), audit result. Suggested LOD screen heights. |

Previews (`previews/City01`): `district_day.jpg`, `district_night.jpg` (glow balance), `street_day.jpg` and
`street_night.jpg` (chopper height, ground cues), `lods.jpg` (LOD0 / LOD1 / LOD2 side by side),
`buildings_sheet.jpg` (every building alone).

## Audit

`generate.py --audit` (also runs on every build; the result goes into the manifest). It checks:
- floating parts (every piece must touch the ground through other pieces);
- nothing below the ground; colliders inside the plot; the mesh hangs over the street by 2.2 m at most;
- no invisible walls (a collider outside the mesh);
- no solid part more than 2.6 m from every collider (thin masts and spires are allowed);
- LOD2 has the collider height (no skyline pop); triangle budgets (style `budgets`);
- nothing inside the clear circle of a `RoofPad` or `Roof` marker.

## Change things

- Look: `cityforge/styles.py` (palette colours, lit share, neon, props, obstruction height, budgets).
- Buildings: `cityforge/specs.py` (one line each: type, seed, plot and shape numbers). New seed = new chance details.
- Parts: `cityforge/kit.py` (facades, shop fronts, roof props, pads, signs, lights), `cityforge/buildings.py` (the
  four types).

## Next

- Unity: a Setup step that imports `Assets/Chopper/Art/City/<kit>` (palette material with point filtering, LOD
  screen heights, box colliders and markers from the manifest, one prefab per building) and a validator check.
- Blender panel (N > CityForge) with Rebuild and sliders, like PartForge.
- Wear and grime: the PartForge bake and runtime skin path (texture sets per building).
- A district / map layout generator (streets, plots, spawn and objective placement from the markers).
