# CityForge — research notes

## City layout algorithms (used in `cityforge/layout.py`)

- **Parish & Müller 2001, "Procedural Modeling of Cities"** (L-system roads with global goals): main roads first,
  then streets; road patterns (grid, radial) by area. We use: main roads first (ring, radials, boulevard, perimeter,
  frontage roads), radial pattern round the core.
- **Chen et al. 2008, "Interactive Procedural Street Modeling"** (tensor fields): streets follow a direction field
  (grid in one area, radial round a centre, blended). We use a simple form: each area between main roads gets a
  street grid turned to the local direction (axes in the core, radial outside).
- **Vanegas et al. 2012, "Procedural Generation of Parcels in Urban Modeling"**: blocks cut into lots by oriented
  bounding box splits (or straight skeletons). We use OBB splits until the lots fit the zone.
- Other approaches seen: Voronoi wards (Watabou's medieval city generator), agent-based growth. Not used.

## Blender add-ons and assets (agent research, 4 Nov 2026)

Short version: nothing replaces our own numpy / shapely / bpy pipeline (most city add-ons are Geometry Nodes groups
with a UI panel; headless use is untested). Items marked "unverified" came from memory, not a web check.

| Item | Licence / price | Notes for us |
|---|---|---|
| Downtown Generator (superhivemarket.com/products/downtown-generator) | GPL, built for Blender 5.x | Most current. Try: append its node group headless and see if it evaluates without UI or GPU. Facade reference or modules. |
| The City Generator (blendermarket.com/products/the-city-generator) | Paid ($49 / $99 commercial) | Blender 4.1/4.2, UI driven. Reference only. |
| ICity, Buildify (unverified), Urbaniac | Paid / GPL | UI driven Geometry Nodes. Reference only. |
| Ultimate City Addon, Citigen, SceneCity | Old | Skip. |
| BlenderGIS, Blosm | Free GPL / paid | Real-world OSM data. Not needed (fictional city). |
| Quaternius, Kenney (unverified per pack) | CC0 | Safe props: signs, vents, AC units, street furniture. |
| Poly Haven, ambientCG (unverified) | CC0 | Concrete and grime textures, for a future trim sheet. |
| MK Edge Wear (superhivemarket.com/products/edge-wear-) | Free, GPL-3.0, Blender 5 | Shader only: Cycles previews, not Unity. |
| Kit Ops kpacks, Brunya Cyberpunk City | Paid | Need UI add-ons. Skip for the pipeline. |

Recommended next steps from the research:
1. Our own wear bake: ambient occlusion and curvature from bmesh, height grime and rain streaks into vertex colour or
   a small baked map; the look graded in a Unity URP shader. Headless, free, no licence risk.
2. CC0 textures (Poly Haven / ambientCG) for a trim sheet that grows out of the palette.
3. A short test of Downtown Generator headless.
4. CC0 props (Quaternius / Kenney) for street furniture.
5. Numpy hydraulic / thermal erosion for the terrain.

Licences: use CC0 for outside assets; keep a licence list in the repo; GPL on add-on code does not cover exported
meshes, but read each product's terms; use the commercial tier of paid packs; avoid CC-BY-NC.
