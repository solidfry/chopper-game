"""
Traffic paths for the game's "life" layer (realism list item 5): where ground cars, sky-road cars, flying cars and
drones move. CityForge only writes the paths ("traffic" in <kit>.terrain.json); Unity moves cosmetic vehicles along
them (client only, no colliders, no network).

Ground lanes (one per direction, right-hand traffic, at the lane centre):
- city streets and main roads (cut where they cross the channel away from a bridge);
- the roads out of the city to the tunnel portals and the canyon ("highway");
- the elevated freeway (two lanes each way);
- the sky-road loop (closed) and its spiral ramp.
Air routes (Unity space, checked against the finished meshes):
- flying-car lanes over the main roads at a few heights, one direction per height, lifted where something is near;
- drone loops round the tallest buildings, dropped where they would pass too near anything.

Every path: kind, closed, speed (m/s), points (flat [x, y, z, x, y, z, ...]: Unity's JsonUtility cannot read nested
arrays; about SPACING apart). Kinds: street, main, highway,
freeway, sky, spiral, air, drone.

Models (models()): Traffic_Car, Traffic_Truck, Traffic_Flyer (flying car), Traffic_Drone: small Buildings in the kit
palette, exported like the buildings (FBX, LODs) but not placed in the layout and without colliders; Unity draws them
instanced along the paths. Origin = bottom centre, +Z = forward. Head lights Dream Cyan, tail lights Cruise Pink
(both glow), drones blink red.
"""
import math

import numpy as np
from shapely.geometry import LineString
from shapely.ops import unary_union

from . import layout as layout_module
from .kit import L01, L2, Building

SPACING = 10.0
SPEED = dict(street=11.0, main=16.0, highway=24.0, freeway=30.0, sky=22.0, spiral=9.0, air=34.0, drone=12.0)
MAIN = ("ring", "radial", "boulevard", "perimeter")
LANE_Y = 0.15                               # on the street surface (0.05) and the bridges (0.15)
SHORTEST = 30.0                             # lanes shorter than this are left out
AIR = dict(roads=("ring", "radial", "boulevard"), heights=(55.0, 95.0, 135.0), clear=10.0, lift_step=5.0,
           lift_most=60.0)
DRONES = dict(count=16, clear=5.0, gap=16.0, points=28)
SIGNALS = dict(before=2.0)   # stop line this far (m) before the crossing road's edge
SEARCHLIGHTS = dict(tall=150.0, most_tall=8, most_industrial=6, colours=("#dfe8ff", "#dfe8ff", "#dfe8ff", "#00f0ff",
                                                                          "#ff4289"))


def paths(state, clearance):
    """All traffic paths of a generated city. clearance(points, most) -> distance from each Unity point to the nearest
    city mesh (buildings and city parts), at most `most`."""
    city, t = state.layout, state.terrain
    if city is None or t is None:
        return None
    lanes = _street_lanes(city) + _highway_lanes(t)
    parts = {name: state.built[name]["building"] for name in ("City_Freeway", "City_SkyRoads") if name in state.built}
    if "City_Freeway" in parts:
        lanes += _freeway_lanes(parts["City_Freeway"])
    if "City_SkyRoads" in parts:
        lanes += _sky_lanes(parts["City_SkyRoads"])
    air = _air_lanes(city, t, clearance) + _drone_loops(city, state.building_tops, t, clearance)
    lights = _searchlights(city, state.building_tops)
    return {
        "note": "Cosmetic traffic, client only. Unity space, metres. Lanes: one per direction, right-hand traffic, "
                "points (flat x, y, z list) about 10 m apart along the lane centre. closed = a loop (the last point joins the first). "
                "Open lanes: vehicles leave at the end and come back at the start.",
        "lanes": [_record(lane) for lane in lanes],
        "air": [_record(route) for route in air],
        "signals": {"note": "Lane stops (flat list): distance along the lane (m), phase (0 or 1). The two roads at a "
                            "junction have opposite phases. Cycle and green times: Unity TrafficSettings."},
        "searchlights": lights,
        "stats": {kind: sum(1 for p in lanes + air if p["kind"] == kind) for kind in SPEED},
    }


def _record(path):
    return {"kind": path["kind"], "closed": path["closed"], "speed": SPEED[path["kind"]],
            "points": [round(float(v), 2) for p in path["points"] for v in p],
            "stops": [round(float(v), 2) for stop in path.get("stops", ()) for v in stop]}


# -- ground ------------------------------------------------------------------------------------------------------------

