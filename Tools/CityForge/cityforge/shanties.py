"""
City_Shanties: a slum town dug into the inner wall of the rim (style option "shanties"). Clusters of shacks climb
the cliff: each shack's back is cut into the rock, its front stands out on steel stilts down to the slope below; stairs
zigzag from roof to roof, cables sag between neighbours, windows are lit and a few neon signs hang on the fronts.
One Building placed at the city origin (terrain space), like the roads.

Style settings (styles.py, "shanties"): step (degrees between cluster spots round the wall), chance (a spot gets a
cluster), levels (shacks per cluster, low..high), height (lowest and highest floor above the crater floor, m),
size ((width low, high), (height low, high)).
Kept away from lava, the canyon, tunnel portals and the map edge.
"""
import math

import numpy as np

from . import roads
from .buildings import _shack
from .kit import L01, Rect
from .terrain import polyline_distance

DEFAULTS = dict(step=2.4, chance=0.55, levels=(2, 7), height=(5.0, 120.0), size=((4.0, 8.0), (2.8, 3.6)))
NEON = ("neon_pink", "neon_cyan", "neon_green")


def shanties(city, terrain, style):
    settings = style.get("shanties")
    if not settings or terrain is None:
        return None
    s = dict(DEFAULTS, **settings)
    b = roads._road_building("City_Shanties", style, terrain.values["size"])
    b.ground_level = -60.0
    rng = b.rng
    x0, x1, z0, z1 = terrain.city
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    plateau = terrain.values["plateau"]
    inner = plateau["radius"] - 220.0
    outer = plateau["radius"] + plateau["wall_at"] + 60.0
    half = terrain.values["size"] / 2 - 30.0
    canyon_clear = max(terrain.values["canyon"]["width"]) / 2 + 80.0
    radii = np.arange(inner, outer, 1.5)
    spots = []
    for angle in np.arange(0.0, 360.0, s["step"]):
        if rng.random() > s["chance"]:
            continue
        a = math.radians(angle + rng.uniform(-0.8, 0.8))
        ux, uz = math.cos(a), math.sin(a)
        xs, zs = cx + ux * radii, cz + uz * radii
        hs = terrain.sample(xs, zs)
        floor = float(hs[0])
        count = rng.randint(*s["levels"])
        level = floor + rng.uniform(s["height"][0], s["height"][0] + 25.0)
        cluster = []
        for _k in range(count):
            if level > floor + s["height"][1]:
                break
            i = int(np.argmax(hs >= level)) if (hs >= level).any() else -1
            if i < 4 or i > len(radii) - 5:
                break
            steep = (hs[i + 3] - hs[i - 3]) / (6 * 1.5)
            x, z = float(xs[i]), float(zs[i])
            if steep < 1.0 or abs(x) > half or abs(z) > half or not _clear(terrain, x, z, canyon_clear):
                break
            w = rng.uniform(*s["size"][0])
            h = rng.uniform(*s["size"][1])
            cluster.append((x, level, z, w, h, (-ux, -uz)))
            level += h + rng.uniform(0.3, 2.5)
        if len(cluster) >= 2:
            spots.append(cluster)
    for cluster in spots:
        for k, (x, y, z, w, h, facing) in enumerate(cluster):
            _wall_shack(b, terrain, x, y, z, w, h, facing)
            if k:
                px, py, pz, _pw, ph, _pf = cluster[k - 1]
                _stair(b, (px, py + ph + 0.12, pz), (x, y, z), facing)
    _cables(b, spots)
    b.clusters = spots          # for the preview shot
    return b


def _clear(terrain, x, z, canyon_clear):
    """Not in or near lava, the canyon (the breach) or a tunnel portal."""
    point_x, point_z = np.array([x]), np.array([z])
    if float(terrain.lava_distance(point_x, point_z)[0]) < 40.0:
        return False
    if float(polyline_distance(point_x, point_z, terrain.canyon)[0][0]) < canyon_clear:
        return False
    return all(math.hypot(x - p["position"][0], z - p["position"][1]) > 90.0 for p in terrain.portals)


