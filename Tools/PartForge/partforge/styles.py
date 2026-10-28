"""
Art direction: one style = one look for the whole kit. Builders read only these values, so a new look is a new entry
here, or a JSON file in Tools/PartForge/styles/ (the Blender panel's "Save style" writes one; it may also carry its own
chassis shapes). See ART_DIRECTION.md for what each value does. Same style + same chassis seed = same model.
"""
import json
import os

STYLE_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "styles")

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
        # Surface detail (Geometry Nodes, chassis only; see detail.py): inset panels, hex nuts, greebles.
        detail=dict(panels=0.35, min_area=0.15, inset=0.84, depth=0.015, recolour=0.4, bolt_size=0.028,
                    greebles=0.12, greeble_size=0.1),
        # Form: rounded section corners, curved silhouettes, filleted edges (see geo.DEFAULT_FORM).
        form=dict(corner_segments=4, loft_smooth=1, curve_detail=1.5, edge_radius=0.05, edge_segments=1,
                  edge_profile=0.5, box_rounding=0.35, box_min=0.1),
        # Skins (skins.py) painted on export: the clean neon look gets the bright ones.
        skins=["factory", "neon_hex", "synthwave", "interceptor"],
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
        detail=dict(panels=0.6, min_area=0.1, inset=0.8, depth=0.025, recolour=0.55, bolt_size=0.038,
                    greebles=0.45, greeble_size=0.14),
        form=dict(corner_segments=3, loft_smooth=1, curve_detail=1.4, edge_radius=0.045, edge_segments=1,
                  edge_profile=0.55, box_rounding=0.3, box_min=0.1),
    ),
}


BUILT_IN = tuple(STYLES)


def _user_styles():
    styles = {}
    if os.path.isdir(STYLE_DIR):
        for file_name in sorted(os.listdir(STYLE_DIR)):
            if file_name.endswith(".json"):
                with open(os.path.join(STYLE_DIR, file_name)) as handle:
                    styles[file_name[:-5]] = json.load(handle)
    return styles


def names():
    return list(BUILT_IN) + [name for name in _user_styles() if name not in BUILT_IN]


def get(name):
    """A copy of the style (built-in or saved JSON). A saved style may hold "chassis": its own chassis specs."""
    every = dict(STYLES, **_user_styles())
    if name not in every:
        raise SystemExit(f"Unknown style '{name}'. Styles: {', '.join(every)}")
    return json.loads(json.dumps(dict(every[name], name=name)))


def save(name, style, chassis=None):
    """Writes Tools/PartForge/styles/<name>.json. Returns the path."""
    os.makedirs(STYLE_DIR, exist_ok=True)
    data = {key: value for key, value in style.items() if key != "name"}
    if chassis is not None:
        data["chassis"] = chassis
    path = os.path.join(STYLE_DIR, f"{name}.json")
    with open(path, "w") as handle:
        json.dump(data, handle, indent=2)
    return path