def _street_lanes(city):
    bridges = unary_union([layout_module.footprint(x, z, width + 2.0, 64.0, yaw) for x, z, yaw, width in city.bridges]) \
        if city.bridges else None
    water = city.channel.difference(bridges) if bridges is not None else city.channel
    out = []
    for road in city.roads:
        kind = "main" if road.kind in MAIN else "street"
        clipped = road.line.intersection(city.rect).difference(water)
        closed = road.closed and clipped.equals(road.line)
        for part in layout_module._parts(clipped):
            if part.geom_type != "LineString" or part.length < SHORTEST:
                continue
            points = [(x, LANE_Y, z) for x, z in _spaced(part, closed)]
            lanes = _both_ways(points, road.width / 4.0, kind, closed)
            if len(lanes) == 2:
                forward, back = _junctions(part, road, city)
                lanes[0]["stops"] = _on_lane(lanes[0], [(part.interpolate(d), phase) for d, phase in forward], closed)
                lanes[1]["stops"] = _on_lane(lanes[1], [(part.interpolate(d), phase) for d, phase in back], closed)
            out += lanes
    return out


def _junctions(part, road, city):
    """Stop lines on a road part, before every road it meets (crossings and T junctions): (distance along the part
    where a vehicle going forward stops, phase) and the same for the way back (as a distance along the part).
    Main roads have priority: they do not stop where a small street meets them."""
    forward, back = [], []
    before = SIGNALS["before"]
    for other in city.roads:
        if other is road or (road.kind in MAIN and other.kind not in MAIN):
            continue
        inside = part.intersection(other.line.buffer(other.width / 2 + 1.0))
        for piece in layout_module._parts(inside):
            if piece.geom_type != "LineString" or piece.length < 0.5:
                continue
            ends = [part.project(_point(*piece.coords[0])), part.project(_point(*piece.coords[-1]))]
            low, high = min(ends), max(ends)
            phase = _phase(part, (low + high) / 2, other.line)
            if low - before > 1.0:
                forward.append((low - before, phase))
            if part.length - (high + before) > 1.0:
                back.append((high + before, phase))
    return forward, back


def _phase(line, at, other):
    """Signal phase of a road at a junction: the road whose direction (mod 180 degrees) is smaller goes first."""
    def angle(geometry, distance):
        p, q = geometry.interpolate(max(0.0, distance - 2.0)), geometry.interpolate(min(geometry.length, distance + 2.0))
        return math.atan2(q.y - p.y, q.x - p.x) % math.pi
    here = line.interpolate(at)
    return 0 if angle(line, at) < angle(other, other.project(here)) else 1


def _on_lane(lane, stops, closed):
    """(distance along the lane, phase) for stop points given on the centre line: projected onto the lane itself
    (offset lanes on a curve are longer or shorter than the centre line), sorted."""
    xz = [(p[0], p[2]) for p in lane["points"]]
    line = LineString(xz + ([xz[0]] if closed else []))
    return sorted((line.project(point), phase) for point, phase in stops)


def _highway_lanes(terrain):
    out = []
    half = terrain.values["size"] / 2 - 20.0
    for road in terrain.roads:
        line = LineString(road["points"])
        runs = np.concatenate([[0.0], np.cumsum([math.hypot(q[0] - p[0], q[1] - p[1])
                                                 for p, q in zip(road["points"], road["points"][1:])])])
        points = []
        for x, z in _spaced(line, False):
            if abs(x) > half or abs(z) > half:
                break
            y = float(np.interp(line.project(_point(x, z)), runs, road["heights"]))
            points.append((x, y + 0.25, z))
        if len(points) * SPACING >= SHORTEST:
            out += _both_ways(points, road["width"] / 4.0, "highway", False)
    return out


def _freeway_lanes(b):
    path, width = getattr(b, "path", None), getattr(b, "deck_width", None)
    if not path:
        return []
    points = _resample3(path, False)
    # Each carriageway is half the deck (median in the middle): two lanes each, inside the lane-line dashes.
    return [lane for offset in (width * 0.17, width * 0.4) for lane in _both_ways(points, offset, "freeway", False, lift=0.05)]


def _sky_lanes(b):
    out = []
    width = 14.0
    if getattr(b, "loop", None):
        out += _both_ways(_resample3(b.loop, True), width / 4.0, "sky", True, lift=0.05)
    if getattr(b, "spiral", None):
        out += _both_ways(_resample3(b.spiral, False), width / 4.0, "spiral", False, lift=0.05)
    return out


