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
    "rock_dark": "soot",       # terrain: dark strata (fallback only: the terrain shader colours the ground)
    "flame": "exhaust",        # flare flames (glow, flickers in the building shader)
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
            "exhaust": ("#ffb347", "#ff7a1e", 0.0, 0.5),  # flare flame
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
            "exhaust": ("#ffb347", "#ff7a1e", 0.0, 0.5),  # flare flame
        },
        layout=dict(
            unique=("Mega_Slab", "Mega_Needle", "Landmark_Orb"),
            variants={"tower": 4, "block": 4, "slum": 4, "industrial": 12},
            # Dense: a bigger city on the crater floor, up to 10 buildings per lot, 4 m between them.
            city=(1150.0, 920.0), fill=40, gap=3.0, pack=True,
            # Refinery plots are big: wider street spacing and bigger lots outside the core.
            spacing=(118.0, 140.0, 150.0, 120.0),
            zones=[(0.35, 3600.0, (("tower", 0.75), ("block", 0.25))),
                   (0.62, 4200.0, (("industrial", 0.6), ("block", 0.25), ("tower", 0.15))),
                   (0.92, 4800.0, (("industrial", 0.85), ("slum", 0.1), ("block", 0.05))),
                   (9.0, 3600.0, (("industrial", 0.6), ("slum", 0.4)))],
        ),
        terrain=dict(
            seed=41, rise=10.0, dunes=2.5,
            # Crater wall: a steep inner cliff, a second step, a high jagged crest.
            plateau=dict(radius=1120.0, width=45.0, height=230.0, warp=90.0, wall=150.0, wall_at=130.0, crest=75.0),
            # Old cinder cones on the crater floor: flat tops (landing), steep terraced sides.
            mesas=dict(shape="cone", count=6, ring=(920.0, 1040.0), radius=(70.0, 130.0), height=(50.0, 115.0),
                       cliff=10.0, talus=30.0, top=0.38),
            strata=7.0,
            # The breach: where lava once broke out through the crater wall.
            canyon=dict(width=(120.0, 170.0), wall=30.0, floor=(-6.0, -45.0), wiggle=50.0, start=40.0),
            # Lava: a moat round the city that drains into the breach, and streams down from the crater wall.
            lava=dict(moat=(745.0, 635.0), streams=4, width=(14.0, 26.0), depth=2.5),
            backdrop=dict(rings=64, segments=320, edge_smooth=260.0),
        ),
        # Slum town dug into the crater wall (shanties.py).
        # Dense and tall (5 Nov 2026, owner): clusters up to 16 levels, up to 210 m. Spots every 1.6 degrees (was 1.2;
        # 12 Nov 2026, build size).
        shanties=dict(step=1.6, chance=0.8, levels=(4, 16), height=(5.0, 210.0), start=0.6),
    ),
    # City05 (working name): a mining town in a high alpine valley. Ranges of eroded granite peaks with snow above
    # about 380 m, a broad valley out to the east, a few rock benches with flat tops. Plan 6c terrain, 13 Nov 2026.
    "alpine": dict(
        kit="City05",
        biome="alpine",
        storey=3.6, lit=0.14, lit_cool=0.4, shop_lit=0.5, neon=0.4, wear=0.8, obstruction_height=30.0, props=1.0,
        edge_radius=0.0,
        budgets=dict(lod0=24000, lod1=0.8, lod2=400),
        palette={
            "hull": ("#7c7f80", None, 0.0, 0.18),         # granite-grey concrete
            "hull_light": ("#9a9c9b", None, 0.0, 0.2),
            "hull_dark": ("#383a3c", None, 0.15, 0.25),
            "concrete": ("#6c6e6f", None, 0.0, 0.15),
            "glass": ("#1f2c33", None, 0.3, 0.88),
            "spare": ("#d9b77a", "#8a6a3a", 0.0, 0.5),
            "missile": ("#cfe3ea", "#5f8590", 0.0, 0.5),
            "carbon": ("#262624", None, 0.0, 0.2),
            "hazard": ("#6c7052", None, 0.0, 0.12),       # tundra (terrain fallback)
            "warhead": ("#6a6c6e", None, 0.0, 0.15),      # granite (terrain fallback)
            "exhaust": ("#ffb347", "#ff7a1e", 0.0, 0.5),
        },
        layout=dict(city=(760.0, 600.0), variants={"tower": 4, "block": 6, "slum": 3, "industrial": 6}),
        terrain=dict(
            shape="alpine", seed=23, step=3.0, rise=14.0, dunes=2.0, strata=0.0, gullies=(),
            mesas=dict(count=4, ring=(560.0, 760.0), radius=(50.0, 80.0), height=(40.0, 80.0), cliff=12.0, talus=40.0),
            # The valley out: a broad trough to the east, the floor falling gently.
            canyon=dict(width=(160.0, 230.0), wall=90.0, floor=(-4.0, -30.0), wiggle=140.0, start=170.0),
            alpine=dict(height=(520.0, 1100.0), cell=1300.0, warp=420.0, start=110.0, ramp=620.0, sharp=1.5,
                        snow=(420.0, 70.0), foothills=55.0),
            erosion=dict(),
            mesh=dict(budget=380000, inner=(1, 2, 4), outer=(2, 4, 8)),
            backdrop=dict(rings=56, segments=256, edge_smooth=300.0),
        ),
    ),
    # City06 (working name): an outpost town on a high plateau beside a deep ravine. The ravine winds across the map
    # (150 to 240 m deep, stepped walls, a river floor), side gullies cut into the plateau, the storm drain falls into
    # it. Plan 6c terrain, 13 Nov 2026.
    "ravine": dict(
        kit="City06",
        biome="rain_desert",
        storey=3.6, lit=0.14, lit_cool=0.35, shop_lit=0.55, neon=0.6, wear=1.0, obstruction_height=30.0, props=1.0,
        edge_radius=0.0,
        budgets=dict(lod0=24000, lod1=0.8, lod2=400),
        palette={
            "hull": ("#857b6c", None, 0.0, 0.18),
            "hull_light": ("#a2998a", None, 0.0, 0.2),
            "hull_dark": ("#3c3a36", None, 0.15, 0.25),
            "concrete": ("#6e6c66", None, 0.0, 0.15),
            "glass": ("#202e34", None, 0.3, 0.88),
            "spare": ("#d9b77a", "#8a6a3a", 0.0, 0.5),
            "missile": ("#cfe3ea", "#5f8590", 0.0, 0.5),
            "carbon": ("#262624", None, 0.0, 0.2),
            "hazard": ("#8f7c60", None, 0.0, 0.12),
            "warhead": ("#6e5141", None, 0.0, 0.15),
            "exhaust": ("#ffb347", "#ff7a1e", 0.0, 0.5),
        },
        layout=dict(city=(700.0, 560.0), variants={"tower": 4, "block": 6, "slum": 6, "industrial": 4}),
        terrain=dict(
            shape="ravine", seed=57, step=3.0, rise=24.0, dunes=7.0, strata=9.0, gullies=(),
            # A low rim far out (the plateau edge), a few mesas on the plateau.
            # No rim: the plateau runs out to the map edge (the backdrop carries on round it).
            plateau=dict(radius=1300.0, width=90.0, height=0.0, warp=160.0, wall=0.0, wall_at=150.0, crest=0.0),
            mesas=dict(count=5, ring=(520.0, 900.0), radius=(45.0, 90.0), height=(35.0, 70.0), cliff=10.0, talus=40.0),
            canyon=dict(width=(200.0, 420.0), wall=24.0, floor=(-8.0, -40.0), wiggle=0.0, start=40.0),
            ravine=dict(depth=(150.0, 240.0), floor=(46.0, 72.0), wall=(85.0, 150.0), benches=3, wiggle=70.0,
                        branches=(10, (160.0, 420.0), 0.75)),
            erosion=dict(),
            mesh=dict(budget=380000, inner=(1, 2, 4), outer=(2, 4, 8)),
            backdrop=dict(rings=56, segments=256, edge_smooth=300.0),
        ),
    ),
}


def names():
    return list(STYLES)


def get(name):
    if name not in STYLES:
        raise SystemExit(f"Unknown style '{name}'. Styles: {', '.join(STYLES)}")
    return json.loads(json.dumps(dict(STYLES[name], name=name)))
