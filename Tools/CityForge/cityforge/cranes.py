"""
Tower cranes over the city (City_Cranes, placed once at the city origin like the roads) and their jib (Crane_Jib, a
model Unity turns; not placed). Construction cranes say "this city is still growing" and give the skyline moving
lights: the jib slews slowly in the game and floodlights under it light the ground (CityTrafficSystem).

Each crane: a square lattice mast (Lightning Yellow, braced every 4 m) on a concrete pad, a slewing ring at the top
and one box collider round the mast. The jib is not solid (it turns; choppers fly through it). Placement: off the
roads and footprints, the mast clear of every mesh, and the circle the jib sweeps above every building within reach
(the mast is as tall as it must be for that, 40 to 180 m). Kept 160 m apart.

The jib model (origin = the slewing pivot on the mast top, +Z = the jib): a triangular lattice jib (JIB m), a counter
jib with concrete counterweights, the operator's cab, a tower head with pendant ties, the trolley and hook, three
sodium floodlights under the jib (FLOODS, local z) and blinking red lights at the tip and the head.
"""
import math

import numpy as np
from shapely.geometry import Point

from . import roads
from .kit import L01, L2, Building

JIB = 50.0
COUNTER = 15.0
FLOODS = (12.0, 26.0, 40.0)
SETTINGS = dict(count=6, tries=500, apart=160.0, low=40.0, high=180.0, above=9.0, clear=4.0, seed=23)
MAST = 1.9                       # mast width (m)


def cranes(city, state, clearance):
    """City_Cranes (masts) with b.cranes = [(pivot (x, y, z), phase, speed, swing)] for the traffic data."""
    s = SETTINGS
    b = roads._road_building("City_Cranes", state.style, state.terrain.values["size"] if state.terrain else 3000.0)
    b.skip_floating = False
    b.ground_level = -1.0                       # the pads sit half a metre into the ground
    rng = np.random.default_rng(s["seed"])
    x0, z0, x1, z1 = city.rect.bounds
    keep_out = city.road_area.buffer(3.0)
    footprints = [shape for _name, shape in city.footprints]
    tops = state.building_tops
    placed = []
    for _ in range(s["tries"]):
        if len(placed) >= s["count"]:
            break
        x, z = float(rng.uniform(x0 + 30, x1 - 30)), float(rng.uniform(z0 + 30, z1 - 30))
        here = Point(x, z)
        if keep_out.contains(here) or any(shape.buffer(3.0).contains(here) for shape in footprints):
            continue
        if any(math.hypot(x - p[0][0], z - p[0][2]) < s["apart"] for p in placed):
            continue
        reach = here.buffer(JIB + 4.0)
        under = [top for shape, top in tops if shape.intersects(reach)]
        height = max([s["low"]] + [top + s["above"] for top in under])
        if height > s["high"]:
            continue
        mast = [(x, y, z) for y in np.arange(2.0, height, 6.0)]
        if (clearance(mast, s["clear"] * 2) < s["clear"]).any():
            continue
        ring = [(x + r * math.cos(a), height + 1.0, z + r * math.sin(a))
                for r in (COUNTER, JIB * 0.5, JIB) for a in np.linspace(0.0, math.tau, 24, endpoint=False)]
        if (clearance(ring, s["clear"] * 2) < s["clear"]).any():
            continue
        placed.append(((x, float(height), z), float(rng.uniform(0.0, math.tau)), float(rng.uniform(0.04, 0.09)),
                       float(rng.uniform(0.6, 1.4))))
    for (x, height, z), *_rest in placed:
        _mast(b, x, z, height)
    b.cranes = placed
    return b


def _mast(b, x, z, height):
    h = MAST / 2
    b.span(x - 3.0, x + 3.0, -0.5, 1.0, z - 3.0, z + 3.0, "concrete", L01)          # pad
    for sx in (-1, 1):
        for sz in (-1, 1):
            b.span(x + sx * h - 0.12, x + sx * h + 0.12, 1.0, height, z + sz * h - 0.12, z + sz * h + 0.12, "hazard", L01)
    y = 1.0
    while y + 4.0 <= height:
        for (ax, az), (bx, bz) in (((-h, -h), (h, -h)), ((h, -h), (h, h)), ((h, h), (-h, h)), ((-h, h), (-h, -h))):
            b.beam((x + ax, y, z + az), (x + bx, y + 4.0, z + bz), 0.1, "hazard", L01)
            b.beam((x + ax, y + 4.0, z + az), (x + bx, y + 4.0, z + bz), 0.1, "hazard", (0,))
        y += 4.0
    b.cylinder((x, height - 0.2, z), (x, height + 1.2, z), 1.6, "trim", segments=12, at=L01)   # slewing ring
    b.span(x - h, x + h, 0.0, height + 1.2, z - h, z + h, "hazard", L2)
    b.collider(x - h - 0.3, x + h + 0.3, 0.0, height + 1.2, z - h - 0.3, z + h + 0.3)


