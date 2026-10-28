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
| **Export kit to Unity** | FBX, palette PNGs and manifest to `Assets/Chopper/Art/Kits/<kit>`. Then Unity: Chopper → Setup → 5. |
| **Render previews** | Line-up, close-ups and module sheet to `previews/<kit>` (Blender is busy for about a minute). |
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

Options: `--style dystopia|neon` (art direction, see `ART_DIRECTION.md`), `--kit NAME` (default: the style's kit),
`--out DIR` (default `Assets/Chopper/Art/Kits/<kit>`), `--previews DIR` (default `Tools/PartForge/previews/<kit>`),
`--no-render`, `--samples 32`. A full run with previews takes about 1–2 min on CPU.

A command-line run also saves `blend/<kit>.blend` (`--no-blend` to skip).
Then in Unity: **Chopper → Setup → 5. Import part kits** (or Do all): texture settings, the kit material, material remap.

## Output

| File | What |
|---|---|
| `Models/Chassis_<Family>.fbx` | Scout, Lifter (utility body); Gunship, Assault (attack body). Origin = centre of mass. Child empties `SOCKET_<Slot>_<Index>` = attach points (same slot order and indices as the game's chassis definitions; Pylon 0 on the right). |
| `Models/<Module>.fbx` | Guns, pods, racks, utilities, turret, winch, rotors, engines. Origin = attach point, points +Z. |
| `Textures/<kit>_Colour/Emission/MetalSmooth.png` | 5×5 palette. Each face samples one swatch: one material per kit; a skin = new maps. |
| `<kit>.manifest.json` | Parts, files, slot, module ids, triangle counts, sockets in Unity space. |

## Conventions

- Built in Unity space (x right, y up, z forward, metres); `geo.to_blender` maps to Blender, the FBX export maps back.
- Socket rotation is identity: modules point +Z. (The old PolygonMilitary placeholder art needed yaw on the sockets; kit
  chassis do not.)
- Look: `partforge/styles.py` (art direction, see `ART_DIRECTION.md`). Shapes: `partforge/kits.py` (chassis numbers +
  seed), `partforge/parts.py` (builders), `partforge/palette.py` (swatches).
- Armour modules have no model (stats only, plates are on the chassis).

## Previews

`previews/<kit>/chassis_lineup.jpg`, `gunship_closeup.jpg`, `modules_sheet.jpg` (Cycles, preview loadouts in `kits.py`).
