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
    "sand": "hazard",          # terrain: desert sand (the city never uses the bright hazard paint)
    "rock": "warhead",         # terrain: red sandstone
    "rock_dark": "exhaust",    # terrain: dark strata
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
            "hazard": ("#b39b78", None, 0.0, 0.12),       # sand (terrain)
            "warhead": ("#8e6a52", None, 0.0, 0.15),      # sandstone (terrain)
            "exhaust": ("#5b4536", None, 0.0, 0.15),      # dark strata (terrain); no glow
        },
    ),
    # City04: a refinery town on the floor of a volcanic crater. Ash-grey concrete, sodium light, lava glow below the
    # crater walls; a few corporate towers in the core, refinery and cooling towers round it.
    "crater": dict(
        kit="City04",
        biome="volcanic",
        storey=3.6, lit=0.12, lit_cool=0.3, shop_lit=0.5, neon=0.45, wear=1.0, obstruction_height=30.0, props=1.0,
        edge_radius=0.0,
        budgets=dict(lod0=24000, lod1=0.8, lod2=400),
        palette={
            "hull": ("#6f6b66", None, 0.0, 0.18),         # ash concrete
            "hull_light": ("#8c8882", None, 0.0, 0.2),
            "hull_dark": ("#33312f", None, 0.15, 0.25),
            "concrete": ("#615e5a", None, 0.0, 0.15),
            "glass": ("#1d2a30", None, 0.3, 0.88),
            "spare": ("#d9b77a", "#8a6a3a", 0.0, 0.5),
            "missile": ("#cfe3ea", "#5f8590", 0.0, 0.5),
            "carbon": ("#232220", None, 0.0, 0.2),
            "hazard": ("#4a4542", None, 0.0, 0.12),       # ash (terrain fallback)
            "warhead": ("#3a2f2a", None, 0.0, 0.15),      # basalt (terrain fallback)
            "exhaust": ("#24201e", None, 0.0, 0.15),      # dark basalt (terrain fallback)
        },
        layout=dict(
            unique=("Mega_Slab", "Mega_Needle", "Landmark_Orb"),
            variants={"tower": 4, "block": 4, "slum": 4, "industrial": 9},
            # Refinery plots are big: wider street spacing and bigger lots outside the core.
            spacing=(118.0, 140.0, 150.0, 120.0),
            zones=[(0.35, 3600.0, (("tower", 0.75), ("block", 0.25))),
                   (0.62, 5600.0, (("industrial", 0.6), ("block", 0.25), ("tower", 0.15))),
                   (0.92, 6500.0, (("industrial", 0.85), ("slum", 0.1), ("block", 0.05))),
                   (9.0, 4200.0, (("industrial", 0.6), ("slum", 0.4)))],
        ),
        terrain=dict(
            seed=41, rise=10.0, dunes=2.5,
            # Crater wall: a steep inner cliff, a second step, a high jagged crest.
            plateau=dict(radius=1120.0, width=45.0, height=230.0, warp=90.0, wall=150.0, wall_at=130.0, crest=75.0),
            # Old cinder cones on the crater floor: flat tops (landing), steep terraced sides.
            mesas=dict(shape="cone", count=7, ring=(860.0, 1010.0), radius=(70.0, 130.0), height=(50.0, 115.0),
                       cliff=10.0, talus=30.0, top=0.38),
            strata=7.0,
            # The breach: where lava once broke out through the crater wall.
            canyon=dict(width=(120.0, 170.0), wall=30.0, floor=(-6.0, -45.0), wiggle=50.0, start=40.0),
            # Lava: a moat round the city that drains into the breach, and streams down from the crater wall.
            lava=dict(moat=(660.0, 560.0), streams=4, width=(14.0, 26.0), depth=2.5),
            backdrop=dict(rings=64, segments=320, edge_smooth=260.0),
        ),
    ),
}


def names():
    return list(STYLES)


def get(name):
    if name not in STYLES:
        raise SystemExit(f"Unknown style '{name}'. Styles: {', '.join(STYLES)}")
    return json.loads(json.dumps(dict(STYLES[name], name=name)))
