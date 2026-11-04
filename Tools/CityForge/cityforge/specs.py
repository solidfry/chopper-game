"""
Building specs: the numbers that shape each building. Change a value or the seed and run the generator again.

Common keys: name, type (tower | block | slum | industrial), seed, plot (width x, depth z in metres: the lot the
building must fit in; the origin is the plot centre on the ground). The front (shop fronts, signs) faces +Z.

tower:      podium (ground floor height), tiers [(width share, depth share, floors, x offset share, z offset share)],
            facade (ribbon | grid), crown (pad | antennas | sign | spire)
mega:       podium, tiers [(width share, depth share, floors, x offset, z offset, ribbon | grid)], crown (spire |
            antennas | pad), lobby_every (floors between sky-lobby rings), neon (tier indices with neon corners),
            billboards [(tier, side)], pads [(tier, side)] (landing pads that stand out from the wall)
twins:      gap (m between the towers), towers [([(floors, facade), ...], crown), ...] (left, right; each tier is 14 %
            smaller), bridges [(glass | deck, height, width)], podium, lobby_every, neon
twist:      size, podium, stacks, floors (per stack), turn (degrees, bottom to top), taper (0..1)
halo:       radius, podium, floors, rings [(height, radius)]
orb:        size, podium, floors, deck (radius), orb (radius), pads (angles)
block:      floors, shape (rect | L | U), roof (tanks | pad), fire_escape (left | right | None), balconies (0..1)
slum:       base_floors, stacks (shacks to pile on the base), spread (overhang, m)
industrial: hall (w, d, h, x, z) or None, chimneys [(x, z, radius, height)], tanks [(x, z, radius, height)],
            office (w, d, floors, x, z) or None (x, z = centre); refinery parts: columns [(x, z, radius, height)],
            cooling [(x, z, radius, height)] (cooling towers), spheres [(x, z, radius)] (gas spheres on legs),
            flares [(x, z, height)] (flare stacks, guy braces 5 m out), racks [((x, z), (x, z), height)] (pipe racks)

only: style names that use the building (default: every style).
"""