def jib_model(style):
    """Crane_Jib: the turning top of a tower crane (see the module notes)."""
    b = Building(dict(name="Crane_Jib", type="traffic", seed=4242, plot=(8.0, 2 * JIB + 10.0)), style)
    b.budgets = dict(lod0=9000, lod1=0.95, lod2=400)
    b.skip_uncovered = True
    b.skip_floating = True
    b.ground_level = -30.0
    w = 0.8                                                       # half width of the jib
    # Jib: two bottom chords and a top chord, braced every 4 m.
    for sx in (-w, w):
        b.span(sx - 0.1, sx + 0.1, 1.2, 1.4, 0.0, JIB, "hazard", L01)
    b.span(-0.1, 0.1, 2.9, 3.1, 0.0, JIB - 2.0, "hazard", L01)
    z = 0.0
    while z + 4.0 <= JIB - 2.0:
        for sx in (-w, w):
            b.beam((sx, 1.3, z), (0.0, 3.0, z + 2.0), 0.08, "hazard", L01)
            b.beam((0.0, 3.0, z + 2.0), (sx, 1.3, z + 4.0), 0.08, "hazard", L01)
        b.beam((-w, 1.3, z), (w, 1.3, z + 4.0), 0.08, "hazard", (0,))
        z += 4.0
    b.beam((-w, 1.3, JIB), (0.0, 3.0, JIB - 2.0), 0.1, "hazard", L01)
    b.beam((w, 1.3, JIB), (0.0, 3.0, JIB - 2.0), 0.1, "hazard", L01)
    # Counter jib and counterweights.
    b.span(-1.2, 1.2, 1.1, 1.6, -COUNTER, 0.0, "hazard", L01)
    for k in range(3):
        zz = -COUNTER + 1.0 + k * 1.6
        b.span(-1.5, 1.5, -1.2, 1.1, zz, zz + 1.4, "concrete", L01)
    # Cab, tower head, pendant ties.
    b.span(-1.6, -0.2, 0.0, 2.6, -1.0, 1.2, "trim", L01)
    b.span(-1.55, -0.25, 1.0, 2.3, 1.18, 1.25, "glass", L01)
    b.box((-0.9, 1.5, 1.27), (1.0, 0.25, 0.05), "lit_warm", L01)
    for sx in (-0.7, 0.7):
        b.beam((sx, 1.6, -1.0), (0.0, 10.0, 0.0), 0.14, "hazard", L01)
        b.beam((sx, 1.6, 1.0), (0.0, 10.0, 0.0), 0.14, "hazard", L01)
    b.beam((0.0, 10.0, 0.0), (0.0, 3.0, JIB * 0.7), 0.05, "steel", L01)
    b.beam((0.0, 10.0, 0.0), (0.0, 1.6, -COUNTER + 0.5), 0.05, "steel", L01)
    b.box((0.0, 10.3, 0.0), (0.3, 0.3, 0.3), "warning", L01)                # blinking lights
    b.box((0.0, 1.0, JIB), (0.3, 0.3, 0.3), "warning", L01)
    # Trolley and hook.
    b.span(-0.9, 0.9, 0.8, 1.2, 30.0, 32.0, "trim", L01)
    b.beam((0.0, 0.8, 31.0), (0.0, -18.0, 31.0), 0.05, "steel", L01)
    b.span(-0.4, 0.4, -19.5, -18.0, 30.6, 31.4, "hazard", L01)
    # Floodlights under the jib (Unity draws their beams down to the ground).
    for zz in FLOODS:
        b.span(-0.5, 0.5, 0.75, 1.1, zz - 0.3, zz + 0.3, "sodium", L01)
    b.span(-1.5, 1.5, -1.2, 3.1, -COUNTER, JIB, "hazard", L2)
    return b
