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

## Ideas for the next pass

- **Faction styles:** a "corporate security" style (clean white panels, cyan lights, bubble canopy) against a "militia"
  style (more asymmetry, mixed primer panels, scrap plates). Same chassis, different style = different team look.
- **Decals as swatches:** stencil numbers and faction marks would need a small decal texture next to the palette.
- **Damage states:** a style with `wear` 1 and missing plates could be the "heavily damaged" model swap.
- **Silhouette check:** render the line-up with all materials black; each chassis must still read by outline alone.