def _both_ways(points, offset, kind, closed, lift=0.0):
    """Two lanes from a centre line: forward on its right, back on the other side (reversed)."""
    if len(points) < 2:
        return []
    forward = _offset(points, offset, closed, lift)
    back = _offset(points[::-1], offset, closed, lift)
    return [dict(kind=kind, closed=closed, points=forward), dict(kind=kind, closed=closed, points=back)]


def _offset(points, offset, closed, lift):
    """The line moved `offset` to the right of its direction of travel (Unity: right of +Z is +X)."""
    p = np.asarray(points, dtype=float)
    ahead = np.roll(p, -1, axis=0) if closed else np.vstack([p[1:], 2 * p[-1] - p[-2]])
    behind = np.roll(p, 1, axis=0) if closed else np.vstack([2 * p[0] - p[1], p[:-1]])
    tangent = ahead - behind
    tx, tz = tangent[:, 0], tangent[:, 2]
    length = np.maximum(np.hypot(tx, tz), 1e-6)
    right = np.stack([tz / length, np.zeros_like(tx), -tx / length], axis=1)
    moved = p + right * offset
    moved[:, 1] += lift
    return [tuple(v) for v in moved]


def _spaced(line, closed):
    """(x, z) points along a 2D line about SPACING apart (a closed line without its repeated end)."""
    count = max(2, int(round(line.length / SPACING)) + (0 if closed else 1))
    ds = np.linspace(0.0, line.length, count, endpoint=not closed)
    return [(p.x, p.y) for p in (line.interpolate(d) for d in ds)]


def _resample3(points, closed):
    """3D points about SPACING apart along the path (by length in 3D)."""
    p = np.asarray(points, dtype=float)
    if closed:
        p = np.vstack([p, p[:1]])
    runs = np.concatenate([[0.0], np.cumsum(np.linalg.norm(np.diff(p, axis=0), axis=1))])
    count = max(2, int(round(runs[-1] / SPACING)) + (0 if closed else 1))
    ds = np.linspace(0.0, runs[-1], count, endpoint=not closed)
    return [tuple(np.interp(d, runs, p[:, k]) for k in range(3)) for d in ds]


def _point(x, z):
    from shapely.geometry import Point
    return Point(x, z)


# -- air ---------------------------------------------------------------------------------------------------------------

def _air_lanes(city, terrain, clearance):
    """Flying-car lanes over the main roads: one direction per height (the next height flies the other way). Where
    anything is nearer than `clear`, the lane climbs (up to lift_most); where it still is, the lane is cut there."""
    a = AIR
    out = []
    for road in city.roads:
        if road.kind not in a["roads"]:
            continue
        clipped = road.line.intersection(city.rect)
        closed = road.closed and clipped.equals(road.line)
        for part in layout_module._parts(clipped):
            if part.geom_type != "LineString" or part.length < 120.0:
                continue
            flat = _spaced(part, closed)
            for k, height in enumerate(a["heights"]):
                xz = flat if k % 2 == 0 else flat[::-1]
                ground = terrain.sample(np.array([p[0] for p in xz]), np.array([p[1] for p in xz]))
                ys = np.maximum(np.full(len(xz), height), ground + a["clear"] + 5.0)
                ys, good = _lift(xz, ys, clearance, a["clear"], a["lift_step"], a["lift_most"])
                ys = _soften(ys, closed)
                # Softening only raises points: check again (a raised point can meet a sky bridge).
                good &= clearance([(xz[i][0], ys[i], xz[i][1]) for i in range(len(xz))], a["clear"] * 2) >= a["clear"] * 0.8
                for run in _runs(good, closed):
                    points = [(xz[i][0], float(ys[i]), xz[i][1]) for i in run]
                    if len(points) * SPACING >= 120.0:
                        out.append(dict(kind="air", closed=closed and len(run) == len(xz), points=points))
    return out


def _drone_loops(city, tops, terrain, clearance):
    """Loops round the tallest buildings, at 35 to 80 % of their height, a gap out from the footprint. A loop that
    passes too near anything (a sky bridge, a neighbour) is left out."""
    d = DRONES
    rng = np.random.default_rng(7)
    ranked = sorted(range(len(tops)), key=lambda k: -tops[k][1])[:d["count"]]
    out = []
    for k in ranked:
        shape, top = tops[k]
        centre = shape.centroid
        radius = max(math.hypot(x - centre.x, z - centre.y) for x, z in shape.exterior.coords) + d["gap"]
        height = top * float(rng.uniform(0.35, 0.8))
        angles = np.linspace(0.0, math.tau, d["points"], endpoint=False)
        if rng.random() < 0.5:
            angles = angles[::-1]
        points = [(centre.x + radius * math.cos(a), height + 4.0 * math.sin(3 * a), centre.y + radius * math.sin(a))
                  for a in angles]
        near = clearance(points, d["clear"] * 2)
        ground = terrain.sample(np.array([p[0] for p in points]), np.array([p[2] for p in points]))
        if (near >= d["clear"]).all() and (np.array([p[1] for p in points]) - ground >= d["clear"]).all():
            out.append(dict(kind="drone", closed=True, points=points))
    return out


