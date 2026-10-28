"""
Art direction: one style = one look for the whole kit. Builders read only these values, so a new look is a new entry
here (see ART_DIRECTION.md for what each value does). Same style + same chassis seed = same model.
"""

STYLES = {
    # Kit01: the first look. Clean military shapes, neon strips, bubble canopy.
    "neon": dict(
        kit="Kit01",
        facet=1.0,            # chamfer multiplier on every section (higher = more cut corners)
        top_taper=1.0,        # top width multiplier (lower = trapezoid, stealthy sides)
        nose="round",         # round | chisel
        canopy="bubble",      # bubble | visor
        boom="tube",          # tube | slab
        cage=0.0,             # slat armour round engines and boom (0..1)
        conduits=0.0,         # external pipes and cables along the body (0..1)
        asymmetry=0.0,        # one-sided retrofits: searchlight, reel, ammo box (0..1)
        wear=0.0,             # primer / rust replacement panels, soot behind exhausts (0..1)
        armour_layers=1,      # 2 = plates stacked in two layers
        antennas=(1, 3),      # min, max on the boom
        neon_strips=True,     # long glowing strips on the sides
        lights={"nose": "neon_cyan", "right": "neon_green", "left": "neon_pink", "sensor": "neon_cyan"},
        hazard="hazard",
        accents={},           # swatch swaps for module glow parts
        palette={},           # swatch overrides: name: (colour, emission or None[, metallic, smoothness])
    ),
    # Kit02: gritty dystopia / near future. Brutal, angular, retrofitted, worn; light is a tool, not decoration.
    "dystopia": dict(
        kit="Kit02",
        facet=1.6,
        top_taper=0.72,
        nose="chisel",
        canopy="visor",
        boom="slab",
        cage=1.0,
        conduits=1.0,
        asymmetry=1.0,
        wear=1.0,
        armour_layers=2,
        antennas=(3, 5),
        neon_strips=False,
        lights={"nose": "sodium", "right": "warning", "left": "sodium", "sensor": "warning"},
        hazard="hazard_faded",
        accents={"neon_green": "sodium", "neon_pink": "warning"},
        palette={
            # Matte painted armour (low metal, low smoothness): faceted sides read as flat slabs, not mirrors.
            "hull": ("#585c55", None, 0.12, 0.28),        # concrete olive grey, desaturated
            "hull_dark": ("#2d2f2c", None, 0.15, 0.25),
            "hull_light": ("#77786f", None, 0.1, 0.3),
            "glass": ("#1a2422", None),
            "carbon": ("#1c1c1b", None),
            "missile": ("#a7a89f", None),
        },
    ),
}


def get(name):
    if name not in STYLES:
        raise SystemExit(f"Unknown style '{name}'. Styles: {', '.join(STYLES)}")
    return dict(STYLES[name], name=name)
