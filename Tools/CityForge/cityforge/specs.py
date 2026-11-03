"""
Building specs: the numbers that shape each building. Change a value or the seed and run the generator again.

Common keys: name, type (tower | block | slum | industrial), seed, plot (width x, depth z in metres: the lot the
building must fit in; the origin is the plot centre on the ground). The front (shop fronts, signs) faces +Z.

tower:      podium (ground floor height), tiers [(width share, depth share, floors, x offset share, z offset share)],
            facade (ribbon | grid), crown (pad | antennas | sign | spire)
block:      floors, shape (rect | L | U), roof (tanks | pad), fire_escape (left | right | None), balconies (0..1)
slum:       base_floors, stacks (shacks to pile on the base), spread (overhang, m)
industrial: hall (w, d, h, x, z) or None, chimneys [(x, z, radius, height)], tanks [(x, z, radius, height)],
            office (w, d, floors, x, z) or None (x, z = centre)
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
]