def _lift(xz, ys, clearance, clear, step, most):
    """Raise each point until it is `clear` from every mesh (at most `most` higher). Returns heights and a mask of
    the points that made it."""
    ys = ys.copy()
    good = np.zeros(len(xz), dtype=bool)
    todo = np.arange(len(xz))
    lift = 0.0
    while len(todo) and lift <= most:
        near = clearance([(xz[i][0], ys[i] + lift, xz[i][1]) for i in todo], clear * 2)
        ok = near >= clear
        ys[todo[ok]] += lift
        good[todo[ok]] = True
        todo = todo[~ok]
        lift += step
    return ys, good


def _soften(ys, closed, reach=4):
    """No steps: each height is the highest within `reach` points, then eased; never below the lifted height."""
    n = len(ys)
    index = np.arange(n)
    windows = [(index + k) % n if closed else np.clip(index + k, 0, n - 1) for k in range(-reach, reach + 1)]
    peak = np.max(np.stack([ys[w] for w in windows]), axis=0)
    eased = np.mean(np.stack([peak[w] for w in windows]), axis=0)
    return np.maximum(eased, ys)


def _runs(good, closed):
    """Index runs of good points (a closed path that is all good is one run in order)."""
    n = len(good)
    if good.all():
        return [list(range(n))]
    runs, current = [], []
    start = int(np.argmin(good)) if closed else 0         # a closed path starts after a bad point
    for step in range(n):
        i = (start + step) % n if closed else step
        if good[i]:
            current.append(i)
        elif current:
            runs.append(current)
            current = []
    if current:
        runs.append(current)
    return runs


# -- models ------------------------------------------------------------------------------------------------------------

MODEL_NAMES = ("Traffic_Car", "Traffic_Truck", "Traffic_Flyer", "Traffic_Drone")
MODEL_BUDGETS = dict(lod0=3000, lod1=0.95, lod2=400)


def models(style):
    """The traffic vehicles (see the module notes)."""
    out = []
    for name, make in zip(MODEL_NAMES, (_car, _truck, _flyer, _drone)):
        b = Building(dict(name=name, type="traffic", seed=sum(map(ord, name)), plot=(6.0, 12.0)), style)
        b.budgets = MODEL_BUDGETS
        b.skip_uncovered = True              # no colliders: cosmetic, client only
        b.skip_floating = name in ("Traffic_Flyer", "Traffic_Drone")    # they fly
        make(b)
        out.append(b)
    return out


def _car(b):
    """A low city car: body, cabin, glass, four wheels, cyan head-light bar, pink tail bar."""
    colour = b.rng.choice(("primer", "trim", "steel", "wall"))
    b.span(-0.95, 0.95, 0.35, 0.95, -2.2, 2.2, colour, L01)                     # body
    b.span(-0.85, 0.85, 0.95, 1.45, -1.2, 0.9, colour, L01)                     # cabin
    b.span(-0.8, 0.8, 1.0, 1.4, 0.88, 0.95, "glass", L01)                       # windscreen
    b.span(-0.8, 0.8, 1.0, 1.4, -1.25, -1.18, "glass", L01)                     # rear window
    for z in (-1.4, 1.4):
        for side in (-1, 1):
            b.cylinder((side * 0.8, 0.35, z), (side * 1.0, 0.35, z), 0.35, "rubber", segments=8, at=L01)
    b.span(-0.8, 0.8, 0.65, 0.8, 2.2, 2.26, "neon_cyan", L01)                    # head lights
    b.span(-0.85, 0.85, 0.7, 0.85, -2.26, -2.2, "neon_pink", L01)                # tail lights
    b.span(-0.95, 0.95, 0.0, 1.45, -2.25, 2.25, colour, L2)


