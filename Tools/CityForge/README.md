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
buildings), `--no-render`, `--no-sheet` (skip the single-building renders), `--no-blend`, `--samples 32`.
Build and export: about 3 s. Previews: about 15 min on 4 CPU cores.

## What it makes

20 buildings (`cityforge/specs.py`). The front faces +Z. The origin is the plot centre on the ground.

| Type | Buildings | What |
|---|---|---|
| mega | Mega_Arcology, Mega_Needle, Mega_Slab | The cyberpunk skyline: 300 to 550 m (Arcology: 471 m roof, about 550 m spire). Setback tiers, sky-lobby light rings every 16 to 20 floors (they count the height), neon corner strips, giant billboards, landing pads that stand out from the walls high up, tall spire or antenna crown, red lights every 90 m. 8 to 11 k triangles. |
| twins | Mega_Twins | Two megatowers (about 330 and 400 m) on one plot, joined by sky bridges: two enclosed glass bridges (96 m, 180 m) and an open sky deck with a landing pad (262 m). Choppers can fly under and between them. |
| landmarks | Landmark_Twist, Landmark_Halo, Landmark_Orb | High tech, unusual shapes. Twist: 30 stacks of floors turned 90° from bottom to top (about 335 m). Halo: round glass tower (295 m) with two giant ring walkways on spokes, pad on the round roof. Orb: grid tower with a wide round deck and a faceted orb set into it (about 225 m), pads on the deck. |
| tower | Tower_Spire, Tower_Pad, Tower_Slab, Tower_Mast | Podium with shop fronts, tiers that step back, ribbon or grid glass facade, crown (spire, landing pad, billboard, antennas). 60 to 115 m. |
| block | Block_Housing, Block_Corner, Block_Court | Rect, L or U plan, 5 to 8 floors, punched windows, balconies, fire escape, water tanks or a pad on the roof. |
| slum | Slum_Stack, Slum_Tower, Slum_Low | Old concrete base with shacks piled on top, overhangs on stilts (found by ray casts), ladders, cables, small neon. |
| industrial | Ind_Hall, Ind_Tanks, Ind_Stacks | Sawtooth hall, office, chimneys (up to 52 m), tanks, pipe runs, floodlights. |

Each building has:
- **3 LODs**: `LOD0` full detail (2 to 7 k triangles), `LOD1` (no small props; windows become one band per floor,
  as dark on average as the windows), `LOD2` (one box per mass, 24 to 160 triangles).
- **Box colliders** (for Unity Physics): main masses, roof huts, tanks, pads, sign boards, pipe runs. Round things
  get 3 boxes (at most 3 % outside the circle).
- **Markers**: `RoofPad` (landing pad on a roof or high on a megatower wall: spawns, Lifter objectives) and `Roof` (a clear circle on a flat roof:
  objective zones). Position = the surface point; radius = the clear circle.

Height and ground cues (test lesson: dark ground and too much glow made height hard to read): a storey line on every
floor, a lit band of shop fronts and sodium strips at street level, red obstruction lights on tops above 30 m and on
masts, few lit windows (style `lit` = 14 %).

## Terrain

`cityforge/terrain.py` (on by default; `--no-terrain` skips it). A 3 km desert basin round the district (numpy
height field, 6 m grid, style key `terrain` overrides `terrain.DEFAULTS`):
- the city stands on an exactly flat concrete floor (y = 0); a 84 m street gap is left in the layout for the channel;
- a **storm-drain channel** (56 m wide, 8 m deep concrete trapezoid, wet strip in the middle) runs through the city
  and out into the canyon: low, fast routes under street level;
- a **canyon** (72 to 110 m wide, floor falling to -40 m) winds east from the basin out through the rim;
- 14 **mesas** on a ring 0.8 to 1.2 km out: flat tops (MesaTop markers: landing, objectives), terraced sandstone
  cliffs, talus aprons; a ragged **plateau rim** round the basin.

Export: `Terrain/Terrain_<row>_<col>.fbx` (64 chunks of 384 m, LOD0..2 each; edge chunks half the detail; 10 m skirts
so LOD seams show no cracks; mesh collider = LOD0), `Terrain/City01_Height.raw` (16-bit height map, 513 x 513, for a
Unity Terrain if wanted) and `City01.terrain.json` (chunks, height range, MesaTop markers, canyon and channel paths for
bot routes, the building layout on the terrain, suggested flight bounds, audit). About 14 MB.

Terrain audit: city floor flat, no plot touches the channel, every plot on flat ground, the canyon at least 50 m wide
30 m above its floor all the way, every mesa top flat within its marker circle.

## Output

| File | What |
|---|---|
| `Models/<Name>.fbx` | `<Name>_LOD0`, `_LOD1`, `_LOD2` meshes (Unity makes the LOD Group from the names) and `MARKER_<Kind>_<n>` empties under LOD0. One material `City01_Kit`. |
| `Textures/City01_Colour.png`, `_Emission.png`, `_MetalSmooth.png` | The palette maps (5 x 5 swatches, point filtering). UV0 = UV1 = the swatch. |
| `City01.manifest.json` | Per building: type, seed, file, plot, bounds, triangles per LOD, colliders (centre, size, yaw = Unity euler Y), markers (kind, position, radius), audit result. Suggested LOD screen heights. |

Previews (`previews/City01`), with the game's Kit02 choppers for scale (`cityforge/choppers.py`, preview only):
`scale_pad_*.jpg` (a Gunship on a megatower wall pad), `scale_flyby_*.jpg` (a Scout flying past, seen from below),
`skyline_*.jpg` (the megatowers from chopper height), `bridge_*.jpg` (a Gunship under a sky bridge),
`landmarks_*.jpg`, `terrain_overview_*.jpg`, `canyon_*.jpg` (a Gunship in the canyon), `channel_*.jpg` (a Scout in
the storm drain), `mesa_*.jpg` (a Lifter on a mesa top), `district_day.jpg`, `district_night.jpg` (glow balance), `street_day.jpg` and
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
- Parts: `cityforge/kit.py` (facades, shop fronts, roof props, pads, signs, lights; `Building.turned()` turns a group
  of parts, colliders and markers round a vertical axis), `cityforge/buildings.py` (the building types).

## Next

- Unity: a Setup step that imports `Assets/Chopper/Art/City/<kit>` (palette material with point filtering, LOD
  screen heights, box colliders and markers from the manifest, one prefab per building) and a validator check.
- Blender panel (N > CityForge) with Rebuild and sliders, like PartForge.
- Wear and grime: the PartForge bake and runtime skin path (texture sets per building).
- A district / map layout generator (streets, plots, spawn and objective placement from the markers). The terrain
  already places the district (`layout` in the terrain manifest); bridges over the channel are still missing.
