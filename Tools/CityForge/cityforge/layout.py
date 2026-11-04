"""
City layout: roads first, then blocks, lots and buildings (the usual order of procedural city generators):

1. Main roads (after Parish & Mueller 2001, "Procedural Modeling of Cities": main roads first, a radial pattern round a
   core): a ring road round the core, radial avenues out to the city edge, a main boulevard through the core (it goes
   on to the north tunnel), a perimeter road, frontage roads along the storm drain (the freeway runs above the north one).
2. Local streets (after Chen et al. 2008, "Interactive Procedural Street Modeling", tensor fields): every area between
   main roads gets a street grid turned to the local direction: square to the axes in the core, along the radial
   direction outside it. Spacing by zone (big blocks in the core, small ones in the slums); some streets left out.
3. Lots (after Vanegas et al. 2012, "Procedural Generation of Parcels in Urban Modeling", oriented bounding box
   splits): each block is cut across its long side again and again until the lots fit the zone.
4. Buildings: the unique megatowers and landmarks go into whole core blocks first; then each lot gets the biggest
   building of its zone that fits, turned so its front faces the nearest road. Lots with nothing that fits stay open
   (plazas: open space for fights).

Coordinates: Unity space (x, z on the ground), origin = the city centre (the terrain origin). Yaw = Unity euler Y.
"""
import json
import math
import random

import numpy as np
import shapely

from shapely import affinity
from shapely.geometry import LineString, Point, Polygon, box
from shapely.ops import split, unary_union

from . import specs as spec_list

CITY = (1000.0, 800.0)        # city size x, z (m)
CHANNEL_SHARE = 0.22          # storm-drain corridor at z = this share of the city depth (towards +Z)
CORRIDOR = 84.0               # channel (56 m) + a frontage road on each side
CHANNEL_TOP = 56.0
FREEWAY_OFFSET = 35.0         # freeway centre line: channel z + this (above the north frontage road)
RING = dict(cx=0.0, cz=-60.0, rx=270.0, rz=180.0)
WIDTH = dict(ring=24.0, radial=20.0, boulevard=26.0, perimeter=16.0, frontage=12.0, street=12.0)
RADIAL_ANGLES = (30, 70, 110, 150, 200, 240, 290, 330)   # degrees in the (x, z) plane; 0 = +X (the spiral is there)
BOULEVARD_X = 20.0
SPIRAL = dict(x=385.0, z=-60.0, radius=78.0)              # plaza kept free for the sky-road spiral
UNIQUE = ("Mega_Arcology", "Mega_Twins", "Landmark_Halo", "Mega_Needle", "Landmark_Orb", "Mega_Slab", "Landmark_Twist")
VARIANTS = {"tower": 6, "block": 6, "slum": 6, "industrial": 3}
# A style's "layout" entry overrides these (styles.py). zones: None = the default mix (_kind, _target); else a list of
# (zone limit 0..1, lot area target m², ((kind, share), ...)), the first zone whose limit is above the lot's zone wins.
# spacing: local street spacing (m) in the core, then by zone (< 0.55, < 0.9, the edge).
# fill: most buildings on one lot; gap: space kept round each building on a lot (m).
DEFAULTS = dict(city=CITY, unique=UNIQUE, variants=VARIANTS, zones=None, spacing=(118.0, 112.0, 98.0, 84.0),
                fill=4, gap=8.0, pack=False)
# pack: place each building where it leaves the biggest piece free (corners first) instead of the middle of the lot
# (the middle leaves thin strips all round that nothing fits in). Denser.


def settings(style):
    values = json.loads(json.dumps(DEFAULTS))
    values.update(json.loads(json.dumps(style.get("layout") or {})))
    return values


