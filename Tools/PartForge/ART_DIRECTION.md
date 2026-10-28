# Art direction (PartForge styles)

A **style** in `partforge/styles.py` is the art direction for a whole kit. The builders never hard-code a look: they read
the style. To change the look, change a style value (or copy a style and give it a new kit name), run the generator,
and check the previews.

## Current direction: "dystopia" (Kit02, the default)

Gritty dystopia, near future. Machines that a broken state keeps flying.

1. **Brutal and angular.** Heavy chamfers and trapezoid sections (faceted, a little stealthy); a low chisel nose;
   flat slab boom. No round, friendly shapes.
2. **Armoured, not glazed.** The crew looks out through slits in an armoured visor, not a glass bubble.
3. **Retrofitted.** Slat (cage) armour on stand-offs, external pipes and cable bundles, one-sided add-ons
   (searchlight, cable reel, ammo boxes, sensor mast). Function over form; it was fixed in the field.
4. **Worn.** Red-oxide primer and rust replacement panels (not mirrored), soot behind the exhausts, bare scuffed
   edges, sun-bleached warning paint. Matte paint, not shiny metal.
5. **Light is a tool.** Sodium amber and red warning lights, a searchlight; no decorative neon strips. Neon stays
   only where the tech glows (pulse laser, EMP, shield).

"neon" (Kit01) is the first look: cleaner shapes, bubble canopy, neon strips. Keep it as a contrast or for a faction.

## Body types (per chassis, `kits.py` → `body`)

Body type is the core shape family; the style is applied on top of it.

- **attack** (Gunship; Assault with options): chunky attack-helicopter shape language (the feel of an Apache, not
  its design). Narrow, tall, flat-sided slab core; stepped tandem cockpit (flat panes or armoured visors); cheek pods;
  engine nacelles at the shoulders with exhaust deflectors; avionics hump; wheels; open tail rotor.
  Options: `snout` = `sensor` (boxy nose sensor, two lenses) or `grille` (beastly snout with a Space Marine-style
  grille: slats, jaw plates, brow ridge, red lenses); `taper` < 1 narrows every section towards its top.
  The **Assault** uses `snout: grille`, `taper: 0.85`.
- **heavy** (Lifter): heavy lifter (CH-53 / "Titan lifter" base) with greebles and cyberpunk details: long boxy cargo
  hull with armour bands, blunt glazed cockpit (or visor), chin sensor and probe, floodlights, lit cabin windows,
  sponsons with dual wheels, big engine pods (glowing intake cores, dot-grid vents, chevrons), top cage, pipes,
  antenna array, dish, belly lift frame with hooks. `rotor_layout`: `single` (tail boom, open tail rotor), `tandem`
  (front and rear towers), `coaxial` (two stacked rotors + pusher prop) or `quad` (four ducted fans on outrigger arms).
  About 3 m wide: noticeably bigger than the others.
- **assault** (option, not used by default): everything built from blocks that taper to the top, grille snout.
- **utility** (Scout): one rounded hull from nose to boom, canopy on top, engines on the roof, skids, ducted tail rotor.

Extra rotor hubs (tandem, coaxial, quad) are sockets `SOCKET_Rotor_1..3`; quad hubs carry a socket scale (0.34) so the
rotor module fits the duct. The game must show the rotor module on every rotor socket at the socket's scale.

## Knobs

| Value | Range | Effect |
|---|---|---|
| `facet` | 0.8–2.0 | Chamfer on every section. Higher = more cut corners, harder silhouette. |
| `top_taper` | 0.5–1.0 | Top width of sections. Lower = trapezoid, stealth-faceted sides. |
| `nose` | `round`, `chisel` | Tapered nose, or a low wide blade with a flat top. |
| `canopy` | `bubble`, `visor` | Glass canopy, or an armoured hood with view slits. |
| `boom` | `tube`, `slab` | Tail boom section. |
| `cage` | 0–1 | Slat armour round the engines and the boom. |
| `conduits` | 0–1 | External pipes and cable bundles. |
| `asymmetry` | 0–1 | Number of one-sided retrofits (picked by the chassis seed). |
| `wear` | 0–1 | Primer/rust panels, soot, bare edges, heavy skids with steps. |
| `armour_layers` | 1–2 | Stacked armour plates. |
| `antennas` | (min, max) | Antenna clutter on the boom; 4+ adds a small dish. |
| `neon_strips` | bool | Long glowing side strips (on = neon look). |
| `lights` | swatches | Nose, right/left position, sensor lights. |
| `hazard` | swatch | Warning paint (`hazard` or `hazard_faded`). |
| `accents` | swaps | Swatch swaps for glowing module parts (e.g. green → sodium). |
| `palette` | overrides | Per swatch: colour, glow, metallic, smoothness. |

