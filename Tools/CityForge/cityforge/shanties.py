"""
City_Shanties: a slum town dug into the inner wall of the rim (style option "shanties"). Clusters of shacks climb
the cliff: each shack's back is cut into the rock, its front stands out on steel stilts down to the slope below; stairs
zigzag from roof to roof, cables sag between neighbours, windows are lit and a few neon signs hang on the fronts.
One Building placed at the city origin (terrain space), like the roads, holds the decks, stilts, stairs, cables,
signs and the row colliders. The shacks themselves are instances (12 Nov 2026, build size): a few shack models
(`shack_models`, one per width and height step) placed through the layout (`b.instances`), so the wall town's mesh
data is a few small models plus the thin parts, not 5000 shacks baked into one mesh.

Style settings (styles.py, "shanties"): step (degrees between cluster spots round the wall), chance (a spot gets a
cluster), levels (shacks per cluster, low..high), height (lowest and highest floor above the crater floor, m),
start (share of that height range where a cluster's lowest level may start: 0 = all start at the bottom, 0.6 = some
start well up the wall), size ((width low, high), (height low, high)).
Kept away from lava, the canyon, tunnel portals and the map edge.
"""
import math

import numpy as np

from . import roads
from .buildings import _shack
from .kit import Building, L0, L01, Rect
from .terrain import polyline_distance

DEFAULTS = dict(step=2.4, chance=0.55, levels=(2, 7), height=(5.0, 120.0), start=0.0, size=((4.0, 8.0), (2.8, 3.6)))
NEON = ("neon_pink", "neon_cyan", "neon_green")
HEIGHT_STEPS = 3      # shack heights per style: low, middle, high of size[1]
BACK, FRONT = -2.5, 2.5   # local z of a shack's back (in the rock) and front (one depth for every model)


def _widths(s):
    low, high = s["size"][0]
    return list(range(math.ceil(low), math.floor(high) + 1)) or [round((low + high) / 2)]


def _heights(s):
    low, high = s["size"][1]
    return [round(low + (high - low) * k / (HEIGHT_STEPS - 1), 2) for k in range(HEIGHT_STEPS)]


def _model_name(w, k):
    return f"Shack_W{w}_H{k}"