def variant_specs(base_specs=None, variants=None, seed=11):
    """New specs from the base buildings: same type, new seed and new numbers (floors, shape, crown...)."""
    rng = random.Random(seed)
    bases = {}
    for spec in base_specs if base_specs is not None else spec_list.BUILDINGS:
        if spec["type"] in ("tower", "block", "slum", "industrial"):
            bases.setdefault(spec["type"], []).append(spec)
    out = []
    for kind, count in (variants or VARIANTS).items():
        if kind not in bases:
            continue
        for k in range(count):
            base = bases[kind][k % len(bases[kind])]
            spec = json.loads(json.dumps(base))
            spec["name"] = f"{base['name']}_V{k + 1}"
            spec["seed"] = base["seed"] * 10 + k + 1
            spec["variantOf"] = base["name"]
            if kind == "tower":
                spec["tiers"] = [[sw, sd, max(3, int(round(floors * rng.uniform(0.7, 1.35)))), ox, oz]
                                 for sw, sd, floors, ox, oz in spec["tiers"]]
                spec["facade"] = rng.choice(("ribbon", "grid"))
                spec["crown"] = rng.choice(("pad", "antennas", "sign", "spire"))
            elif kind == "block":
                spec["floors"] = rng.randint(4, 9)
                if spec["shape"] != "U":
                    spec["shape"] = rng.choice(("rect", "L"))
                spec["roof"] = rng.choice(("tanks", "tanks", "pad"))
                spec["fire_escape"] = rng.choice(("left", "right", None))
                spec["balconies"] = round(rng.uniform(0.1, 0.4), 2)
            elif kind == "slum":
                spec["base_floors"] = rng.randint(2, 5)
                spec["stacks"] = rng.randint(6, 13)
            elif kind == "industrial":
                # Refinery parts get new heights (the older industrial keys stay as they are).
                for key in ("columns", "cooling"):
                    if key in spec:
                        spec[key] = [[x, z, r, round(h * rng.uniform(0.8, 1.2), 1)] for x, z, r, h in spec[key]]
                if "flares" in spec:
                    spec["flares"] = [[x, z, round(h * rng.uniform(0.85, 1.2), 1)] for x, z, h in spec["flares"]]
            out.append(spec)
    return out


class Road:
    def __init__(self, kind, points, closed=False, width=None):
        self.kind = kind
        self.width = width or WIDTH[kind]
        self.closed = closed
        self.points = list(points) + ([points[0]] if closed else [])
        self.line = LineString(self.points)

    def area(self):
        return self.line.buffer(self.width / 2, cap_style="flat" if not self.closed else "round", join_style="round")


