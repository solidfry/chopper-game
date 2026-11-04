"""
Road meshes, each a Building (LODs, box colliders, audit) placed once at the city origin:

    City_Roads     street surfaces (the layout's road area as one polygon with holes, cut at the channel), centre
                   lines on main roads, sodium street lights, bridges over the channel, the roads out of the city
                   (strips on the flattened terrain), tunnel portals in the rim (the road ends out of sight)
    City_Freeway   the elevated freeway along the storm drain: 2 x 2 lanes on T piers, ramps down to the ground at
                   both ends (west to the tunnel, east into the canyon)
    City_Utilities pipe racks between the industrial plots (pipe bridges over the streets), power lines on the
                   roads out of the city, parked trucks, vans, dumpsters and barriers in the small streets
    City_SkyRoads  a sky road loop round the core (rises and falls, kept clear of the towers) and a spiral ramp that
                   winds down from it to a plaza, round a central column

Swept decks: a cross-section moved along a path of (x, y, z) points (y = deck top); colliders are short boxes per
segment, turned by Unity yaw.
"""
import math

import numpy as np
import shapely
import shapely.ops
from shapely.geometry import LineString, Point

from . import layout as layout_module
from . import kit
from .kit import ALL, L0, L01, L2, Building, sw

CITY_PARTS = ("City_Roads", "City_Freeway", "City_SkyRoads", "City_Utilities", "City_Shanties")   # placed once at the city origin
ROAD_BUDGETS = dict(lod0=400000, lod1=0.95, lod2=60000)
SURFACE_Y = 0.05


def _road_building(name, style, size):
    b = Building(dict(name=name, type="props", seed=sum(map(ord, name)), plot=(size, size)), style)
    b.budgets = ROAD_BUDGETS
    b.skip_floating = True
    return b


def _polygon(b, points, swatch, at):
    """A flat face from 3D points (already in order)."""
    index = sw(swatch)
    for level in at:
        builder = b.lods[level]
        builder._face(builder._verts(points), index)


def _frames(points, closed=False):
    """Per point: (right vector x, z) from the horizontal direction of travel."""
    out = []
    count = len(points)
    for k in range(count):
        a = points[k - 1] if (k > 0 or closed) else points[k]
        c = points[(k + 1) % count] if (k < count - 1 or closed) else points[k]
        dx, dz = c[0] - a[0], c[2] - a[2]
        length = math.hypot(dx, dz) or 1.0
        out.append((dz / length, -dx / length))   # right of travel in Unity (x right, z forward)
    return out


def sweep(b, points, width, thick, swatches, at, offset=0.0, closed=False, lift=0.0):
    """A box cross-section (width x thick, top at y + lift) along the path. swatches: (top, right, bottom, left)."""
    frames = _frames(points, closed)
    rings = []
    for (x, y, z), (rx, rz) in zip(points, frames):
        cx, cz = x + rx * offset, z + rz * offset
        top = y + lift
        rings.append([(cx - rx * width / 2, top, cz - rz * width / 2), (cx + rx * width / 2, top, cz + rz * width / 2),
                      (cx + rx * width / 2, top - thick, cz + rz * width / 2),
                      (cx - rx * width / 2, top - thick, cz - rz * width / 2)])
    pairs = list(zip(rings, rings[1:])) + ([(rings[-1], rings[0])] if closed else [])
    order = [(0, 1), (1, 2), (2, 3), (3, 0)]   # top, right, bottom, left
    for level in at:
        builder = b.lods[level]
        verts = [builder._verts(ring) for ring in rings]
        index = {id(r): v for r, v in zip(rings, verts)}
        for ra, rb in pairs:
            va, vb = index[id(ra)], index[id(rb)]
            for (i, j), swatch in zip(order, swatches):
                builder._face([va[i], va[j], vb[j], vb[i]], sw(swatch))
        if not closed:
            builder._face(list(reversed(verts[0])), sw(swatches[2]))
            builder._face(verts[-1], sw(swatches[2]))


def segment_colliders(b, points, width, height, closed=False, step=1):
    """Short boxes along a swept deck (top at y, `height` down), turned by yaw; tall enough for the slope."""
    pairs = list(zip(points, points[1:])) + ([(points[-1], points[0])] if closed else [])
    for k, (a, c) in enumerate(pairs):
        if k % step:
            continue
        end = not closed and k in (0, len(pairs) - 1)
        dx, dy, dz = c[0] - a[0], c[1] - a[1], c[2] - a[2]
        length = math.hypot(dx, dz)
        if length < 0.5:
            continue
        yaw = math.degrees(math.atan2(dx, dz))
        h = height + abs(dy)
        b.colliders.append((((a[0] + c[0]) / 2, (a[1] + c[1]) / 2 - height / 2 + 0.6, (a[2] + c[2]) / 2),
                            (width, h, length + (0.0 if end else 0.5)), yaw))