Per chassis shape (size, nose length, boom, wings, engines, armour amount, seed) is in `partforge/kits.py`.

## Form (soft shapes)

Owner direction (28 Oct): "most technology isn't basic shapes with hard square edges". Style key `form`
(panel: **Form**) sets how soft every part is:

| Value | Effect |
|---|---|
| `corner_segments` | Arc segments per section corner. 1 = the old straight 45° cut; 3-4 = rounded corners on every hull, pod, nacelle and canopy section. |
| `loft_smooth` | Extra spline-interpolated sections between the authored ones: curved silhouettes instead of kinks. |
| `curve_detail` | More segments on cylinders, ducts and balls. |
| `edge_radius`, `edge_segments`, `edge_profile` | Bevel on the remaining hard edges. 1 segment + hardened normals = a soft-shaded rounded edge at no extra cost; 2-4 = real fillets (many more triangles). |
| `box_rounding`, `box_min` | Boxes thicker than `box_min` get every edge and corner rounded (moulded parts, not bricks); thin slats, vents and bars stay square to save triangles. |

Cost at the current values: about 15k-26k triangles per chassis, 37k for the Lifter (with surface detail).

## Parts on the hull sides

Plates, repair patches, vents, strips, windows, door frames and ladder rungs are laid flat on the real hull: after the
main hull is built, `Builder.snapshot_surface()` keeps it, and `Builder.on_surface(side, y, z, size, ...)` casts a ray
from outside, puts the part on the hit point and turns it to the surface slope. Random parts (plates, patches) skip
spots that are already covered, so they never cut into each other; the door and ladder areas are kept free.

## No free-standing 90° plates

Parts that stick out from the hull are swept and tapered towards the front, not square boxes:

- Wings, stub mounts, sponson stubs and the tail stabiliser: `_swept_wing` (a rounded section that gets narrower and
  moves back towards the tip; wings also droop a little).
- End plates (the yellow wing tips, the stabiliser tips): `_end_plate` (lower at the front, raked leading edge, leaning
  out; `Builder.fin(..., cant=)` gives the lean).
- Slat armour and railings: `_slat_cage` (round bars that lean back, lower towards the front, top rail sloping down to
  the front); the Lifter's top railing also gets narrower towards the front.

## Alignment check

`generate.py --audit` (no export, under a second) checks every part: each primitive is a mesh island; islands that
touch or cut into each other are joined, and any group not joined to the main hull is listed as **floating** (in the
air, or hidden inside the hull). Sockets further than 0.12 m from the hull are listed too. Keep it at 0 problems.

Builders place add-ons with rays, not fixed numbers: `Builder.hull_x` / `hull_y` (where the hull surface is),
`Builder.lay` (a thin part laid flat on a surface from any direction: front slits, belly plates, vents on a slope) and
`on_surface` (side parts). Wing and stub roots, nacelles, engine pods, pipes, clamps, cage stand-offs, retrofits,
antennas and boom bands all find the hull this way, so they stay attached when a shape number changes. Sockets are
seated too: hanging modules hang from the underside above their socket, engine stacks sit on the deck.

## Materials (texture sets)

Export bakes real textures; the palette stays as the swatch ids and the quick look in the panel.

- **UVs:** UV0 = a unique unwrap per chassis (all modules share one atlas); UV1 = the palette cell.
- **Baked once per set:** normal map (fine cast grain and hammered dents; glass and lights stay smooth), occlusion,
  and the surface data the skins use: position and normal in model space, swatch id, edge mask, cavity mask.