def footprint(cx, cz, w, d, yaw):
    """Building footprint polygon: local +X = (cos, -sin), local +Z (the front) = (sin, cos) for a Unity yaw."""
    a = math.radians(yaw)
    ux, uz = math.cos(a), -math.sin(a)
    fx, fz = math.sin(a), math.cos(a)
    corners = [(sx * w / 2, sz * d / 2) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    return Polygon([(cx + x * ux + z * fx, cz + x * uz + z * fz) for x, z in corners])


def _parts(geometry):
    if geometry.is_empty:
        return []
    return list(geometry.geoms) if hasattr(geometry, "geoms") else [geometry]


class Layout:
    def __init__(self, building_specs, values=None, seed=5):
        self.rng = random.Random(seed)
        self.values = values or settings({})
        self.unique = tuple(self.values["unique"])
        self.width, self.depth = self.values["city"]
        self.rect = box(-self.width / 2, -self.depth / 2, self.width / 2, self.depth / 2)
        self.channel_z = self.depth * CHANNEL_SHARE
        self.freeway_z = self.channel_z + FREEWAY_OFFSET
        self.corridor = box(-self.width, self.channel_z - CORRIDOR / 2, self.width, self.channel_z + CORRIDOR / 2)
        self.channel = box(-self.width, self.channel_z - CHANNEL_TOP / 2, self.width, self.channel_z + CHANNEL_TOP / 2)
        self.spiral = Point(SPIRAL["x"], SPIRAL["z"]).buffer(SPIRAL["radius"], 48)
        self.specs = {spec["name"]: spec for spec in building_specs}
        self.pools = {}
        for spec in building_specs:
            if spec["name"] not in self.unique:
                self.pools.setdefault(spec["type"], []).append(spec)
        self.roads, self.instances, self.footprints, self.plazas, self.bridges = [], [], [], [], []
        self._main_roads()
        self.ring_area = self._ring_polygon()
        self._local_streets()
        self.road_area = unary_union([road.area() for road in self.roads])
        self._blocks()
        self._buildings()
        self._bridges()

    # -- roads -----------------------------------------------------------------------------------------------------
    def _ring_points(self, count=72):
        return [(RING["cx"] + RING["rx"] * math.cos(math.tau * k / count),
                 RING["cz"] + RING["rz"] * math.sin(math.tau * k / count)) for k in range(count)]

    def _ring_polygon(self):
        return Polygon(self._ring_points())

    def _main_roads(self):
        rng = self.rng
        hw, hd = self.width / 2, self.depth / 2
        self.roads.append(Road("ring", self._ring_points(), closed=True))
        for degrees in RADIAL_ANGLES:
            a = math.radians(degrees + rng.uniform(-4, 4))
            dx, dz = math.cos(a), math.sin(a)
            start = (RING["cx"] + RING["rx"] * dx, RING["cz"] + RING["rz"] * dz)
            ray = LineString([start, (start[0] + dx * 3000, start[1] + dz * 3000)])
            end = ray.intersection(self.rect.boundary)
            end = end if end.geom_type == "Point" else _parts(end)[0]
            mid = ((start[0] + end.x) / 2 - dz * rng.uniform(-25, 25), (start[1] + end.y) / 2 + dx * rng.uniform(-25, 25))
            self.roads.append(Road("radial", [start, mid, (end.x, end.y)]))
        self.roads.append(Road("boulevard", [(BOULEVARD_X, -hd), (BOULEVARD_X, hd)]))
        inset = WIDTH["perimeter"] / 2
        self.roads.append(Road("perimeter", [(-hw + inset, -hd + inset), (hw - inset, -hd + inset),
                                             (hw - inset, hd - inset), (-hw + inset, hd - inset)], closed=True))
        for side in (-1, 1):
            z = self.channel_z + side * FREEWAY_OFFSET
            self.roads.append(Road("frontage", [(-hw, z), (hw, z)]))

    def _zone(self, x, z):
        """0 = core ... 1 = city edge (an ellipse over the city rectangle)."""
        return math.hypot(x / (self.width / 2), z / (self.depth / 2))

    def _local_streets(self):
        rng = self.rng
        main = unary_union([road.area() for road in self.roads])
        areas = self.rect.difference(main).difference(self.corridor).difference(self.spiral)
        self.superblocks = [p for p in _parts(areas) if p.area > 1500]
        for area in self.superblocks:
            c = area.centroid
            core = self.ring_area.contains(c)
            gaps = self.values["spacing"]
            if core:
                angle, spacing = 0.0, gaps[0]
            else:
                angle = math.degrees(math.atan2(c.y - RING["cz"], c.x - RING["cx"]))
                r = self._zone(c.x, c.y)
                spacing = gaps[1] if r < 0.55 else gaps[2] if r < 0.9 else gaps[3]
            # Grid lines in the area's own frame, then turned into place and cut by the area.
            local = affinity.rotate(area, -angle, origin=c)
            x0, z0, x1, z1 = local.bounds
            lines = []
            offset = rng.uniform(0, spacing)
            x = x0 + offset
            while x < x1:
                lines.append(LineString([(x, z0 - 10), (x, z1 + 10)]))
                x += spacing * rng.uniform(0.85, 1.15)
            z = z0 + rng.uniform(0, spacing)
            while z < z1:
                lines.append(LineString([(x0 - 10, z), (x1 + 10, z)]))
                z += spacing * rng.uniform(0.85, 1.15)
            for line in lines:
                if rng.random() < 0.12 and not core:
                    continue
                for piece in _parts(affinity.rotate(line, angle, origin=c).intersection(area)):
                    if piece.geom_type == "LineString" and piece.length > 35.0:
                        self.roads.append(Road("street", list(piece.coords)))

    # -- blocks, lots, buildings -----------------------------------------------------------------------------------
    def _blocks(self):
        areas = self.rect.difference(self.road_area).difference(self.corridor).difference(self.spiral)
        self.blocks = [p for p in _parts(areas) if p.area > 500]

    def _fit(self, spec, area, rng, near_centre=False):
        """Where a building fits in an area: (yaw, centre, footprint) or None. Tries the area's main directions and
        a few positions; prefers the front facing the nearest road."""
        if self.values["pack"] and not near_centre:
            return self._fit_packed(spec, area, rng)
        w, d = spec["plot"]
        obb = area.minimum_rotated_rectangle
        coords = list(obb.exterior.coords)[:4]
        ex, ez = coords[1][0] - coords[0][0], coords[1][1] - coords[0][1]
        base = math.degrees(math.atan2(-ez, ex))
        inner = area.buffer(-1.5)
        if inner.is_empty:
            return None
        c = obb.centroid
        side_a = math.hypot(ex, ez)
        side_b = math.hypot(coords[2][0] - coords[1][0], coords[2][1] - coords[1][1])
        if min(w, d) > min(side_a, side_b) - 3.0 or max(w, d) > max(side_a, side_b) - 3.0:
            return None
        ax, az = ex / side_a, ez / side_a
        bx, bz = -az, ax
        # Positions: the middle first, then a grid over the free room along both sides.
        room_a = max(0.0, (side_a - max(w, d)) / 2)
        room_b = max(0.0, (side_b - min(w, d)) / 2)
        offsets = [(0.0, 0.0)] + [(fa * room_a, fb * room_b) for fa in (-1.0, -0.5, 0.5, 1.0) for fb in (-1.0, 0.0, 1.0)]
        best = None
        for oa, ob in offsets:
            cx, cz = c.x + ax * oa + bx * ob, c.y + az * oa + bz * ob
            for k in range(4):
                yaw = (base + 90.0 * k) % 360.0
                shape = footprint(cx, cz, w, d, yaw)
                if not inner.contains(shape):
                    continue
                a = math.radians(yaw)
                front = Point(cx + math.sin(a) * d / 2, cz + math.cos(a) * d / 2)
                score = front.distance(self.road_area) + (math.hypot(cx, cz) * 0.05 if near_centre else 0.0)
                if self.values["pack"]:
                    # Waste: the area left over outside the biggest free piece, as a strip width (m).
                    rest = [p.area for p in _parts(area.difference(shape.buffer(self.values["gap"])))]
                    waste = area.area - shape.area - (max(rest) if rest else 0.0)
                    score += waste / max(w, d)
                if best is None or score < best[0]:
                    best = (score, yaw, (cx, cz), shape)
            if best is not None and oa == 0.0 and ob == 0.0 and not self.values["pack"]:
                break
        return None if best is None else best[1:]

    def _fit_packed(self, spec, area, rng):
        """Pack mode: try centres on a grid over the part of the area a footprint centre can reach (the area shrunk by
        half the short side: a fast "does not fit"), in the area's main directions. Best = hugs the edges of the
        area (leaves one big free piece, not strips) and faces a road."""
        w, d = spec["plot"]
        inner = area.buffer(-1.5)
        if inner.is_empty:
            return None
        reach = inner.buffer(-min(w, d) / 2.0)
        if reach.is_empty:
            return None
        shapely.prepare(inner)
        coords = list(area.minimum_rotated_rectangle.exterior.coords)[:2]
        base = math.degrees(math.atan2(-(coords[1][1] - coords[0][1]), coords[1][0] - coords[0][0]))
        x0, z0, x1, z1 = reach.bounds
        step = max(3.0, min(w, d) / 5.0)
        xs, zs = np.meshgrid(np.arange(x0, x1 + step, step), np.arange(z0, z1 + step, step))
        keep = shapely.contains_xy(reach.buffer(step * 0.5), xs.ravel(), zs.ravel())
        points = list(zip(xs.ravel()[keep], zs.ravel()[keep]))
        if len(points) > 160:
            points = rng.sample(points, 160)
        ring_width = self.values["gap"] + 2.0
        best = None
        for cx, cz in points:
            for k in range(4):
                yaw = (base + 90.0 * k) % 360.0
                shape = footprint(cx, cz, w, d, yaw)
                if not inner.contains(shape):
                    continue
                a = math.radians(yaw)
                front = Point(cx + math.sin(a) * d / 2, cz + math.cos(a) * d / 2)
                ring = shape.buffer(ring_width)
                contact = ring.difference(area).area / max(1.0, ring.area - shape.area)
                score = front.distance(self.road_area) - 60.0 * contact
                if best is None or score < best[0]:
                    best = (score, yaw, (cx, cz), shape)
        return None if best is None else best[1:]

    def _place(self, spec, yaw, centre, shape):
        self.instances.append((spec["name"], (centre[0], 0.0, centre[1]), yaw))
        self.footprints.append((spec["name"], shape))

    def _style_zone(self, x, z):
        r = self._zone(x, z)
        zones = self.values["zones"]
        return next((zone for zone in zones if r < zone[0]), zones[-1])

    def _kind(self, x, z):
        rng = self.rng
        r = self._zone(x, z)
        if self.values["zones"]:
            kinds = self._style_zone(x, z)[2]
        elif r < 0.35:
            kinds = (("tower", 1.0),)
        elif r < 0.6:
            kinds = (("tower", 0.6), ("block", 0.4))
        elif r < 0.92:
            kinds = (("block", 0.65), ("tower", 0.15), ("slum", 0.2))
        elif x > self.width * 0.22:
            kinds = (("industrial", 0.55), ("block", 0.25), ("slum", 0.2))
        else:
            kinds = (("slum", 0.5), ("block", 0.35), ("industrial", 0.15))
        roll, total = rng.random(), 0.0
        for name, share in kinds:
            total += share
            if roll <= total:
                return name
        return kinds[-1][0]

    def _target(self, x, z, core):
        if core:
            return 5200.0
        if self.values["zones"]:
            return self._style_zone(x, z)[1]
        r = self._zone(x, z)
        if r < 0.6:
            return 3600.0
        if r < 0.92:
            return 2600.0
        return 3000.0 if x > self.width * 0.22 else 1500.0

    def _buildings(self):
        rng = self.rng
        blocks = sorted(self.blocks, key=lambda p: p.centroid.distance(Point(RING["cx"], RING["cz"])))
        # 1. Unique buildings into whole core blocks, biggest first, nearest the centre.
        uniques = sorted([self.specs[n] for n in self.unique if n in self.specs], key=lambda s: -s["plot"][0] * s["plot"][1])
        self.missing = []
        for spec in uniques:
            placed = False
            for k, block in enumerate(blocks):
                if not self.ring_area.buffer(140).contains(block.centroid):
                    continue
                fit = self._fit(spec, block, rng, near_centre=True)
                if fit:
                    yaw, centre, shape = fit
                    self._place(spec, yaw, centre, shape)
                    rest = block.difference(shape.buffer(12.0))
                    blocks[k:k + 1] = [p for p in _parts(rest) if p.area > 400]
                    placed = True
                    break
            if not placed:
                self.missing.append(spec["name"])
        # 2. Lots: oriented bounding box splits.
        lots = []
        for block in blocks:
            c = block.centroid
            core = self.ring_area.contains(c)
            # Pack mode: a whole block is one piece, filled building by building (no lot splits).
            lots += [block] if self.values["pack"] else self._split(block, self._target(c.x, c.y, core), 20.0)
        # 3. The biggest building of the zone that fits; else smaller kinds; else an open plaza.
        fallback = {"tower": ["block"], "block": ["slum"], "industrial": ["block", "slum"], "slum": []}
        for lot in lots:
            # A big lot can take more than one building: fill what is left after each one.
            pieces, placed = [lot], 0
            while pieces and placed < self.values["fill"]:
                piece = pieces.pop(0)
                c = piece.centroid
                pack = self.values["pack"]
                if placed == 0 or pack:
                    kind = self._kind(c.x, c.y)
                fit_spec = None
                # The first building of a lot may fall back to a smaller kind; the rest of the lot only takes the
                # lot's own kind (otherwise every leftover strip fills with slum shacks).
                kinds = [kind] + fallback[kind] if placed == 0 or pack else [kind]
                for name in kinds:
                    pool = sorted(self.pools.get(name, []),
                                  key=lambda s: -s["plot"][0] * s["plot"][1] * rng.uniform(0.7, 1.3))
                    for spec in pool:
                        fit = self._fit(spec, piece, rng)
                        if fit:
                            fit_spec = (spec, fit)
                            break
                    if fit_spec:
                        break
                if fit_spec is None:
                    if piece.area > 1200 and placed == 0:
                        self.plazas.append(piece)
                    continue
                spec, (yaw, centre, shape) = fit_spec
                self._place(spec, yaw, centre, shape)
                placed += 1
                gap = self.values["gap"]
                pieces += [p for p in _parts(piece.difference(shape.buffer(gap))) if p.area > 450 * gap / 8.0]

    def _split(self, poly, target, min_side):
        if poly.area <= target:
            return [poly]
        obb = poly.minimum_rotated_rectangle
        coords = list(obb.exterior.coords)[:4]
        e0 = (coords[1][0] - coords[0][0], coords[1][1] - coords[0][1])
        e1 = (coords[2][0] - coords[1][0], coords[2][1] - coords[1][1])
        long_, short = (e0, e1) if math.hypot(*e0) >= math.hypot(*e1) else (e1, e0)
        length = math.hypot(*long_)
        if length < 2 * min_side:
            return [poly]
        ux, uz = long_[0] / length, long_[1] / length
        c = obb.centroid
        t = self.rng.uniform(-0.12, 0.12) * length
        px, pz = c.x + ux * t, c.y + uz * t
        cut = LineString([(px - uz * 5000, pz + ux * 5000), (px + uz * 5000, pz - ux * 5000)])
        pieces = [g for g in _parts(split(poly, cut)) if g.area > 60]
        if len(pieces) < 2:
            return [poly]
        out = []
        for piece in pieces:
            out += self._split(piece, target, min_side)
        return out

    def _bridges(self):
        """A bridge wherever a road centre line crosses the channel: (x, z, yaw, deck width)."""
        channel_line = LineString([(-self.width, self.channel_z), (self.width, self.channel_z)])
        for road in self.roads:
            if road.kind in ("frontage",):
                continue
            hit = road.line.intersection(channel_line)
            for point in _parts(hit):
                if point.geom_type != "Point" or not self.rect.buffer(1.0).contains(point):
                    continue
                d = road.line.project(point)
                a, b = road.line.interpolate(max(0.0, d - 2.0)), road.line.interpolate(min(road.line.length, d + 2.0))
                yaw = math.degrees(math.atan2(b.x - a.x, b.y - a.y))
                self.bridges.append((point.x, point.y, yaw, road.width + 4.0))

    # -- results ---------------------------------------------------------------------------------------------------
    def city_rect(self):
        x0, z0, x1, z1 = self.rect.bounds
        return (x0, x1, z0, z1)

    def plots(self):
        """(name, corner list) per building, for the terrain audit."""
        return [(name, list(shape.exterior.coords)[:4]) for name, shape in self.footprints]

    def exterior(self):
        """What the terrain needs for the roads out of the city: freeway line, boulevard x, ramp ends."""
        return dict(freeway_z=self.freeway_z, north_x=BOULEVARD_X, ramp=self.width / 2 + 200.0,
                    city=self.city_rect())

    def audit(self):
        problems = []
        shapes = self.footprints
        for k, (name_a, a) in enumerate(shapes):
            for name_b, other in shapes[k + 1:]:
                if a.intersection(other).area > 1.0:
                    problems.append(f"layout     {name_a} and {name_b} overlap")
        for name, shape in shapes:
            if shape.intersection(self.road_area).area > 1.0:
                problems.append(f"layout     {name} stands on a road")
            if shape.intersection(self.corridor).area > 1.0:
                problems.append(f"layout     {name} stands in the channel corridor")
            if not self.rect.contains(shape):
                problems.append(f"layout     {name} leaves the city")
        if self.missing:
            problems.append(f"layout     no room for {', '.join(self.missing)}")
        return problems
