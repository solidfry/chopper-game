# PartForge — procedural chopper parts (Blender)

Generates the base chassis and module models for the modular chopper (plan section 5, "Part kits and authoring
pipeline"). Hard-surface, faceted, gritty military look with neon accents from the style guide. Everything is
code + numbers, so a change is a re-run, not a re-model.

## Run

Either Blender as a Python module (no Blender install needed):

    python -m venv .venv && .venv/bin/pip install bpy==5.2.3     # Python 3.13
    .venv/bin/python Tools/PartForge/generate.py

or a normal Blender install:

    blender --background --python Tools/PartForge/generate.py -- [options]

Options: `--kit Kit01` (name), `--out DIR` (default `Assets/Chopper/Art/Kits/<kit>`), `--previews DIR`
(default `Tools/PartForge/previews`), `--no-render`, `--samples 32`. A full run with previews takes about 1–2 min on CPU.

Then in Unity: **Chopper → Setup → 5. Import part kits** (or Do all): texture settings, the kit material, material remap.

## Output

| File | What |
|---|---|
| `Models/Chassis_<Family>.fbx` | Scout, Gunship, Assault, Lifter. Origin = centre of mass. Child empties `SOCKET_<Slot>_<Index>` = attach points (same slot order and indices as the game's chassis definitions; Pylon 0 on the right). |
| `Models/<Module>.fbx` | Guns, pods, racks, utilities, turret, winch, rotors, engines. Origin = attach point, points +Z. |
| `Textures/<kit>_Colour/Emission/MetalSmooth.png` | 4×4 palette. Each face samples one swatch: one material per kit; a skin = new maps. |
| `<kit>.manifest.json` | Parts, files, slot, module ids, triangle counts, sockets in Unity space. |

## Conventions

- Built in Unity space (x right, y up, z forward, metres); `geo.to_blender` maps to Blender, the FBX export maps back.
- Socket rotation is identity: modules point +Z. (The old PolygonMilitary placeholder art needed yaw on the sockets; kit
  chassis do not.)
- Shapes: `partforge/kits.py` (chassis numbers + seed), `partforge/parts.py` (builders), `partforge/palette.py` (swatches).
- Armour modules have no model (stats only, plates are on the chassis).

## Previews

`previews/chassis_lineup.jpg`, `gunship_closeup.jpg`, `modules_sheet.jpg` (Cycles, preview loadouts in `kits.py`).
