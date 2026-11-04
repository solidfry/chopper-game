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
from .kit import L0, L01, Rect
from .terrain import polyline_distance

DEFAULTS = dict(step=2.4, chance=0.55, levels=(2, 7), height=(5.0, 120.0), size=((4.0, 8.0), (2.8, 3.6)))
NEON = ("neon_pink", "neon_cyan", "neon_green")


def shanties(city, terrain, style):
    settings = style.get("shanties")
    if not settings or terrain is None:
        return None
    s = dict(DEFAULTS, **settings)
    # A wall town at night is mostly lit windows: three times the style's share.
    b = roads._road_building("City_Shanties", dict(style, lit=min(0.4, style["lit"] * 3.0)), terrain.values["size"])
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
    reach = plateau["radius"]

    def wall_at(a, level):
        """Where the wall reaches `level` along the ray at angle a: (x, z, facing) or None (not a steep wall there,
        or too close to lava, the canyon, a portal or the map edge)."""
        ux, uz = math.cos(a), math.sin(a)
        xs, zs = cx + ux * radii, cz + uz * radii
        hs = terrain.sample(xs, zs)
        if not (hs >= level).any():
            return None
        i = int(np.argmax(hs >= level))
        if i < 4 or i > len(radii) - 5 or (hs[i + 3] - hs[i - 3]) / (6 * 1.5) < 1.0:
            return None
        x, z = float(xs[i]), float(zs[i])
        if abs(x) > half or abs(z) > half or not _clear(terrain, x, z, canyon_clear):
            return None
        return x, z, (-ux, -uz)

    spots = []
    for angle in np.arange(0.0, 360.0, s["step"]):
        if rng.random() > s["chance"]:
            continue
        a0 = math.radians(angle + rng.uniform(-0.8, 0.8))
        floor = float(terrain.sample(cx + math.cos(a0) * radii[0], cz + math.sin(a0) * radii[0]))
        count = rng.randint(*s["levels"])
        wide = rng.randint(2, 4)                # shacks side by side on the lowest level; fewer higher up
        level = floor + rng.uniform(s["height"][0], s["height"][0] + 25.0)
        drift = 0.0
        levels = []
        for k in range(count):
            if level > floor + s["height"][1]:
                break
            per = max(1, wide - k // 2 - rng.randint(0, 1))
            sizes = [(rng.uniform(*s["size"][0]), rng.uniform(*s["size"][1])) for _ in range(per)]
            gaps = [rng.uniform(0.6, 2.5) for _ in range(per)]
            total = sum(w for w, _h in sizes) + sum(gaps[:-1])
            u = drift - total / 2
            row = []
            for (w, h), gap in zip(sizes, gaps):
                spot = wall_at(a0 + (u + w / 2) / reach, level)
                if spot is not None:
                    row.append((spot[0], level, spot[1], w, h, spot[2]))
                u += w + gap
            if not row:
                break
            levels.append(row)
            drift += rng.uniform(-3.0, 3.0)    # each level a little off the one below
            level += max(h for _w, h in sizes) + rng.uniform(0.4, 2.5)
        if len(levels) >= 2:
            spots.append(levels)
    for levels in spots:
        for k, row in enumerate(levels):
            for x, y, z, w, h, facing in row:
                _wall_shack(b, terrain, x, y, z, w, h, facing)
            for (xa, y, za, wa, _ha, fa), (xb, _y, zb, wb, _hb, _fb) in zip(row, row[1:]):
                _deck(b, terrain, (xa, za), (xb, zb), y, fa)
            if k:
                # A stair from the nearest roof below to this level's first shack.
                x, y, z, _w, _h, facing = row[0]
                px, py, pz, _pw, ph, _pf = min(levels[k - 1], key=lambda s_: math.hypot(s_[0] - x, s_[2] - z))
                _stair(b, (px, py + ph + 0.12, pz), (x, y, z), facing)
    spots = [[shack for row in levels for shack in row] for levels in spots]
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
        b.collider(rect.x0 - 0.2, rect.x1 + 0.2, y - 0.25, y, rect.z0, rect.z1 + 0.6)
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


def _deck(b, terrain, p, q, y, facing):
    """An open walkway along the wall between two shacks of a level: plank deck out in front of the doors, a rail on
    the open side, a post down to the slope, a string of bulbs."""
    nx, nz = facing
    out = 3.2                                  # in front of the shack fronts (the front is 1.5 .. 3.5 m out)
    a = (p[0] + nx * out, p[1] + nz * out)
    c = (q[0] + nx * out, q[1] + nz * out)
    dx, dz = c[0] - a[0], c[1] - a[1]
    length = math.hypot(dx, dz)
    if length < 1.0:
        return
    mx, mz = (a[0] + c[0]) / 2, (a[1] + c[1]) / 2
    with b.turned(mx, mz, math.degrees(math.atan2(dx, dz))):
        b.span(mx - 0.9, mx + 0.9, y - 0.2, y, mz - length / 2, mz + length / 2, "rust", L01)
        rail = mx + 0.9 if (nx * dz - nz * dx) < 0 else mx - 0.9    # the side away from the wall
        b.span(rail - 0.03, rail + 0.03, y + 0.95, y + 1.0, mz - length / 2, mz + length / 2, "metal", L01)
        k = mz - length / 2
        while k <= mz + length / 2:
            b.span(rail - 0.03, rail + 0.03, y, y + 1.0, k - 0.03, k + 0.03, "metal")
            b.box((rail, y + 2.3, k), (0.12, 0.12, 0.12), b.lit_swatch(), L0)
            k += 1.6
        b.collider(mx - 0.9, mx + 0.9, y - 0.2, y + 1.0, mz - length / 2, mz + length / 2)
    ground = float(terrain.sample(mx, mz)) - 0.5
    if y - ground > 0.5:
        b.span(mx - 0.1, mx + 0.1, ground, y - 0.2, mz - 0.1, mz + 0.1, "metal", L01)
        b.collider(mx - 0.15, mx + 0.15, ground, y - 0.2, mz - 0.15, mz + 0.15)


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
