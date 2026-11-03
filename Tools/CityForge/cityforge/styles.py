"""
Art direction for the city: one style = one look for every building of a city kit. Builders read only these values.
Same style + same building seed = same model.

Plan 0 "Level": a rainy cyberpunk city in the desert. Test lesson: a dark ground and too much glow made height hard to
read. So the default style keeps clear height cues: a storey line on every floor, a lit street band at ground level,
red obstruction lights on tall tops, and few lit windows (light is a tool, not decoration).
"""
import json

# City swatch names -> PartForge palette swatches (palette cells never change; the style recolours them).
SWATCH = {
    "wall": "hull",            # main facade concrete
    "trim": "hull_light",      # sills, bands, parapet caps
    "dark": "hull_dark",       # awnings, sign boards, doors, pad decks
    "concrete": "concrete",    # raw concrete (slabs, plinths, chimneys)
    "glass": "glass",
    "lit_warm": "spare",       # lit window, warm (dim)
    "lit_cool": "missile",     # lit window, cool office light (dim)
    "sodium": "sodium",        # street and pad lights (bright amber)
    "warning": "warning",      # red obstruction lights
    "neon_pink": "neon_pink",
    "neon_cyan": "neon_cyan",
    "neon_green": "neon_green",
    "steel": "steel",
    "metal": "gunmetal",
    "rust": "rust",
    "primer": "primer",
    "soot": "soot",
    "tar": "carbon",           # roof membrane
    "rubber": "rubber",
    "hazard": "hazard_faded",
    "bare": "bare",
}

STYLES = {
    # City01: gritty desert dystopia. Sand concrete, sodium light, a little neon.
    "dystopia": dict(
        kit="City01",
        storey=3.6,             # floor to floor (m)
        lit=0.14,               # share of lit windows (0..1)
        lit_cool=0.35,          # share of lit windows with cool office light (the rest warm)
        shop_lit=0.55,          # share of lit shop fronts at street level
        neon=0.6,               # neon signs (0..1: chance per sign spot)
        wear=1.0,               # rust and primer patches, boarded windows on slums (0..1)
        obstruction_height=30.0,  # tops higher than this get red obstruction lights
        props=1.0,              # roof prop density (vents, units, tanks)
        edge_radius=0.0,        # bevel on hard edges (m); 0 = none (seen from 20 m and more)
        budgets=dict(lod0=24000, lod1=0.8, lod2=400),  # LOD0 triangles; LOD1 share of LOD0; LOD2 triangles
        palette={
            # name: (colour, emission or None, metallic, smoothness). Names are PartForge swatches (see SWATCH).
            "hull": ("#8b8273", None, 0.0, 0.18),         # sand concrete
            "hull_light": ("#a9a190", None, 0.0, 0.2),
            "hull_dark": ("#3e3c38", None, 0.15, 0.25),
            "concrete": ("#74726b", None, 0.0, 0.15),
            "glass": ("#202e34", None, 0.3, 0.88),
            "spare": ("#d9b77a", "#8a6a3a", 0.0, 0.5),    # lit warm window (dimmer than the sodium lights)
            "missile": ("#cfe3ea", "#5f8590", 0.0, 0.5),  # lit cool window
            "carbon": ("#262624", None, 0.0, 0.2),        # roof membrane
        },
    ),
}


def names():
    return list(STYLES)


def get(name):
    if name not in STYLES:
        raise SystemExit(f"Unknown style '{name}'. Styles: {', '.join(STYLES)}")
    return json.loads(json.dumps(dict(STYLES[name], name=name)))
