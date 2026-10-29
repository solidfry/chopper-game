# PartForge — procedural chopper parts (Blender)

Generates the base chassis and module models for the modular chopper (plan section 5, "Part kits and authoring
pipeline"). Hard-surface, faceted, gritty military look with neon accents from the style guide. Everything is
code + numbers, so a change is a re-run, not a re-model.

## Tweak in Blender (panel)

Blender 5.x (the files are saved with 5.2; the script itself needs 4.2 or newer).

1. Open `Tools/PartForge/blend/Kit02.blend` (dystopia) or `Kit01.blend` (neon).
2. Blender asks to allow the file's scripts: **Allow Execution** (or Scripting tab → run `partforge_panel.py` once).
3. 3D view → **N** → **PartForge** tab. Press **Z → Material Preview** to see paint and glow.

Or start from the command line with the panel open: `blender --python Tools/PartForge/generate.py [-- --style neon]`.

| Panel | What it does |
|---|---|
| Style + **Load** | Puts a style's values (built-in or saved) into the sliders and rebuilds. |
| **Rebuild** / **Auto rebuild** | Builds every part again in this file (under a second). Auto: after each slider change. |
| Shape, Add-ons, Wear and lights, Paint | Every style value (see `ART_DIRECTION.md`). |
| Chassis | Pick a chassis: body type, size, nose and boom length, wings, engines, armour, seed (**New seed** = new chance details). |
| **Export kit to Unity** | Bakes the texture sets, paints every skin with the runtime shader's maths, then FBX, textures and manifest to `Assets/Chopper/Art/Kits/<kit>` (a few minutes). Then Unity: Chopper → Setup → 5. After it the file shows the textures. |
| Skin + **Show skin** | After an export: puts that skin on every part (viewport in Material Preview or Rendered). |
| **Render previews** | Line-up, close-ups, four-view sheets, module sheet, and (after an export) the skin sheet, to `previews/<kit>`. |
| **Save style** | Writes `styles/<name>.json` (all values + chassis shapes). It shows in the Style menu and works on the command line: `--style <name>`. |

Your own objects in the file are safe: a rebuild replaces only the `PartForge:` collections. Hand edits to generated
parts are lost on the next rebuild; change the numbers instead (or copy a part out of the collection first).

## Run

Either Blender as a Python module (no Blender install needed):

    python -m venv .venv && .venv/bin/pip install bpy==5.2.3     # Python 3.13
    .venv/bin/python Tools/PartForge/generate.py                      # style "dystopia" -> Kit02
    .venv/bin/python Tools/PartForge/generate.py --style neon         # Kit01

or a normal Blender install:

    blender --background --python Tools/PartForge/generate.py -- [options]

Alignment check only (under a second): `generate.py --audit [--style NAME]` lists floating parts and sockets off the
hull (see `ART_DIRECTION.md`, "Alignment check").

Options: `--style dystopia|neon` (art direction, see `ART_DIRECTION.md`), `--kit NAME` (default: the style's kit),
`--out DIR` (default `Assets/Chopper/Art/Kits/<kit>`), `--previews DIR` (default `Tools/PartForge/previews/<kit>`),
`--no-render`, `--samples 32`. Export (bake + 10 skins) about 2.5 min, previews about 12 min more.

Texture files: about 13 MB per kit, whatever the number of skins (skins are data: a row in the skin table).

A command-line run also saves `blend/<kit>.blend` (`--no-blend` to skip).
Then in Unity: **Chopper → Setup → 5. Import part kits** (or Do all): texture settings, the skin-shader materials, skin table, material remap. **Open skin preview scene** shows every chassis with its modules.

## Output

| File | What |
|---|---|
| `Models/Chassis_<Family>.fbx` | Scout, Lifter (utility body); Gunship, Assault (attack body). Origin = centre of mass. Child empties `SOCKET_<Slot>_<Index>` = attach points (same slot order and indices as the game's chassis definitions; Pylon 0 on the right). |
| `Models/<Module>.fbx` | Guns, pods, racks, utilities, turret, winch, rotors, engines. Origin = attach point, points +Z. |
| `Textures/<kit>_<Set>_Normal.png`, `_Occlusion.png`, `_Emission.png` | Per texture set (each chassis, and `Modules` for all modules): normal map, occlusion, lights. |
| `Textures/<kit>_<Set>_<skin>_Colour.png`, `_MetalSmooth.png` | The first skin only (the skin shader's base): paint, metallic (R) / smoothness (A). Style `"baked_skins": true` writes every skin (and glow `_Emission.png`). |
| `Textures/<kit>_<Set>_SkinMask.png`, `_SkinWear.png` | Per set, for the runtime skin shader (`Assets/Chopper/Art/Shaders`): paint areas and tone; dirt, soot, blotches, wear threshold. |
| `<kit>.manifest.json` | Parts (file, slot, module ids, triangles, texture set, sockets in Unity space), texture sets, skins (id, name, maps per set). |

## Conventions

- Built in Unity space (x right, y up, z forward, metres); `geo.to_blender` maps to Blender, the FBX export maps back.
- Socket rotation is identity: modules point +Z. (The old PolygonMilitary placeholder art needed yaw on the sockets; kit
  chassis do not.)
- Look: `partforge/styles.py` (art direction, see `ART_DIRECTION.md`). Shapes: `partforge/kits.py` (chassis numbers +
  seed), `partforge/parts.py` (builders), `partforge/palette.py` (swatches).
- Armour modules have no model (stats only, plates are on the chassis).

## Previews

`previews/<kit>/chassis_lineup.jpg`, `<chassis>_closeup.jpg`, `<chassis>_views.jpg` (front left, rear right, side,
top), `modules_sheet.jpg`, `skins_sheet.jpg` (every skin on the Gunship, with the runtime skin shader's maths; its maps
go to `Tools/PartForge/cache`, not in git; `skins_baked_sheet.jpg` too with `baked_skins`). Cycles, in a grey review studio (soft key,
fill, rim, a floor with a 1 m grid that fades into the horizon); preview loadouts in `kits.py`.