def shanties(city, terrain, style):
    settings = style.get("shanties")
    if not settings or terrain is None:
        return None
    s = dict(DEFAULTS, **settings)
    # A wall town at night is mostly lit windows: three times the style's share.
    b = roads._road_building("City_Shanties", dict(style, lit=min(0.4, style["lit"] * 3.0)), terrain.values["size"])
    b.ground_level = -60.0
    # A whole wall town round the rim (5 Nov 2026: dense and tall at the owner's ask), not one building: a bigger
    # LOD0 budget than the other map-wide parts. LOD1 and LOD2 keep the shared limits.
    b.budgets = dict(roads.ROAD_BUDGETS, lod0=800000)
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

    widths, heights = _widths(s), _heights(s)
    b.instances = []            # (model name, position, yaw): the shacks, placed through the layout
    b.instance_points = []      # their corners (audit.py: the row colliders cover them, not this mesh)
    spots = []
    for angle in np.arange(0.0, 360.0, s["step"]):
        if rng.random() > s["chance"]:
            continue
        a0 = math.radians(angle + rng.uniform(-0.8, 0.8))
        floor = float(terrain.sample(cx + math.cos(a0) * radii[0], cz + math.sin(a0) * radii[0]))
        count = rng.randint(*s["levels"])
        wide = rng.randint(2, 4)                # shacks side by side on the lowest level; fewer higher up
        low, high = s["height"]
        level = floor + rng.uniform(low, low + max(25.0, s["start"] * (high - low)))
        drift = 0.0
        levels = []
        for k in range(count):
            if level > floor + s["height"][1]:
                break
            per = max(1, wide - k // 2 - rng.randint(0, 1))
            sizes = [(rng.choice(widths), rng.randrange(HEIGHT_STEPS)) for _ in range(per)]
            gaps = [rng.uniform(0.6, 2.5) for _ in range(per)]
            total = sum(w for w, _k in sizes) + sum(gaps[:-1])
            u = drift - total / 2
            row = []
            for (w, k_h), gap in zip(sizes, gaps):
                spot = wall_at(a0 + (u + w / 2) / reach, level)
                if spot is not None:
                    row.append((spot[0], level, spot[1], w, heights[k_h], spot[2]))
                u += w + gap
            if not row:
                break
            levels.append(row)
            drift += rng.uniform(-3.0, 3.0)    # each level a little off the one below
            level += max(heights[k_h] for _w, k_h in sizes) + rng.uniform(0.4, 2.5)
        if len(levels) >= 2:
            spots.append(levels)
    for levels in spots:
        for k, row in enumerate(levels):
            for x, y, z, w, h, facing in row:
                _wall_shack(b, terrain, x, y, z, w, h, _model_name(w, heights.index(h)), facing)
            for (xa, y, za, wa, _ha, fa), (xb, _y, zb, wb, _hb, _fb) in zip(row, row[1:]):
                _deck(b, terrain, (xa, za), (xb, zb), y, fa)
            if k % 2 == 0:
                x, y, z, _w, h, (nx, nz) = row[len(row) // 2]
                b.light("shanty", (x + nx * 5.0, y + h * 0.6, z + nz * 5.0), "#ffb070", 14.0, 1.0)
            if k:
                # A stair from the nearest roof below to this level's first shack.
                x, y, z, _w, _h, facing = row[0]
                px, py, pz, _pw, ph, _pf = min(levels[k - 1], key=lambda s_: math.hypot(s_[0] - x, s_[2] - z))
                _stair(b, (px, py + ph + 0.12, pz), (x, y, z), facing)
    _row_colliders(b, spots)
    spots = [[shack for row in levels for shack in row] for levels in spots]
    _cables(b, spots)
    b.colliders = b.row_colliders      # cables, stairs and decks get none (see _row_colliders)
    b.clusters = spots          # for the preview shot
    return b


def _row_colliders(b, spots):
    """One box per row of shacks (a level of a cluster), not one per shack, deck, stair and cable: the wall town had
    24 837 colliders, which overflowed the Entities name table and made the subscene bake run for many minutes
    (5 Nov 2026). Players hit the shacks as blocks; stilts, stairs and cables are thin and have none, so the audit's
    "uncovered points" check is off for this part (skip_uncovered)."""
    boxes = []
    saved, b.colliders = b.colliders, boxes
    for levels in spots:
        for row in levels:
            mx, y, mz, _w, _h, facing = row[len(row) // 2]
            yaw = _yaw(facing)
            a = math.radians(yaw)
            # Local +X of a Unity yaw is (cos, -sin) in world xz: the shacks' offsets along the row.
            spans = [((x - mx) * math.cos(a) - (z - mz) * math.sin(a), w) for x, _y, z, w, _hh, _f in row]
            u0 = min(u - w / 2 for u, w in spans) - 0.2
            u1 = max(u + w / 2 for u, w in spans) + 0.2
            top = max(h for _x, _y, _z, _w, h, _f in row)
            with b.turned(mx, mz, yaw):
                b.collider(mx + u0, mx + u1, y - 0.25, y + top, mz - 2.5, mz + 3.6)
    b.colliders = saved
    b.row_colliders = boxes
    b.skip_uncovered = True


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


def _wall_shack(b, terrain, x, y, z, w, h, model, facing):
    """A shack (an instance of `model`, see shack_models) with its back 2.5 m in the rock and its front out over the
    slope, on stilts to the ground below; a neon sign on some."""
    rng = b.rng
    yaw = _yaw(facing)
    b.instances.append((model, (x, y, z), yaw))
    nx, nz = facing
    tx, tz = -nz, nx
    b.instance_points += [(x + tx * side * w / 2 + nx * d, y + h + 0.12, z + tz * side * w / 2 + nz * d)
                          for side in (-1, 1) for d in (BACK, FRONT)]     # for the audit (colliders cover them)
    if rng.random() < 0.25:
        with b.turned(x, z, yaw):
            b.span(x - w * 0.3, x + w * 0.3, y + h - 0.9, y + h - 0.3, z + FRONT + 0.05, z + FRONT + 0.15, rng.choice(NEON), L01)
    # Stilts under the two front corners, down to the slope (terrain heights at their world positions).
    for side in (-1, 1):
        reach = FRONT - 0.2
        sx, sz = x + tx * side * (w / 2 - 0.2) + nx * reach, z + tz * side * (w / 2 - 0.2) + nz * reach
        ground = float(terrain.sample(sx, sz)) - 0.5
        if y - ground < 0.4:
            continue
        b.span(sx - 0.1, sx + 0.1, ground, y - 0.25, sz - 0.1, sz + 0.1, "metal", L01)


def shack_models(style):
    """The shack models the wall town places (one per width and height step): walls, roof, door, windows and the
    floor deck, in a local frame (wall at z = 0, floor at y = 0, front towards +z). No colliders: the row boxes in
    City_Shanties cover them. Empty when the style has no wall town."""
    settings = style.get("shanties")
    if not settings:
        return []
    s = dict(DEFAULTS, **settings)
    look = dict(style, lit=min(0.4, style["lit"] * 3.0))
    out = []
    for w in _widths(s):
        for k, h in enumerate(_heights(s)):
            name = _model_name(w, k)
            m = Building(dict(name=name, type="props", seed=sum(map(ord, name)), plot=(w + 0.6, FRONT - BACK + 0.6)), look)
            rect = Rect(-w / 2, w / 2, BACK, FRONT)
            _shack(m, rect, 0.0, h)
            m.span(rect.x0 - 0.2, rect.x1 + 0.2, -0.25, 0.0, rect.z0, rect.z1 + 0.6, "metal", L01)   # floor deck
            m.colliders = []
            m.skip_uncovered = True
            m.ground_level = -0.25   # the floor deck's underside
            out.append(m)
    return out


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
            k += 3.2                           # a post and a bulb every 3.2 m (was 1.6: build size)
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
    steps = max(1, int(abs(c[1] - a[1]) / 0.9))     # a tread every 0.9 m of rise (was 0.3: build size)
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
        if best is not None and b.rng.random() < 0.3:
            roads._cable(b, p, best[1], sag=1.5 + best[0] * 0.04, pieces=3)