def _truck(b):
    """A box truck: cab, cargo box, wheels, amber roof lights, cyan head lights, pink tail lights."""
    b.span(-1.2, 1.2, 0.5, 1.0, -4.5, 4.4, "dark", L01)                         # chassis
    b.span(-1.25, 1.25, 1.0, 3.2, 2.4, 4.4, b.rng.choice(("hazard", "primer", "steel")), L01)   # cab
    b.span(-1.1, 1.1, 2.2, 3.0, 4.38, 4.45, "glass", L01)
    b.span(-1.3, 1.3, 1.0, 3.8, -4.6, 2.3, b.rng.choice(("wall", "rust", "trim")), L01)        # cargo box
    for z in (3.4, -1.8, -3.8):
        for side in (-1, 1):
            b.cylinder((side * 1.0, 0.5, z), (side * 1.3, 0.5, z), 0.5, "rubber", segments=8, at=L01)
    b.span(-1.0, 1.0, 3.2, 3.35, 2.8, 4.0, "sodium", L01)                        # roof light bar
    b.span(-1.0, 1.0, 1.2, 1.4, 4.4, 4.48, "neon_cyan", L01)
    b.span(-1.2, 1.2, 1.1, 1.3, -4.68, -4.6, "neon_pink", L01)
    b.span(-1.3, 1.3, 0.0, 3.8, -4.6, 4.45, "wall", L2)


def _flyer(b):
    """A flying car: a low wedge with a glass canopy, four thruster pods with hot glow underneath, a cyan nose strip
    and a pink tail bar."""
    b.span(-1.1, 1.1, 0.5, 1.1, -2.6, 2.0, "dark", L01)                          # hull
    b.span(-0.9, 0.9, 0.55, 0.95, 2.0, 2.9, "dark", L01)                         # nose
    b.span(-0.75, 0.75, 1.1, 1.55, -0.9, 1.1, "glass", L01)                      # canopy
    for x in (-1.45, 1.45):
        for z in (-1.9, 1.5):
            b.cylinder((x, 0.2, z), (x, 0.9, z), 0.42, "trim", segments=10, at=L01)
            b.cylinder((x, 0.12, z), (x, 0.2, z), 0.32, "exhaust", segments=10, at=L01)   # thruster glow
    b.span(-1.6, 1.6, 0.6, 0.75, -2.1, -1.7, "trim", L01)                        # pod struts
    b.span(-1.6, 1.6, 0.6, 0.75, 1.3, 1.7, "trim", L01)
    b.span(-0.85, 0.85, 0.7, 0.8, 2.9, 2.96, "neon_cyan", L01)                   # nose strip
    b.span(-1.05, 1.05, 0.75, 0.9, -2.66, -2.6, "neon_pink", L01)                # tail bar
    b.span(-1.6, 1.6, 0.1, 1.55, -2.6, 2.95, "dark", L2)


def _drone(b):
    """A quad drone: body, four arms, rotor discs, a cyan sensor eye, red blinking nav lights."""
    b.span(-0.3, 0.3, 0.25, 0.55, -0.4, 0.4, "dark", L01)                        # body
    b.span(-0.12, 0.12, 0.3, 0.42, 0.4, 0.46, "neon_cyan", L01)                  # eye
    for sx in (-1, 1):
        for sz in (-1, 1):
            tip = (sx * 0.75, 0.5, sz * 0.75)
            b.beam((0.0, 0.45, 0.0), tip, 0.07, "trim", L01)
            b.cylinder((tip[0], 0.5, tip[2]), (tip[0], 0.56, tip[2]), 0.38, "rubber", segments=10, at=L01)
            b.box((tip[0], 0.44, tip[2]), (0.08, 0.08, 0.08), "warning", L01)   # nav light (blinks)
    b.span(-1.1, 1.1, 0.25, 0.56, -1.1, 1.1, "dark", L2)


# -- searchlights ------------------------------------------------------------------------------------------------------

def _searchlights(city, tops):
    """Sweeping searchlights on the roofs: the tallest towers (over `tall` m) and a few industrial plots. Position =
    the roof (the highest point over the footprint's centre), a phase and a sweep speed so they never move together,
    a colour (mostly cool white, a few in the neon colours)."""
    s = SEARCHLIGHTS
    rng = np.random.default_rng(11)
    ranked = sorted(range(len(tops)), key=lambda k: -tops[k][1])
    tall = [k for k in ranked if tops[k][1] >= s["tall"]][:s["most_tall"]]
    industrial = [k for k in ranked if city.instances[k][0].startswith("Ind_")][:s["most_industrial"]]
    out = []
    for k in tall + industrial:
        shape, top = tops[k]
        centre = shape.centroid
        out.append({"position": [round(centre.x, 2), round(top + 1.0, 2), round(centre.y, 2)],
                    "phase": round(float(rng.uniform(0.0, math.tau)), 3),
                    "speed": round(float(rng.uniform(0.15, 0.35)), 3),
                    "colour": s["colours"][int(rng.integers(len(s["colours"])))]})
    return out