def resample(points, spacing):
    """Even spacing along a 3D polyline (by horizontal distance)."""
    pts = np.array(points, dtype=float)
    seg = np.hypot(np.diff(pts[:, 0]), np.diff(pts[:, 2]))
    runs = np.concatenate([[0.0], np.cumsum(seg)])
    total = runs[-1]
    count = max(2, int(total // spacing) + 1)
    s = np.linspace(0.0, total, count)
    return [tuple(np.interp(v, runs, pts[:, k]) for k in range(3)) for v in s]


def lamp(b, x, y, z, facing, height=7.0):
    """Street light: post and a sodium head over the road."""
    fx, fz = facing
    b.span(x - 0.12, x + 0.12, y, y + height, z - 0.12, z + 0.12, "metal")
    hx, hz = x + fx * 0.9, z + fz * 0.9
    b.box((hx, y + height + 0.1, hz), (0.5 + abs(fx) * 1.4, 0.25, 0.5 + abs(fz) * 1.4), "dark")
    b.box((hx, y + height - 0.05, hz), (0.4 + abs(fx) * 1.2, 0.08, 0.4 + abs(fz) * 1.2), "sodium", L01)
    b.add_thin(x, z, y + height + 0.4, 2.0)


def dashes(b, line, y, spacing=10.0, length=3.0, at=L0, skip=None):
    """Centre line dashes along a 2D shapely line."""
    d = 0.0
    while d + length < line.length:
        a, c = line.interpolate(d), line.interpolate(d + length)
        if skip is None or not skip.contains(Point((a.x + c.x) / 2, (a.y + c.y) / 2)):
            b.beam((a.x, y, a.y), (c.x, y, c.y), 0.22, "trim", at)
        d += spacing


# -- City_Roads ---------------------------------------------------------------------------------------------------------

def city_roads(city, terrain, style):
    size = terrain.values["size"] if terrain else 3000.0
    b = _road_building("City_Roads", style, size + 700.0)
    b.ground_level = -60.0
    surface = city.road_area.intersection(city.rect).difference(city.channel)
    # Street surface at 0.05 m: triangles from a constrained Delaunay of the road area (holes = the blocks). The
    # builder's normal fix cannot orient an open flat surface, so build.py turns these faces up afterwards
    # (face_up_at below).
    for triangle in shapely.constrained_delaunay_triangles(surface).geoms:
        coords = list(triangle.exterior.coords)[:3]
        _polygon(b, [(x, SURFACE_Y, z) for x, z in coords], "dark", ALL)
    b.face_up_at = SURFACE_Y
    # Centre lines and street lights on the main roads.
    lit = city.channel.buffer(4.0)
    for road in city.roads:
        if road.kind in ("street", "frontage"):
            continue
        line = road.line.intersection(city.rect)
        for part in layout_module._parts(line):
            if part.geom_type != "LineString":
                continue
            dashes(b, part, 0.08, skip=lit)
            d = 18.0
            while d < part.length:
                p, q = part.interpolate(d), part.interpolate(min(part.length, d + 1.0))
                dx, dz = q.x - p.x, q.y - p.y
                n = math.hypot(dx, dz) or 1.0
                rx, rz = dz / n, -dx / n
                for side in (-1, 1):
                    lx, lz = p.x + rx * side * (road.width / 2 + 1.2), p.y + rz * side * (road.width / 2 + 1.2)
                    spot = Point(lx, lz)
                    if not lit.contains(spot) and city.rect.contains(spot) and \
                            not any(shape.contains(spot) for _n, shape in city.footprints):
                        lamp(b, lx, 0.05, lz, (-rx * side, -rz * side))
                d += 38.0
    # Bridges over the channel.
    for x, z, yaw, width in city.bridges:
        with b.turned(x, z, yaw):
            hw = width / 2
            b.span(x - hw, x + hw, -1.4, 0.15, z - 31.0, z + 31.0, "concrete", ALL, swatches={"top": "dark"})
            b.collider(x - hw, x + hw, -1.4, 0.15, z - 31.0, z + 31.0)
            for side in (-1, 1):
                rx = x + side * (hw - 0.25)
                b.span(rx - 0.25, rx + 0.25, 0.15, 1.2, z - 31.0, z + 31.0, "trim", L01)
                for k in range(4):
                    zz = z - 24.0 + k * 16.0
                    b.span(rx - 0.15, rx + 0.15, 1.2, 1.35, zz - 0.15, zz + 0.15, "sodium", L0)
            b.span(x - hw * 0.6, x + hw * 0.6, -8.0, -1.4, z - 1.0, z + 1.0, "concrete", ALL)
            b.collider(x - hw * 0.6, x + hw * 0.6, -8.0, -1.4, z - 1.0, z + 1.0)
    if terrain is not None:
        _exterior(b, terrain)
    return b


def _exterior(b, terrain):
    """Roads out of the city on the flattened terrain, and tunnel portals where they meet the rim."""
    for road in terrain.roads:
        points = road["points"]
        runs = np.concatenate([[0.0], np.cumsum([math.hypot(q[0] - p[0], q[1] - p[1])
                                                 for p, q in zip(points, points[1:])])])
        path = [(x, h, z) for (x, z), h in zip(points, road["heights"])]
        path = resample(path, 8.0)
        half = terrain.values["size"] / 2 + 250.0
        path = [p for p in path if abs(p[0]) < half and abs(p[2]) < half]
        if len(path) < 2:
            continue
        sweep(b, path, road["width"], 0.6, ("dark", "concrete", "concrete", "concrete"), ALL, lift=0.12)
        line = LineString([(p[0], p[2]) for p in path])
        heights = [p[1] for p in path]
        d = 0.0
        while d + 3.0 < line.length:
            a, c = line.interpolate(d), line.interpolate(d + 3.0)
            ya = float(np.interp(d, np.linspace(0, line.length, len(heights)), heights))
            b.beam((a.x, ya + 0.15, a.y), (c.x, ya + 0.15, c.y), 0.22, "trim", L0)
            d += 10.0
        del runs
    for portal in terrain.portals:
        _portal(b, portal, terrain)


def _portal(b, portal, terrain):
    """Tunnel portal: a concrete headwall with the opening, wing walls, a dark recess with a few lights going in, a
    neon sign. The road runs into the dark; the end is never seen."""
    x, z = portal["position"]
    y = float(terrain.sample(x, z))
    w = portal["width"]
    with b.turned(x, z, portal["yaw"]):
        # Local frame: front (+Z) faces the city; the tunnel goes in towards -Z.
        hw, height, opening = w / 2, 18.0, 8.0
        b.span(x - hw - 9.0, x - hw, y, y + height, z - 4.0, z, "concrete", ALL)
        b.span(x + hw, x + hw + 9.0, y, y + height, z - 4.0, z, "concrete", ALL)
        b.span(x - hw, x + hw, y + opening, y + height, z - 4.0, z, "concrete", ALL)
        b.span(x - hw - 9.5, x + hw + 9.5, y + height, y + height + 1.0, z - 4.5, z + 0.5, "trim", L01)
        b.collider(x - hw - 9.0, x + hw + 9.0, y, y + height + 1.0, z - 4.0, z)
        b.collider(x - hw, x + hw, y, y + opening, z - 30.0, z - 4.0)   # no flying into the hill
        b.span(x - hw, x + hw, y, y + opening, z - 30.0, z - 29.0, "rubber", L01)        # the dark at the back
        b.span(x - hw, x - hw + 0.5, y, y + opening, z - 30.0, z - 4.0, "dark", L01)
        b.span(x + hw - 0.5, x + hw, y, y + opening, z - 30.0, z - 4.0, "dark", L01)
        b.span(x - hw, x + hw, y + opening - 0.5, y + opening, z - 30.0, z - 4.0, "dark", L01)
        for k in range(5):
            zz = z - 6.0 - k * 5.0
            for side in (-1, 1):
                b.box((x + side * (hw - 0.6), y + opening - 1.2, zz), (0.2, 0.2, 1.2), "sodium", L01)
        for side in (-1, 1):
            b.beam((x + side * (hw + 9.0), y + 6.0, z), (x + side * (hw + 24.0), y + 1.0, z + 14.0), 2.0, "concrete")
            lo, hi = sorted((x + side * (hw + 9.0), x + side * (hw + 24.0)))
            b.collider(lo, hi, y, y + 7.0, z, z + 14.0)
        b.span(x - 7.0, x + 7.0, y + height - 6.0, y + height - 3.0, z, z + 0.4, "dark", L01)
        b.span(x - 6.5, x + 6.5, y + height - 4.8, y + height - 4.2, z + 0.4, z + 0.5, "neon_cyan", L01)
        b.span(x - 6.5, x + 6.5, y + height - 5.6, y + height - 5.3, z + 0.4, z + 0.5, "warning", L01)


# -- City_Freeway -------------------------------------------------------------------------------------------------------

FREEWAY = dict(height=16.0, width=layout_module.FREEWAY_WIDTH, thick=1.8, pier=40.0)


def freeway(city, terrain, style):
    b = _road_building("City_Freeway", style, terrain.values["size"] if terrain else 3000.0)
    b.ground_level = -60.0
    e = city.exterior()
    fz, ramp = e["freeway_z"], e["ramp"]
    x0, x1 = city.city_rect()[0], city.city_rect()[1]
    f = FREEWAY

    def ground(x, z):
        return float(terrain.sample(x, z)) if terrain is not None else 0.0

    xs = np.arange(-ramp, ramp + 0.1, 10.0)
    path = []
    for x in xs:
        if x < x0:
            t = (x - (-ramp)) / (x0 - (-ramp))
        elif x > x1:
            t = (ramp - x) / (ramp - x1)
        else:
            t = 1.0
        t = max(0.0, min(1.0, t))
        lift = f["height"] * (t * t * (3 - 2 * t))
        path.append((float(x), ground(x, fz) + 0.15 + lift, fz))
    w = f["width"]
    sweep(b, path, w, f["thick"], ("dark", "concrete", "concrete", "concrete"), L01)
    sweep(b, path[::4] + [path[-1]], w, f["thick"], ("dark", "concrete", "concrete", "concrete"), L2)
    for side in (-1, 1):
        sweep(b, path, 0.5, -1.1, ("trim", "concrete", "concrete", "concrete"), L01, offset=side * (w / 2 - 0.25))
        sweep(b, path, 0.15, 0.15, ("neon_cyan",) * 4, L01, offset=side * (w / 2 - 0.1), lift=-f["thick"] + 0.05)
    sweep(b, path, 0.6, -0.9, ("trim", "concrete", "concrete", "concrete"), L01)          # median barrier
    segment_colliders(b, path, w, f["thick"] + 1.2)
    for lane in (-w / 4 - 1.5, w / 4 + 1.5):
        line = LineString([(p[0], p[2] + lane) for p in path])
        d = 0.0
        while d + 3.0 < line.length:
            a, c = line.interpolate(d), line.interpolate(d + 3.0)
            ya = float(np.interp(a.x, xs, [p[1] for p in path]))
            b.beam((a.x, ya + 0.03, a.y), (c.x, ya + 0.03, c.y), 0.2, "trim", L0)
            d += 12.0
    # T piers where the deck stands high enough.
    for x in np.arange(-ramp + f["pier"], ramp, f["pier"]):
        y = float(np.interp(x, xs, [p[1] for p in path]))
        g = ground(x, fz)
        if y - g < 4.0:
            continue
        top = y - f["thick"]
        b.span(x - 1.5, x + 1.5, g, top - 1.4, fz - 1.5, fz + 1.5, "concrete", ALL)
        b.span(x - 1.8, x + 1.8, top - 1.4, top, fz - w / 2 + 1.0, fz + w / 2 - 1.0, "concrete", L01)
        b.collider(x - 1.5, x + 1.5, g, top - 1.4, fz - 1.5, fz + 1.5)
        b.span(x - 1.55, x + 1.55, g + 3.0, g + 3.4, fz - 1.55, fz + 1.55, "neon_pink", L01)
    # Lights on the barriers.
    for k, (x, y, z) in enumerate(path):
        if k % 4 == 0 and y > 2.0:
            for side in (-1, 1):
                lamp(b, x, y + 1.1, z + side * (w / 2 - 0.3), (0.0, -side), height=5.0)
    return b


# -- City_SkyRoads ------------------------------------------------------------------------------------------------------

SKY = dict(rx=340.0, rz=255.0, base=150.0, swing=30.0, width=14.0, thick=1.6, clear=14.0, spiral_turns=3.0,
           spiral_radius=55.0)


def sky_roads(city, terrain, style, tops):
    """tops: [(footprint polygon, roof height)] of the placed buildings (for clearance and pylon spots)."""
    b = _road_building("City_SkyRoads", style, terrain.values["size"] if terrain else 3000.0)
    s = SKY
    cx, cz = layout_module.RING["cx"], layout_module.RING["cz"]
    count = 160
    points = []
    for k in range(count):
        a = math.tau * k / count
        wiggle = 0.0 if abs(math.sin(a / 2)) < 0.2 else 14.0 * math.sin(3 * a + 1.0)
        x, z = cx + (s["rx"] + wiggle) * math.cos(a), cz + (s["rz"] + wiggle) * math.sin(a)
        y = s["base"] + s["swing"] * math.sin(2 * a + 0.6)
        # Clear of the buildings: over the low ones, round the tall ones (pushed out from the core).
        for _ in range(12):
            here = Point(x, z)
            hits = [top for shape, top in tops if shape.buffer(s["clear"]).contains(here)]
            if not hits:
                break
            top = max(hits)
            if top + s["clear"] < y + 45.0:
                y = max(y, top + s["clear"])
                break
            x, z = x + math.cos(a) * 10.0, z + math.sin(a) * 10.0
        points.append([x, y, z])
    pts = np.array(points)
    for _ in range(3):   # smooth the path and the heights a little (keep the clearance: max with the original)
        smooth = (np.roll(pts, 1, axis=0) + 2 * pts + np.roll(pts, -1, axis=0)) / 4
        smooth[:, 1] = np.maximum(smooth[:, 1], pts[:, 1] * 0.98)
        pts = smooth
    loop = [tuple(p) for p in pts]
    _sky_deck(b, loop, closed=True)
    _pylons(b, loop, tops, city, closed=True)
    # Spiral: round a column on the plaza east of the core; its top joins the loop at the loop's east point.
    east = max(range(count), key=lambda k: loop[k][0] - abs(loop[k][2] - cz) * 0.5)
    ex, ey, ez = loop[east]
    r = s["spiral_radius"]
    centre = (ex + r, ez)
    turns = s["spiral_turns"]
    steps = int(turns * 72)
    spiral = []
    for k in range(steps + 1):
        t = k / steps                                  # 0 at the top, 1 at the bottom
        phi = math.pi + t * turns * math.tau
        y = ey + (1.8 - ey) * t
        spiral.append((centre[0] + r * math.cos(phi), y, centre[1] + r * math.sin(phi)))
    _sky_deck(b, spiral, closed=False)
    b.loop, b.spiral, b.spiral_centre = loop, spiral, (centre[0], ey, centre[1])
    column = 9.0
    b.cylinder((centre[0], 0.0, centre[1]), (centre[0], ey + 6.0, centre[1]), column, "concrete", segments=16, at=L01)
    b.cylinder((centre[0], 0.0, centre[1]), (centre[0], ey + 6.0, centre[1]), column, "concrete", segments=8, at=L2)
    b.round_collider(centre[0], centre[1], column, 0.0, ey + 6.0)
    for k in range(1, int(ey // 30) + 1):
        y = k * 30.0
        b.cylinder((centre[0], y, centre[1]), (centre[0], y + 1.0, centre[1]), column + 0.2,
                   ("neon_cyan", "neon_pink")[k % 2], segments=16, at=L01)
    b.box((centre[0], ey + 6.3, centre[1]), (1.0, 1.0, 1.0), "warning", L01)
    # Spokes from the column to the deck, every 60 degrees.
    for k, (x, y, z) in enumerate(spiral):
        if k % 12 == 6 and y > 8.0:
            ix, iz = centre[0] + (x - centre[0]) * (column / r), centre[1] + (z - centre[1]) * (column / r)
            b.beam((ix, y - 6.0, iz), (x, y - s["thick"], z), 1.2, "concrete")
            b.beam((ix, y - 1.0, iz), ((ix + x) / 2, y - s["thick"], (iz + z) / 2), 0.8, "concrete")
            dx, dz = x - ix, z - iz
            b.colliders.append((((ix + x) / 2, y - 3.8, (iz + z) / 2), (1.6, 5.4, math.hypot(dx, dz)),
                                math.degrees(math.atan2(dx, dz))))
    return b


def _sky_deck(b, path, closed):
    s = SKY
    w = s["width"]
    sweep(b, path, w, s["thick"], ("dark", "concrete", "dark", "concrete"), L01, closed=closed)
    sweep(b, path[::4] + ([] if closed else [path[-1]]), w, s["thick"], ("dark", "concrete", "dark", "concrete"), L2,
          closed=closed)
    for side in (-1, 1):
        sweep(b, path, 0.25, -1.2, ("glass",) * 4, L01, offset=side * (w / 2 - 0.15), closed=closed)
        sweep(b, path, 0.3, 0.2, ("neon_cyan",) * 4, L01, offset=side * (w / 2 - 0.15), lift=1.35, closed=closed)
    sweep(b, path, 0.8, 0.12, ("neon_pink",) * 4, L01, lift=-s["thick"] + 0.06, closed=closed)
    segment_colliders(b, path, w, s["thick"] + 1.4, closed=closed, step=1)
    pairs = list(zip(path, path[1:]))
    for k, (a, c) in enumerate(pairs):
        if k % 2 == 0:
            b.beam((a[0], a[1] + 0.03, a[2]), ((a[0] + c[0]) / 2, (a[1] + c[1]) / 2 + 0.03, (a[2] + c[2]) / 2), 0.2,
                   "trim", L0)


def _pylons(b, path, tops, city, closed):
    """Columns under the loop where the ground is free (not in a building or the channel), about every 70 m."""
    spacing, run, last = 70.0, 0.0, -1e9
    for k, (x, y, z) in enumerate(path):
        if k:
            px, _py, pz = path[k - 1]
            run += math.hypot(x - px, z - pz)
        if run - last < spacing:
            continue
        here = Point(x, z)
        if any(shape.buffer(4.0).contains(here) for shape, _top in tops) or city.channel.buffer(3.0).contains(here):
            continue
        last = run
        top = y - SKY["thick"]
        b.cylinder((x, 0.0, z), (x, top - 2.0, z), 2.2, "concrete", segments=10, at=L01)
        b.cylinder((x, 0.0, z), (x, top - 2.0, z), 2.2, "concrete", segments=6, at=L2)
        b.span(x - 3.5, x + 3.5, top - 2.0, top, z - 3.5, z + 3.5, "concrete", L01)
        b.cylinder((x, top * 0.5, z), (x, top * 0.5 + 0.8, z), 2.35, "neon_cyan", segments=10, at=L01)
        b.round_collider(x, z, 2.2, 0.0, top)


# -- City_Utilities -----------------------------------------------------------------------------------------------------
# What ties the city together: pipe racks along the streets between the industrial plots (crossing the streets as pipe
# bridges), power lines along the roads out of the city, parked trucks and street clutter, covered sky walks between
# neighbouring tall buildings.

RACK = dict(height=7.0, frame=8.0, reach=260.0, front=14.0)
PYLON = dict(spacing=95.0, offset=24.0, height=26.0)


def utilities(city, terrain, style, tops=(), wall_hit=None):
    b = _road_building("City_Utilities", style, terrain.values["size"] if terrain else 3000.0)
    b.ground_level = -60.0
    industrial = [shape for name, shape in city.footprints if city.specs[name]["type"] == "industrial"]
    keep_out = city.channel.buffer(12.0)
    racks = _pipe_racks(b, city, industrial, keep_out)
    b.racks = racks             # for the preview shot
    _street_clutter(b, city, racks, keep_out)
    if wall_hit is not None:
        _sky_walks(b, city, tops, wall_hit)
    if terrain is not None:
        _power_lines(b, terrain)
    return b


WALK = dict(gap=(6.0, 32.0), low=22.0, high=110.0, most=2, chance=0.55, width=3.2, height=3.4)


def _sky_walks(b, city, tops, wall_hit):
    """Covered footbridges between tall neighbours (towers, blocks) across a street: from wall to wall where the two
    face each other along at least 6 m, at a random height between 22 m and 110 m (under the sky roads, over the
    freeway and the racks), at most 2 per building, never through a third building. Both ends are found on the real
    walls (wall_hit: a ray against the building's mesh), so a tower that steps back gets a longer walk, not a gap."""
    w = WALK
    rng = b.rng
    tall = [(k, shape, top) for k, ((name, _p, _y), (shape, top)) in enumerate(zip(city.instances, tops))
            if city.specs[name]["type"] in ("tower", "block") and top > w["low"] + 8.0]
    count = [0] * len(tall)
    every = [shape for shape, _top in tops]
    for i, (ka, a, top_a) in enumerate(tall):
        for j in range(i + 1, len(tall)):
            kb, b_shape, top_b = tall[j]
            if count[i] >= w["most"] or count[j] >= w["most"]:
                continue
            gap = a.distance(b_shape)
            if not w["gap"][0] < gap < w["gap"][1] or rng.random() > w["chance"]:
                continue
            strip = a.buffer(gap + 2.0).intersection(b_shape)
            if strip.is_empty or strip.area < 12.0:      # they only touch at a corner
                continue
            centre = strip.centroid
            p = a.exterior.interpolate(a.exterior.project(centre))
            q = b_shape.exterior.interpolate(b_shape.exterior.project(p))
            span = LineString([(p.x, p.y), (q.x, q.y)])
            if span.length < 3.0 or any(other is not a and other is not b_shape and other.intersects(span.buffer(w["width"]))
                                        for other in every):
                continue
            top = min(top_a, top_b, w["high"])
            y = max(w["low"], rng.uniform(0.35, 0.75) * top)
            if y + w["height"] > top - 3.0:
                continue
            # The real walls: rays from the middle of the gap, out to each building at deck height + 1.5 m.
            ux, uz = (q.x - p.x) / span.length, (q.y - p.y) / span.length
            mid = ((p.x + q.x) / 2, y + 1.5, (p.y + q.y) / 2)
            end_a = wall_hit(ka, mid, (-ux, 0.0, -uz), span.length / 2 + 15.0)
            end_b = wall_hit(kb, mid, (ux, 0.0, uz), span.length / 2 + 15.0)
            if end_a is None or end_b is None or math.dist(end_a, end_b) > w["gap"][1] + 10.0:
                continue
            _walk(b, (end_a[0], end_a[2]), (end_b[0], end_b[2]), y)
            count[i] += 1
            count[j] += 1


def _walk(b, p, q, y):
    """A glass tube on a concrete deck, steel ribs every 3 m, a neon strip under it; ends 0.5 m into the walls."""
    w, h = WALK["width"], WALK["height"]
    dx, dz = q[0] - p[0], q[1] - p[1]
    length = math.hypot(dx, dz) + 1.0
    mx, mz = (p[0] + q[0]) / 2, (p[1] + q[1]) / 2
    with b.turned(mx, mz, math.degrees(math.atan2(dx, dz))):
        z0, z1 = mz - length / 2, mz + length / 2
        b.span(mx - w / 2, mx + w / 2, y - 0.6, y, z0, z1, "concrete", L01)
        b.span(mx - w / 2, mx + w / 2, y - 0.6, y + h, z0, z1, "concrete", (2,))
        for side in (-1, 1):
            b.span(mx + side * (w / 2 - 0.05) - 0.05, mx + side * (w / 2 - 0.05) + 0.05, y, y + h, z0, z1, "glass", L01)
        b.span(mx - w / 2 - 0.1, mx + w / 2 + 0.1, y + h, y + h + 0.25, z0, z1, "metal", L01)
        k = z0 + 1.5
        while k < z1 - 1.0:
            for side in (-1, 1):
                b.span(mx + side * w / 2 - 0.12, mx + side * w / 2 + 0.12, y, y + h, k - 0.12, k + 0.12, "trim", L01)
            k += 3.0
        b.span(mx - 0.4, mx + 0.4, y - 0.66, y - 0.6, z0 + 0.5, z1 - 0.5, "neon_cyan", L01)
        b.span(mx - w / 2 + 0.3, mx + w / 2 - 0.3, y + h - 0.1, y + h, z0 + 0.5, z1 - 0.5, b.lit_swatch(), L0)
        b.collider(mx - w / 2, mx + w / 2, y - 0.6, y + h + 0.25, z0, z1)


def _side_line(line, offset):
    """The line moved sideways (shapely offset_curve; + = left of the direction), as one LineString or None."""
    moved = line.offset_curve(offset)
    parts = [p for p in layout_module._parts(moved) if p.geom_type == "LineString" and p.length > 1.0]
    return max(parts, key=lambda p: p.length) if parts else None


def _pipe_racks(b, city, industrial, keep_out):
    """For every street: the industrial plots that front it; a two-tier rack on one side of the street from plot to
    plot (when they are close), and a spur from the rack to each plot (over the street when the plot is across it).
    Returns the rack lines (for keeping clutter away)."""
    lines = []
    for road in city.roads:
        if road.kind not in ("street", "frontage", "perimeter", "radial"):
            continue
        line = road.line.intersection(city.rect.buffer(-2.0)).difference(keep_out)
        for part in layout_module._parts(line):
            if part.geom_type != "LineString" or part.length < 30.0:
                continue
            fronting = [(part.project(shape.centroid), shape) for shape in industrial
                        if shape.distance(part) < road.width / 2 + RACK["front"]]
            if len(fronting) < 2:
                continue
            fronting.sort(key=lambda item: item[0])
            # The rack side: where most of the plots are.
            left = sum(1 if _side(part, shape.centroid) > 0 else -1 for _a, shape in fronting)
            sign = 1.0 if left >= 0 else -1.0
            offset = sign * (road.width / 2 - 1.3)
            rail = _side_line(part, offset)
            if rail is None:
                continue
            for (a0, s0), (a1, s1) in zip(fronting, fronting[1:]):
                if a1 - a0 < 8.0 or a1 - a0 > RACK["reach"]:
                    continue
                piece = shapely.ops.substring(rail, rail.project(part.interpolate(a0)), rail.project(part.interpolate(a1)))
                if piece.geom_type != "LineString" or piece.length < 8.0:
                    continue
                _rack(b, list(piece.coords))
                lines.append(piece)
            for along, shape in fronting:
                _spur(b, rail, part.interpolate(along), shape)
    return lines


def _side(line, point):
    """> 0 when the point is left of the line (looking along it)."""
    along = line.project(point)
    a, c = line.interpolate(max(0.0, along - 1.0)), line.interpolate(min(line.length, along + 1.0))
    return (c.x - a.x) * (point.y - a.y) - (c.y - a.y) * (point.x - a.x)


def _rack_pipes(b, p0, p1, y):
    """Pipes on a two-tier rack between two ground points (x, z): 3 low (big), 2 high (small), across offsets."""
    (x0, z0), (x1, z1) = p0, p1
    length = math.hypot(x1 - x0, z1 - z0)
    if length < 0.5:
        return
    px, pz = -(z1 - z0) / length, (x1 - x0) / length
    for offset, radius, lift, swatch in ((-0.7, 0.38, 0.0, "metal"), (0.0, 0.28, 0.0, "steel"),
                                         (0.7, 0.32, 0.0, "rust"), (-0.45, 0.18, 1.3, "trim"),
                                         (0.45, 0.22, 1.3, "metal")):
        top = y + lift + radius
        b.cylinder((x0 + px * offset, top, z0 + pz * offset), (x1 + px * offset, top, z1 + pz * offset), radius,
                   swatch, segments=8, at=L01)
    b.cylinder((x0, y + 0.4, z0), (x1, y + 0.4, z1), 0.9, "metal", segments=4, at=(2,))


def _rack(b, coords):
    """A pipe rack along ground points: T frames every RACK["frame"] m, two cross beams, pipes per straight piece."""
    y = RACK["height"]
    line = LineString(coords)
    for (x0, z0), (x1, z1) in zip(coords, coords[1:]):
        _rack_pipes(b, (x0, z0), (x1, z1), y)
        length = math.hypot(x1 - x0, z1 - z0)
        if length > 0.5:
            yaw = math.degrees(math.atan2(x1 - x0, z1 - z0))
            b.colliders.append((((x0 + x1) / 2, y + 0.85, (z0 + z1) / 2), (2.2, 1.9, length), yaw))
    d = 0.0
    while d <= line.length:
        p = line.interpolate(d)
        q = line.interpolate(min(line.length, d + 1.0)) if d + 1.0 <= line.length else line.interpolate(d - 1.0)
        dx, dz = q.x - p.x, q.y - p.y
        n = math.hypot(dx, dz) or 1.0
        rx, rz = dz / n, -dx / n
        b.cylinder((p.x, 0.0, p.y), (p.x, y + 1.3, p.y), 0.22, "steel", segments=6, at=L01)
        b.span(p.x - 0.4, p.x + 0.4, 0.0, 0.3, p.y - 0.4, p.y + 0.4, "concrete", L01)
        for level in (y - 0.15, y + 1.15):
            b.beam((p.x - rx * 1.1, level, p.y - rz * 1.1), (p.x + rx * 1.1, level, p.y + rz * 1.1), 0.2, "steel", L01)
        b.colliders.append(((p.x, (y + 1.3) / 2, p.y), (0.5, y + 1.3, 0.5), 0.0))
        d += RACK["frame"]


def _spur(b, rail, at, shape):
    """From the rack (nearest point) straight to the plot edge, then a drop to a ground manifold."""
    start = rail.interpolate(rail.project(at))
    edge = shape.exterior.interpolate(shape.exterior.project(start))
    dx, dz = edge.x - start.x, edge.y - start.y
    length = math.hypot(dx, dz)
    if length < 1.0 or length > 40.0:
        return
    ux, uz = dx / length, dz / length
    end = (edge.x - ux * 1.0, edge.y - uz * 1.0)      # stop 1 m short of the plot
    y = RACK["height"] + 0.45
    b.cylinder((start.x, y, start.y), (end[0], y, end[1]), 0.3, "metal", segments=8, at=L01)
    b.cylinder((end[0], 0.6, end[1]), (end[0], y + 0.3, end[1]), 0.3, "metal", segments=8, at=L01)
    b.span(end[0] - 0.8, end[0] + 0.8, 0.0, 1.2, end[1] - 0.8, end[1] + 0.8, "primer", L01)
    b.box((end[0], 1.4, end[1]), (0.5, 0.3, 0.5), "hazard", L01)          # valve wheel housing
    if length > 3.0:
        # A post halfway, so long spurs over a street do not hang free.
        mx, mz = (start.x + end[0]) / 2, (start.y + end[1]) / 2
        b.cylinder((mx, 0.0, mz), (mx, y - 0.3, mz), 0.18, "steel", segments=6, at=L01)
    yaw = math.degrees(math.atan2(ux, uz))
    b.colliders.append((((start.x + end[0]) / 2, y, (start.y + end[1]) / 2), (0.8, 0.8, length), yaw))


def _street_clutter(b, city, racks, keep_out):
    """Parked trucks, tankers, vans, dumpsters and barriers in the kerb lane of the small streets, on the side
    without a rack, kept off junctions."""
    rng = b.rng
    rack_zone = shapely.unary_union([line.buffer(3.0) for line in racks]) if racks else None
    junctions = shapely.unary_union([r.line for r in city.roads if r.kind not in ("street", "frontage")]).buffer(16.0)
    blocked = keep_out.union(junctions)
    if rack_zone is not None:
        blocked = blocked.union(rack_zone)
    footprints = shapely.unary_union([shape for _n, shape in city.footprints]).buffer(0.8)
    kinds = ("truck", "tanker", "van", "van", "dumpster", "barrier", "barrier")
    for road in city.roads:
        if road.kind not in ("street", "frontage"):
            continue
        pieces = [part for part in layout_module._parts(road.line.intersection(city.rect.buffer(-4.0)))
                  if part.geom_type == "LineString" and part.length > 20.0]
        for piece, sign in [(piece, sign) for piece in pieces for sign in (-1.0, 1.0)]:
            lane = _side_line(piece, sign * (road.width / 2 - 1.6))
            if lane is None:
                continue
            d = rng.uniform(5.0, 25.0)
            while d < lane.length - 8.0:
                p = lane.interpolate(d)
                q = lane.interpolate(d + 1.0)
                kind = rng.choice(kinds)
                if not blocked.contains(p) and not footprints.contains(p):
                    yaw = math.degrees(math.atan2(q.x - p.x, q.y - p.y)) + (180.0 if rng.random() < 0.5 else 0.0)
                    kit.vehicle(b, kind, p.x, p.y, yaw)
                d += rng.uniform(14.0, 45.0)


def _power_lines(b, terrain):
    """Lattice pylons beside the roads out of the city (outside the city floor), three sagging cables between them.
    Pylons stand off steep ground and lava; cables only between neighbours that both stand."""
    from .terrain import CITY_FLAT
    p = PYLON
    for road in terrain.roads:
        line = LineString(road["points"])
        rail = _side_line(line, -(road["width"] / 2 + p["offset"]))
        if rail is None:
            continue
        previous = None
        d = 30.0
        while d < rail.length:
            pt = rail.interpolate(d)
            x, z = pt.x, pt.y
            half = terrain.values["size"] / 2 - 40.0
            ok = abs(x) < half and abs(z) < half and terrain._city_distance(x, z) > CITY_FLAT + 20.0
            if ok:
                ys = [float(terrain.sample(x + dx, z + dz)) for dx, dz in ((0, 0), (4, 0), (-4, 0), (0, 4), (0, -4))]
                ok = max(ys) - min(ys) < 3.0 and float(terrain.lava_distance(np.array(x), np.array(z))) > 12.0
            if ok:
                q = rail.interpolate(min(rail.length, d + 1.0))
                yaw = math.degrees(math.atan2(q.x - x, q.y - z))
                ground = min(ys)
                arms = _pylon(b, x, ground, z, yaw)
                if previous is not None and math.hypot(x - previous[2][0], z - previous[2][2]) < p["spacing"] * 1.6:
                    for a, c in zip(previous, arms):
                        _cable(b, a, c)
                previous = arms
            else:
                previous = None
            d += p["spacing"]


def _pylon(b, x, y, z, yaw):
    """A tapered lattice pylon (4 legs, cross braces) with two cross arms; returns the 3 cable points."""
    h = PYLON["height"]
    arms = []
    with b.turned(x, z, yaw):
        base, top = 3.0, 0.9
        corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
        for sx, sz in corners:
            b.beam((x + sx * base, y, z + sz * base), (x + sx * top, y + h, z + sz * top), 0.25, "steel", L01)
            b.span(x + sx * base - 0.5, x + sx * base + 0.5, y - 0.5, y + 0.4, z + sz * base - 0.5,
                   z + sz * base + 0.5, "concrete", L01)
        for k in range(4):
            y0, y1 = y + h * k / 4, y + h * (k + 1) / 4
            w0, w1 = base + (top - base) * k / 4, base + (top - base) * (k + 1) / 4
            for (ax, az), (cx, cz) in zip(corners, corners[1:] + corners[:1]):
                b.beam((x + ax * w0, y0, z + az * w0), (x + cx * w1, y1, z + cz * w1), 0.12, "steel", (0,))
        b.span(x - 0.9, x + 0.9, y, y + h, z - 0.9, z + 0.9, "steel", (2,))
        for level, reach in ((h - 5.0, 7.0), (h - 0.5, 4.5)):
            b.span(x - reach, x + reach, y + level - 0.3, y + level + 0.3, z - 0.3, z + 0.3, "steel", L01)
        b.box((x, y + h + 0.4, z), (0.5, 0.5, 0.5), "warning", L01)
        b.collider(x - 3.2, x + 3.2, y - 0.5, y + h + 0.7, z - 3.2, z + 3.2)
        for level, reach in ((h - 5.0, 7.0), (h - 0.5, 4.5)):
            b.collider(x - reach, x + reach, y + level - 0.4, y + level + 0.4, z - 0.4, z + 0.4)
    a = math.radians(yaw)       # local +X turns to (cos, -sin) for a Unity yaw
    for lx, ly in ((-6.5, h - 5.6), (6.5, h - 5.6), (0.0, h - 1.1)):
        arms.append((x + lx * math.cos(a), y + ly, z - lx * math.sin(a)))
    return arms


def _cable(b, a, c, sag=3.5, pieces=6):
    """A sagging cable (parabola) of short thin bars."""
    points = []
    for k in range(pieces + 1):
        t = k / pieces
        points.append((a[0] + (c[0] - a[0]) * t, a[1] + (c[1] - a[1]) * t - sag * 4 * t * (1 - t),
                       a[2] + (c[2] - a[2]) * t))
    for p0, p1 in zip(points, points[1:]):
        b.beam(p0, p1, 0.08, "dark", L01)
        # Wires are solid: a chopper that flies into them hits them.
        dx, dy, dz = p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]
        b.colliders.append((((p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2, (p0[2] + p1[2]) / 2),
                            (0.4, 0.4 + abs(dy), math.hypot(dx, dz)), math.degrees(math.atan2(dx, dz))))