BUILDINGS = [
    # -- towers: skyline, height cues, landing pads high up --------------------------------------------------------
    dict(name="Tower_Spire", type="tower", seed=101, plot=(34, 34), podium=6.0, facade="ribbon", crown="spire",
         tiers=[(1.0, 1.0, 9, 0, 0), (0.78, 0.78, 12, 0.04, -0.03), (0.56, 0.56, 8, 0.06, -0.05)]),
    dict(name="Tower_Pad", type="tower", seed=102, plot=(30, 30), podium=5.5, facade="grid", crown="pad",
         tiers=[(1.0, 1.0, 6, 0, 0), (0.84, 0.84, 10, 0, 0)]),
    dict(name="Tower_Slab", type="tower", seed=103, plot=(40, 22), podium=6.0, facade="ribbon", crown="sign",
         tiers=[(1.0, 1.0, 16, 0, 0), (0.6, 0.86, 5, -0.18, 0)]),
    dict(name="Tower_Mast", type="tower", seed=104, plot=(26, 26), podium=5.0, facade="grid", crown="antennas",
         tiers=[(1.0, 1.0, 14, 0, 0), (0.72, 0.72, 6, 0.08, 0.08)]),
    # -- megatowers: the cyberpunk skyline, 300 to 550 m; pads high up the walls ---------------------------------------
    dict(name="Mega_Arcology", type="mega", seed=501, plot=(64, 64), podium=10.0, crown="spire", lobby_every=20,
         tiers=[(1.0, 1.0, 24, 0, 0, "ribbon"), (0.86, 0.86, 30, 0, 0, "grid"), (0.72, 0.72, 32, 0, 0, "ribbon"),
                (0.58, 0.58, 26, 0, 0, "grid"), (0.44, 0.44, 16, 0, 0, "ribbon")],
         neon=(2, 4), billboards=[(1, "front"), (2, "left")], pads=[(3, "front"), (4, "right")]),
    dict(name="Mega_Needle", type="mega", seed=502, plot=(52, 52), podium=8.0, crown="antennas", lobby_every=16,
         tiers=[(0.8, 0.8, 40, 0, 0, "grid"), (0.62, 0.62, 40, 0, 0, "ribbon"), (0.45, 0.45, 24, 0, 0, "grid")],
         neon=(1, 2), billboards=[(0, "front")], pads=[(2, "left")]),
    dict(name="Mega_Slab", type="mega", seed=503, plot=(72, 44), podium=9.0, crown="pad", lobby_every=18,
         tiers=[(1.0, 1.0, 30, 0, 0, "ribbon"), (0.85, 0.7, 30, 0, 0, "grid"), (0.6, 0.45, 20, 0.08, 0, "ribbon")],
         neon=(0,), billboards=[(1, "front")], pads=[(2, "back")]),
    dict(name="Mega_Twins", type="twins", seed=504, plot=(100, 46), gap=26.0, podium=9.0, lobby_every=18,
         neon=(1,), towers=[([(36, "grid"), (30, "ribbon"), (24, "grid")], "antennas"),
                            ([(40, "ribbon"), (36, "grid"), (30, "ribbon")], "spire")],
         bridges=[("glass", 96.0, 10.0), ("glass", 180.0, 12.0), ("deck", 262.0, 22.0)]),
    # -- landmarks: high tech, unusual shapes -----------------------------------------------------------------------
    dict(name="Landmark_Twist", type="twist", seed=601, plot=(52, 52), size=32.0, podium=8.0, stacks=30, floors=3,
         turn=90.0, taper=0.25),
    dict(name="Landmark_Halo", type="halo", seed=602, plot=(80, 80), radius=15.0, podium=7.0, floors=80,
         rings=[(150.0, 34.0), (232.0, 26.0)]),
    dict(name="Landmark_Orb", type="orb", seed=603, plot=(80, 80), size=26.0, podium=7.0, floors=52, deck=38.0,
         orb=22.0, pads=(45.0, 225.0)),
    # -- blocks: mid-rise housing and offices; roofs are fight space ------------------------------------------------
    dict(name="Block_Housing", type="block", seed=201, plot=(36, 24), floors=6, shape="rect", roof="tanks",
         fire_escape="right", balconies=0.35),
    dict(name="Block_Corner", type="block", seed=202, plot=(36, 36), floors=8, shape="L", roof="tanks",
         fire_escape="left", balconies=0.25),
    dict(name="Block_Court", type="block", seed=203, plot=(44, 36), floors=5, shape="U", roof="pad",
         fire_escape=None, balconies=0.2),
    # -- slums: stacked shacks on old concrete; low cover, lots of detail ------------------------------------------
    dict(name="Slum_Stack", type="slum", seed=301, plot=(24, 20), base_floors=3, stacks=9, spread=1.6),
    dict(name="Slum_Tower", type="slum", seed=302, plot=(18, 18), base_floors=5, stacks=12, spread=1.2),
    dict(name="Slum_Low", type="slum", seed=303, plot=(28, 22), base_floors=2, stacks=7, spread=2.0),
    # -- industrial: halls, chimneys and tanks; tall thin landmarks ------------------------------------------------
    dict(name="Ind_Hall", type="industrial", seed=401, plot=(48, 36), hall=(40, 24, 12, 0, -4),
         chimneys=[(14, 13, 1.6, 34)], tanks=[], office=(12, 8, 3, -14, 13)),
    dict(name="Ind_Tanks", type="industrial", seed=402, plot=(44, 36), hall=(14, 10, 7, -14.5, -10),
         chimneys=[], tanks=[(-11, 6, 6.0, 13), (5, 8, 6.0, 13), (-1, -9, 4.5, 10), (12, -8, 3.5, 8)], office=None),
    dict(name="Ind_Stacks", type="industrial", seed=403, plot=(40, 32), hall=(26, 20, 18, 0, 2),
         chimneys=[(-9, -12, 2.4, 52), (0, -12, 2.4, 52)], tanks=[(16.5, -10, 3.0, 9)], office=None),
    # -- refinery (crater style): columns, cooling towers, gas spheres, flare stacks --------------------------------
    dict(name="Ind_Refinery", type="industrial", seed=411, plot=(52, 40), only=("crater",),
         hall=(14, 9, 6, -17, -13), chimneys=[], tanks=[(-18, 11, 3.5, 8)], office=None,
         columns=[(-4, -7, 2.2, 38), (4, -7, 1.8, 30), (12, -7, 2.6, 44), (-4, 7, 1.6, 24)],
         flares=[(17, 12, 48)], racks=[((-23, 0), (22, 0), 7.0)]),
    dict(name="Ind_Cooling", type="industrial", seed=412, plot=(64, 60), only=("crater",),
         hall=(16, 10, 8, 18, -21), chimneys=[], tanks=[], office=None,
         cooling=[(-12, -8, 15, 46), (15, 8, 12, 38)], racks=[((3, -21), (3, -28), 4.0)]),
    dict(name="Ind_Spheres", type="industrial", seed=413, plot=(48, 40), only=("crater",),
         hall=None, chimneys=[], tanks=[(16, -12, 4.0, 9)], office=None,
         spheres=[(-14, -8, 7.0), (2, -8, 7.0), (-14, 10, 6.0)], columns=[(14, 7, 2.0, 32)],
         racks=[((-22, 1.5), (22, 1.5), 5.0)]),
]


def for_style(style_name):
    """The base buildings a style uses (specs with `only` are kept to their styles)."""
    return [spec for spec in BUILDINGS if style_name in spec.get("only", (style_name,))]