- **Per skin:** colour, metallic/smoothness, emission (only skins with glow lines have their own emission map).
- **Wear and grime** come from the bake, on every skin: bare metal on the edges and in chips near them, dirt in the
  crevices, rain streaks, soot plumes behind each exhaust (found from the exhaust faces), a little occlusion.
- Style key `textures`: `density` (normal-map pixels per metre, 110), `max_size` (2048), `module_size` (1024),
  `skin_scale` (0.5: skin maps at half size; small files, the paint is soft-edged anyway), `samples` (16).
- Unity: one URP Lit material per set and skin (base, metallic/smoothness, normal, occlusion, emission).

## Skins

`partforge/skins.py`. A skin is data: a pattern and its colours, a paint finish (metallic, smoothness), optional glow
lines, how much wear and grime show, and whether replacement panels and warning paint take the skin's colours. Only
painted armour takes the skin; glass, lights, bare metal, rubber and weapons keep the kit's look, and the wear and
grime of the bake sit on top, so every skin is the same worn machine.

Patterns are worked out in 3D (model space, metres), not on the UVs: no seams, and the same skin lines up across
parts and chassis.

| Pattern | Look |
|---|---|
| `solid` | One colour (dark and light armour panels keep their contrast). |
| `camo` | Layered blobs, one noise per colour; `cover` = share per colour (holds on every model). |
| `digital` | The same, snapped to square pixels on each face. |
| `splinter` | Angular shards (stretched Voronoi cells). |
| `tiger` | Warped stripes across the hull. |
| `hex` | Hex armour cells with dark seams; with `glow`, some cell outlines glow (cyberpunk). |
| `synthwave` | Deep purple, a striped sunset band along the sides, a glowing grid. |
| `livery` | Twin top stripes, a side band and a dark belly (corporate, police, racing). |

Skins now: Factory (the style's own paint), Woodland, Desert, Arctic Digital, Urban Splinter, Jungle Tiger,
Night City (neon hex), Outrun (synthwave), Interceptor (white corporate livery), Night Stalker (matte black, red).
`previews/<kit>/skins_sheet.jpg` shows them all on the Gunship.

New skin: copy an entry in `SKINS` (new `id`, never reused: player data saves it), or list skins in a style
(`"skins": ["factory", "woodland", {...}]`). Ideas: faction liveries (team colour as the main colour), hazard
chevrons (rescue, riot), rust bucket (no paint, all primer), chrome show skin, damage states (more wear, scorch).

## Surface detail (Geometry Nodes)

Every chassis runs through the **PF Surface Detail** node group after the bevel (`partforge/detail.py`); the result is
baked into the exported mesh, so Unity gets real geometry. Style key `detail` (panel: **Surface detail**):

| Value | Effect |
|---|---|
| `panels` | Share of large hull faces that become inset panels. |
| `min_area` | Faces smaller than this (m²) get no panels or greebles (keeps small parts clean). |
| `inset`, `depth` | Panel size relative to its face; how far it sinks in. |
| `recolour` | Share of panels painted light or dark (two-tone armour). |
| `bolt_size` | Hex nuts at every panel corner, aligned to the surface; 0 = none. |
| `greebles`, `greeble_size` | Small boxes per m² on large hull faces; their largest size. |

Only hull faces get detail (glass, lights and warning paint stay clean). The node group lives in the `.blend`: edit it
in the Geometry Nodes editor and Rebuild uses your version; **Reset detail nodes** makes it again from code. Cost:
about +5k to +10k triangles per chassis at the dystopia values; lower `panels`, `bolt_size` 0 or `greebles` to save.

## Ideas for the next pass

- **Faction styles:** a "corporate security" style (clean white panels, cyan lights, bubble canopy) against a "militia"
  style (more asymmetry, mixed primer panels, scrap plates). Same chassis, different style = different team look.
- **Decals:** stencil numbers and faction marks: a decal pass in the skin composer (the baked position map says
  where the nose and the boom are).
- **Damage states:** a style with `wear` 1 and missing plates could be the "heavily damaged" model swap.
- **Silhouette check:** render the line-up with all materials black; each chassis must still read by outline alone.