def _yaw(facing):
    """Unity yaw that turns local +Z to the facing (nx, nz)."""
    return math.degrees(math.atan2(facing[0], facing[1]))


def _wall_shack(b, terrain, x, y, z, w, h, facing):
    """A shack with its back 2.5 m in the rock and its front out over the slope, on stilts to the ground below."""
    rng = b.rng
    depth = rng.uniform(4.0, 6.0)
    back, front = -2.5, depth - 2.5           # local z of the back and the front (0 = the wall at floor height)
    with b.turned(x, z, _yaw(facing)):
        rect = Rect(x - w / 2, x + w / 2, z + back, z + front)
        _shack(b, rect, y, h)
        b.span(rect.x0 - 0.2, rect.x1 + 0.2, y - 0.25, y, rect.z0, rect.z1 + 0.6, "metal", L01)   # floor deck
        if rng.random() < 0.25:
            b.span(x - w * 0.3, x + w * 0.3, y + h - 0.9, y + h - 0.3, z + front + 0.05, z + front + 0.15,
                   rng.choice(NEON), L01)
    # Stilts under the front corners, down to the slope (terrain heights at their world positions).
    nx, nz = facing
    tx, tz = -nz, nx
    for side in (-1, 1):
        for reach in (front - 0.2, front * 0.4):
            sx, sz = x + tx * side * (w / 2 - 0.2) + nx * reach, z + tz * side * (w / 2 - 0.2) + nz * reach
            ground = float(terrain.sample(sx, sz)) - 0.5
            if y - ground < 0.4:
                continue
            b.span(sx - 0.1, sx + 0.1, ground, y - 0.25, sz - 0.1, sz + 0.1, "metal", L01)
            b.collider(sx - 0.15, sx + 0.15, ground, y - 0.25, sz - 0.15, sz + 0.15)


def _stair(b, low, high, facing):
    """A steel stair from a roof to the next shack's floor: stringer, treads, a hand rail; solid."""
    nx, nz = facing
    tx, tz = -nz, nx
    # Start out on the roof's front, end at the next floor's front edge, to the side of the door.
    a = (low[0] + tx * 1.0 + nx * 1.5, low[1], low[2] + tz * 1.0 + nz * 1.5)
    c = (high[0] + tx * 1.0 + nx * 2.0, high[1], high[2] + tz * 1.0 + nz * 2.0)
    if math.dist(a, c) < 0.5:
        return
    b.beam(a, c, 0.5, "metal", L01)
    steps = max(1, int(abs(c[1] - a[1]) / 0.3))
    for k in range(1, steps):
        t = k / steps
        p = tuple(a[i] + (c[i] - a[i]) * t for i in range(3))
        b.box(p, (0.9, 0.05, 0.3), "metal")
    b.beam((a[0], a[1] + 1.0, a[2]), (c[0], c[1] + 1.0, c[2]), 0.06, "hazard")
    dx, dy, dz = c[0] - a[0], c[1] - a[1], c[2] - a[2]
    b.colliders.append((((a[0] + c[0]) / 2, (a[1] + c[1]) / 2 + 0.3, (a[2] + c[2]) / 2),
                        (1.0, abs(dy) + 1.2, max(0.5, math.hypot(dx, dz))), math.degrees(math.atan2(dx, dz))))


def _cables(b, spots):
    """Power and washing lines between neighbouring clusters: from a roof corner to one at about the same height."""
    roofs = [(x, y + h + 0.6, z) for cluster in spots for x, y, z, _w, h, _f in cluster]
    for k, p in enumerate(roofs):
        best = None
        for q in roofs[k + 1:]:
            d = math.hypot(q[0] - p[0], q[2] - p[2])
            if 12.0 < d < 70.0 and abs(q[1] - p[1]) < 8.0 and (best is None or d < best[0]):
                best = (d, q)
        if best is not None and b.rng.random() < 0.6:
            roads._cable(b, p, best[1], sag=1.5 + best[0] * 0.04, pieces=5)
