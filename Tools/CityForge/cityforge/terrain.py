"""
Terrain round the city (plan 7a2 follow-up): a desert basin with the city on its flat floor, a ring of mesas (flat tops
for landing and cover from lock-ons), a plateau rim, one canyon from the basin out through the rim (a low route in),
and a concrete storm-drain channel through the city that runs out into the canyon.

Everything is a height field in Unity space (x right, y up, z forward, metres), origin = the city centre on the
ground. It is built with numpy, then cut into square chunks with 3 LODs (palette swatch per face, the city's palette
material), skirts on the chunk edges (no cracks between LODs), and exported like the buildings: one FBX per chunk.
A 16-bit RAW height map goes out too (for a Unity Terrain, if the game uses one).
"""
import json
import math
import os

import numpy as np

STEP = 6.0               # LOD0 grid step (m); LOD1 and LOD2 use 2x and 4x
CHUNK_CELLS = 64         # LOD0 cells per chunk side (384 m)
LOD_FACTORS = (1, 2, 4)
OUTER_FACTORS = (2, 4, 8)  # chunks on the map edge (mostly outside the flight bounds): half the detail
SKIRT = 10.0             # skirt depth on chunk edges (m)
EDGE_SKIRT = 60.0        # skirt depth on the map edge (hides the seam to the far backdrop)
BACKDROP_RADIUS = 20000.0
CITY_FLAT = 40.0         # flat concrete margin round the city plots (m)

DEFAULTS = dict(
    size=3072.0, seed=7,
    rise=26.0,                    # basin floor rises this much towards the rim
    dunes=5.0,                    # dune noise outside the city (m)
    # Rim: a first cliff step at `radius`, a second wall `wall` m higher further out (hides the map edge from low and
    # mid height), a ragged crest.
    plateau=dict(radius=1240.0, width=70.0, height=165.0, warp=150.0, wall=200.0, wall_at=170.0, crest=35.0),
    mesas=dict(count=14, ring=(780.0, 1160.0), radius=(45.0, 150.0), height=(55.0, 140.0), cliff=10.0, talus=45.0),
    strata=11.0,                  # cliff terrace height (m)
    canyon=dict(width=(72.0, 110.0), wall=24.0, floor=(-8.0, -40.0), wiggle=110.0, start=40.0),
    channel=dict(depth=8.0, bottom=36.0, top=56.0, west=90.0),
)


def settings(style):
    values = json.loads(json.dumps(DEFAULTS))
    for key, value in (style.get("terrain") or {}).items():
        if isinstance(value, dict):
            values[key].update(value)
        else:
            values[key] = value
    return values


# -- noise ------------------------------------------------------------------------------------------------------------

def _lattice(seed):
    return np.random.default_rng(seed).random(1 << 16)


def _value(table, x, z, cell):
    gx, gz = np.floor(x / cell), np.floor(z / cell)
    fx, fz = x / cell - gx, z / cell - gz
    gx, gz = gx.astype(np.int64), gz.astype(np.int64)

    def corner(i, j):
        return table[((i * 73856093) ^ (j * 19349663)) & 0xFFFF]

    sx, sz = fx * fx * (3 - 2 * fx), fz * fz * (3 - 2 * fz)
    top = corner(gx, gz) * (1 - sx) + corner(gx + 1, gz) * sx
    bottom = corner(gx, gz + 1) * (1 - sx) + corner(gx + 1, gz + 1) * sx
    return top * (1 - sz) + bottom * sz


def fbm(seed, x, z, cell, octaves=4):
    """Fractal value noise in -1..1."""
    table = _lattice(seed)
    total, amp, norm = 0.0, 1.0, 0.0
    for k in range(octaves):
        total = total + amp * (_value(table, x + 1000.0 * k, z - 700.0 * k, cell) * 2.0 - 1.0)
        norm += amp
        amp *= 0.5
        cell *= 0.5
    return total / norm


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def polyline_distance(x, z, points):
    """Distance from every (x, z) to a polyline, and the distance along the line of the nearest point."""
    best = np.full(x.shape, np.inf)
    along = np.zeros(x.shape)
    run = 0.0
    for (ax, az), (bx, bz) in zip(points, points[1:]):
        dx, dz = bx - ax, bz - az
        length = math.hypot(dx, dz)
        t = np.clip(((x - ax) * dx + (z - az) * dz) / (length * length), 0.0, 1.0)
        d = np.hypot(x - (ax + t * dx), z - (az + t * dz))
        closer = d < best
        best = np.where(closer, d, best)
        along = np.where(closer, run + t * length, along)
        run += length
    return best, along, run


# -- the field ---------------------------------------------------------------------------------------------------------

class Terrain:
    """The height field and everything placed by it. city: (x0, x1, z0, z1) of the plots, relative to the city
    centre; channel_z: z of the storm-drain channel through the city (in the street gap left for it)."""

    def __init__(self, style, city, channel_z, plots=(), exterior=None):
        """plots: (name, corner list) per building. exterior: the roads out of the city (layout.Layout.exterior())."""
        self.values = values = settings(style)
        self.city, self.channel_z, self.plots = city, channel_z, list(plots)
        self.exterior = exterior
        size = values["size"]
        self.count = int(round(size / STEP)) + 1
        axis = np.linspace(-size / 2, size / 2, self.count)
        self.x, self.z = np.meshgrid(axis, axis)          # [iz, ix]
        self.markers = []
        self._paths()
        self.h = self._height(self.x, self.z)

    # -- layout of the features ------------------------------------------------------------------------------------
    def _paths(self):
        v = self.values
        x0, x1, z0, z1 = self.city
        rng = np.random.default_rng(v["seed"])
        size = v["size"]
        # Channel: from a closed end west of the city, along the street gap, east to the canyon mouth.
        mouth = (x1 + CITY_FLAT + v["canyon"]["start"] + 120.0, self.channel_z)
        west = (x0 - v["channel"]["west"], self.channel_z)
        self.channel = [west, (x0 - 20.0, self.channel_z), (x1 + 20.0, self.channel_z),
                        ((x1 + mouth[0]) / 2 + 20.0, self.channel_z + 12.0), mouth]
        # Canyon: from the mouth east to past the map edge, winding.
        phase = rng.uniform(0, math.tau)
        points = []
        steps = 40
        end_x = size / 2 + 120.0
        for k in range(steps + 1):
            t = k / steps
            x = mouth[0] + (end_x - mouth[0]) * t
            wiggle = v["canyon"]["wiggle"] * math.sin(phase + t * math.pi * 2.2) * min(1.0, t / 0.25)
            points.append((x, mouth[1] + wiggle - v["canyon"]["wiggle"] * math.sin(phase) * min(1.0, t / 0.25)))
        self.canyon = points
        self._exterior_roads()
        # Mesas on a ring, kept clear of the canyon, the roads and the channel's closed end.
        m = v["mesas"]
        self.mesas = []
        tries = 0
        while len(self.mesas) < m["count"] and tries < 500:
            tries += 1
            angle = rng.uniform(0, math.tau)
            ring = rng.uniform(*m["ring"])
            cx, cz = (x0 + x1) / 2 + math.cos(angle) * ring, (z0 + z1) / 2 + math.sin(angle) * ring
            radius = rng.uniform(*m["radius"])
            height = rng.uniform(*m["height"])
            canyon_gap, _a, _r = polyline_distance(np.array([cx]), np.array([cz]), self.canyon)
            if canyon_gap[0] < radius + m["talus"] + v["canyon"]["width"][1] / 2 + 60.0:
                continue
            stretch = rng.uniform(0.65, 1.0)
            gap_x = max(x0 - cx, 0.0, cx - x1)
            gap_z = max(z0 - cz, 0.0, cz - z1)
            if math.hypot(gap_x, gap_z) < radius / stretch + m["talus"] + CITY_FLAT + 60.0:
                continue
            if any(polyline_distance(np.array([cx]), np.array([cz]), road["points"])[0][0]
                   < radius / stretch + m["talus"] + road["width"] / 2 + 60.0 for road in self.roads):
                continue
            if any(math.hypot(cx - o[0], cz - o[1]) < radius / stretch + o[2] / o[5] + 60.0 for o in self.mesas):
                continue
            if abs(cx) + radius / stretch > size / 2 - 60 or abs(cz) + radius / stretch > size / 2 - 60:
                continue
            # (x, z, radius, height, turn, stretch): an ellipse, long axis radius / stretch.
            self.mesas.append((cx, cz, radius, height, rng.uniform(0, math.pi), stretch))

    def _city_distance(self, x, z):
        x0, x1, z0, z1 = self.city
        dx = np.maximum(np.maximum(x0 - x, 0.0), x - x1)
        dz = np.maximum(np.maximum(z0 - z, 0.0), z - z1)
        return np.hypot(dx, dz)

    def _rim(self, x, z):
        """The rim: a first cliff step, a higher wall further out, a ragged crest."""
        v = self.values
        seed = v["seed"]
        p = v["plateau"]
        cx, cz = (self.city[0] + self.city[1]) / 2, (self.city[2] + self.city[3]) / 2
        r = np.hypot(x - cx, z - cz) + p["warp"] * fbm(seed + 1, x, z, 420.0)
        r2 = np.hypot(x - cx, z - cz) + p["warp"] * 0.7 * fbm(seed + 4, x, z, 300.0)
        wall_at = p["radius"] + p["wall_at"]
        return (p["height"] * smoothstep(p["radius"] - p["width"] / 2, p["radius"] + p["width"] / 2, r)
                + p["wall"] * smoothstep(wall_at - p["width"] / 2, wall_at + p["width"] / 2, r2)
                + p["crest"] * (1.0 - np.abs(fbm(seed + 2, x, z, 240.0))) * smoothstep(wall_at, wall_at + 120.0, r2))

    def _exterior_roads(self):
        """Roads out of the city: west along the freeway line into a tunnel in the rim, north from the boulevard into
        a tunnel, east down into the canyon and out of the map. Each: points, heights at the points, width."""
        self.roads, self.portals = [], []
        e = self.exterior
        if not e:
            return
        x0, x1, z0, z1 = self.city
        half = self.values["size"] / 2
        fz = e["freeway_z"]

        def portal(start, direction):
            """First point along the direction where the rim cliff starts (rock over 22 m), a little before it."""
            steps = np.arange(0.0, 1500.0, 4.0)
            xs, zs = start[0] + direction[0] * steps, start[1] + direction[1] * steps
            rim = self._rim(xs, zs)
            k = int(np.argmax(rim > 22.0)) if (rim > 22.0).any() else len(steps) - 1
            k = max(0, k - 2)
            return (float(xs[k]), float(zs[k]))

        west = portal((x0, fz), (-1.0, 0.0))
        self.roads.append(dict(name="West", points=[(x0, fz), west], heights=[0.0, 0.0], width=24.0))
        self.portals.append(dict(name="West", position=west, yaw=90.0, width=24.0))
        north = portal((e["north_x"], z0), (0.0, -1.0))
        self.roads.append(dict(name="North", points=[(e["north_x"], z0), north], heights=[0.0, 0.0], width=20.0))
        self.portals.append(dict(name="North", position=north, yaw=0.0, width=20.0))
        # Canyon road: along the freeway line to the canyon mouth, then along the canyon floor (left bank).
        from shapely.geometry import LineString
        mouth_x = self.channel[-1][0]
        bank = LineString(self.canyon).offset_curve(18.0)
        coords = [(px, pz) for px, pz in bank.coords if px > mouth_x - 10.0 and abs(px) < half + 300.0]
        points = [(x1, fz), (mouth_x - 70.0, fz)] + coords[1:]
        c = self.values["canyon"]
        total = sum(math.hypot(b[0] - a[0], b[1] - a[1]) for a, b in zip(self.canyon, self.canyon[1:]))
        heights, run = [], 0.0
        for k, (px, pz) in enumerate(points):
            if k:
                run += math.hypot(px - points[k - 1][0], pz - points[k - 1][1])
            t = max(0.0, run - (mouth_x - 70.0 - x1))
            floor = c["floor"][0] + (c["floor"][1] - c["floor"][0]) * min(1.0, t / total)
            blend = float(smoothstep(0.0, 160.0, np.array(t)))
            heights.append(floor * blend)
        self.roads.append(dict(name="Canyon", points=points, heights=heights, width=24.0))

    def _flatten_roads(self, x, z, h):
        for road in self.roads:
            points, heights, width = road["points"], road["heights"], road["width"]
            d, along, _total = polyline_distance(x, z, points)
            runs = np.concatenate([[0.0], np.cumsum([math.hypot(b[0] - a[0], b[1] - a[1])
                                                     for a, b in zip(points, points[1:])])])
            level = np.interp(along, runs, heights)
            blend = smoothstep(width / 2 + 2.0, width / 2 + 30.0, d)
            near = d < width / 2 + 30.0
            h = np.where(near, level + (h - level) * blend, h)
        return h

    def _height(self, x, z):
        v = self.values
        seed = v["seed"]
        d = self._city_distance(x, z)
        h = v["rise"] * smoothstep(CITY_FLAT, 900.0, d) + v["dunes"] * fbm(seed, x, z, 180.0) * smoothstep(CITY_FLAT, 300.0, d)
        rock = np.maximum(np.zeros_like(h), self._rim(x, z))
        # Mesas: flat top, a cliff, a talus apron.
        m = v["mesas"]
        for k, (mx, mz, radius, height, turn, stretch) in enumerate(self.mesas):
            u = ((x - mx) * math.cos(turn) + (z - mz) * math.sin(turn)) * stretch
            w = -(x - mx) * math.sin(turn) + (z - mz) * math.cos(turn)
            dm = np.hypot(u, w) + 0.35 * radius * fbm(seed + 10 + k, x, z, radius * 0.7, 3)
            cliff = np.clip((radius - dm) / m["cliff"], 0.0, 1.0)
            talus = np.clip((radius + m["talus"] - dm) / m["talus"], 0.0, 1.0) ** 2 * 0.28
            rock = np.maximum(rock, height * np.maximum(cliff, talus))
        # Terraces on rock (strata), only where the rock stands above the ground.
        s = v["strata"]
        level = rock / s
        terraced = s * (np.floor(level) + smoothstep(0.62, 1.0, level - np.floor(level)))
        rock = np.where(rock > 6.0, terraced, rock)
        h = np.maximum(h, rock)
        self.rock = rock
        # City floor: exactly flat.
        h = np.where(d <= CITY_FLAT, 0.0, h)
        # Canyon: carve a winding trench to a floor that falls away from the basin.
        c = v["canyon"]
        dc, along, total = polyline_distance(x, z, self.canyon)
        width = c["width"][0] + (c["width"][1] - c["width"][0]) * (0.5 + 0.5 * fbm(seed + 3, along, along * 0, 300.0, 2))
        floor = c["floor"][0] + (c["floor"][1] - c["floor"][0]) * np.clip(along / total, 0.0, 1.0)
        wall = smoothstep(0.0, 1.0, (dc - width / 2) / c["wall"])
        carved = floor + (h - floor) * wall
        h = np.where(h > floor, carved, h)
        self.canyon_distance, self.canyon_width = dc, width
        h = self._flatten_roads(x, z, h)
        # Storm drain: a trapezoid concrete channel; open water strip in the middle.
        ch = v["channel"]
        dch, _along, _total = polyline_distance(x, z, self.channel)
        half_b, half_t = ch["bottom"] / 2, ch["top"] / 2
        profile = np.where(dch <= half_b, -ch["depth"],
                           -ch["depth"] * (1.0 - np.clip((dch - half_b) / (half_t - half_b), 0.0, 1.0)))
        h = np.where(dch < half_t, np.minimum(h, profile), h)
        self.channel_distance = dch
        return h

    # -- surface types ---------------------------------------------------------------------------------------------
    def masks(self):
        """Terrain facts for the biome shader, per height-field sample, 0..1 (RGBA = vertex colour, linear):
        R flow   water flow (D8 flow accumulation, log scale): gullies, wet streaks, river beds, lava or toxic runs
        G hollow concave ground (minus the Laplacian of a smoothed height): sediment, dirt, puddles
        B made   city floor, roads, channel (no biome ground there)
        A scree  loose rock spilled below cliffs (cliff mask blurred, minus the cliff itself)
        The biome decides what each fact looks like (terrain_biomes.py, TerrainBiome.hlsl)."""
        if getattr(self, "_masks", None) is not None:
            return self._masks
        h = self.h
        n = self.count
        # Flow: every sample passes its water to its lowest neighbour, highest samples first.
        padded = np.pad(h, 1, mode="edge")
        offsets = [(-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1)]
        drops = np.stack([(h - padded[1 + dz:1 + dz + n, 1 + dx:1 + dx + n]) / math.hypot(dz, dx)
                          for dz, dx in offsets])
        best = np.argmax(drops, axis=0)
        has = drops.max(axis=0) > 1e-3
        iz, ix = np.indices((n, n))
        rz = np.clip(iz + np.array([o[0] for o in offsets])[best], 0, n - 1)
        rx = np.clip(ix + np.array([o[1] for o in offsets])[best], 0, n - 1)
        receiver = np.where(has, rz * n + rx, -1).ravel()
        flow = np.ones(n * n)
        for i in np.argsort(-h.ravel(), kind="stable"):
            r = receiver[i]
            if r >= 0:
                flow[r] += flow[i]
        flow = np.clip((np.log(flow.reshape(n, n)) - math.log(6.0)) / math.log(600.0), 0.0, 1.0)

        def blur(a, passes):
            for _ in range(passes):
                a = (np.roll(a, 1, 0) + np.roll(a, -1, 0) + np.roll(a, 1, 1) + np.roll(a, -1, 1) + 4 * a) / 8
            return a

        smooth = blur(h, 2)
        laplace = (np.roll(smooth, 1, 0) + np.roll(smooth, -1, 0) + np.roll(smooth, 1, 1) + np.roll(smooth, -1, 1)
                   - 4 * smooth) / (STEP * STEP)
        hollow = np.clip(laplace * 18.0, 0.0, 1.0)
        gz, gx = np.gradient(h, STEP)
        slope = np.degrees(np.arctan(np.hypot(gx, gz)))
        cliff = (slope > 42.0).astype(float)
        scree = np.clip(blur(cliff, 4) * 2.2 - cliff, 0.0, 1.0) * (slope < 40.0)
        d = self._city_distance(self.x, self.z)
        made = (d <= CITY_FLAT).astype(float)
        made = np.maximum(made, (self.channel_distance < self.values["channel"]["top"] / 2).astype(float))
        for road in getattr(self, "roads", []):
            dr, _a, _t = polyline_distance(self.x, self.z, road["points"])
            made = np.maximum(made, smoothstep(road["width"] / 2 + 4.0, road["width"] / 2, dr))
        self._masks = np.stack([flow, hollow, made, scree], axis=-1)
        return self._masks

    def cell_swatches(self):
        """City swatch name index per LOD0 cell: channel, city concrete, cliff strata, talus rock, sand."""
        h = self.h
        hc = (h[:-1, :-1] + h[1:, :-1] + h[:-1, 1:] + h[1:, 1:]) / 4
        sx = ((h[:-1, 1:] - h[:-1, :-1]) + (h[1:, 1:] - h[1:, :-1])) / (2 * STEP)
        sz = ((h[1:, :-1] - h[:-1, :-1]) + (h[1:, 1:] - h[:-1, 1:])) / (2 * STEP)
        slope = np.degrees(np.arctan(np.hypot(sx, sz)))
        xc = (self.x[:-1, :-1] + self.x[1:, 1:]) / 2
        zc = (self.z[:-1, :-1] + self.z[1:, 1:]) / 2
        dch, _a, _t = polyline_distance(xc, zc, self.channel)
        city = self._city_distance(xc, zc) <= CITY_FLAT
        ch = self.values["channel"]
        names = np.full(hc.shape, "sand", dtype=object)
        # Rock by steepness: the steepest faces (terrace risers) dark, the rest red sandstone. Colour that follows the
        # geometry reads as strata; height bands thinner than the grid would zig-zag.
        names[slope >= 16.0] = "rock"
        names[slope >= 58.0] = "rock_dark"
        names[city] = "concrete"
        in_channel = dch < ch["top"] / 2
        names[in_channel] = "trim"
        names[in_channel & (dch < ch["bottom"] / 2)] = "concrete"
        names[in_channel & (dch < 2.5)] = "glass"
        return names

    # -- markers ---------------------------------------------------------------------------------------------------
    def make_markers(self):
        """MesaTop markers (landing / objective circles on flat tops), canyon and channel paths (bot route hints)."""
        markers = []
        m = self.values["mesas"]
        for k, (mx, mz, radius, height, _turn, _stretch) in enumerate(self.mesas):
            clear = radius * 0.5 - m["cliff"]
            if clear < 15.0:
                continue
            top = float(self.sample(mx, mz))
            markers.append(("MesaTop", k, (mx, top, mz), round(clear, 1)))
        self.markers = markers
        return markers

    def path_points(self, points, lift=0.0):
        return [(px, float(self.sample(px, pz)) + lift, pz) for px, pz in points
                if abs(px) <= self.values["size"] / 2 and abs(pz) <= self.values["size"] / 2]

    def sample(self, x, z):
        """Height at (x, z) by bilinear interpolation of the field."""
        size = self.values["size"]
        fx = (np.asarray(x) + size / 2) / STEP
        fz = (np.asarray(z) + size / 2) / STEP
        ix = np.clip(np.floor(fx).astype(int), 0, self.count - 2)
        iz = np.clip(np.floor(fz).astype(int), 0, self.count - 2)
        tx, tz = np.clip(fx - ix, 0, 1), np.clip(fz - iz, 0, 1)
        h = self.h
        return ((h[iz, ix] * (1 - tx) + h[iz, ix + 1] * tx) * (1 - tz)
                + (h[iz + 1, ix] * (1 - tx) + h[iz + 1, ix + 1] * tx) * tz)

    # -- audit -----------------------------------------------------------------------------------------------------
    def audit(self):
        problems = []
        d = self._city_distance(self.x, self.z)
        city = (d <= CITY_FLAT) & (self.channel_distance >= self.values["channel"]["top"] / 2)
        bumps = np.abs(self.h[city]).max() if city.any() else 0.0
        if bumps > 0.01:
            problems.append(f"city       ground not flat ({bumps:.2f} m)")
        top = self.values["channel"]["top"] / 2
        for name, corners in self.plots:
            if len(corners) == 4 and not hasattr(corners[0], "__len__"):
                px0, px1, pz0, pz1 = corners
                corners = [(px0, pz0), (px1, pz0), (px1, pz1), (px0, pz1)]
            pts = list(corners) + [((a[0] + b[0]) / 2, (a[1] + b[1]) / 2) for a, b in zip(corners, corners[1:] + corners[:1])]
            pts.append((sum(p[0] for p in corners) / 4, sum(p[1] for p in corners) / 4))
            gx, gz = np.array([p[0] for p in pts]), np.array([p[1] for p in pts])
            dch, _a, _t = polyline_distance(gx, gz, self.channel)
            if (dch < top + 2.0).any():
                problems.append(f"plot       {name} touches the storm-drain channel")
            heights = self.sample(gx, gz)
            if np.abs(heights).max() > 0.05:
                problems.append(f"plot       {name} is not on flat ground ({np.abs(heights).max():.2f} m)")
        # Canyon: at every path point, the open width 30 m above its floor must be 50 m or more.
        for k, (px, pz) in enumerate(self.canyon[1:-1], 1):
            ax, az = self.canyon[k + 1][0] - self.canyon[k - 1][0], self.canyon[k + 1][1] - self.canyon[k - 1][1]
            length = math.hypot(ax, az)
            nx, nz = -az / length, ax / length
            offsets = np.linspace(-150.0, 150.0, 301)
            heights = self.sample(px + nx * offsets, pz + nz * offsets)
            floor = heights[150]
            if abs(px) > self.values["size"] / 2 - 10:
                continue
            open_ = heights < floor + 30.0
            left = right = 150
            while left > 0 and open_[left - 1]:
                left -= 1
            while right < 300 and open_[right + 1]:
                right += 1
            if right - left < 50:
                problems.append(f"canyon     only {right - left} m wide at ({px:.0f}, {pz:.0f})")
        for kind, index, (mx, my, mz), radius in self.markers:
            angles = np.linspace(0, math.tau, 24, endpoint=False)
            ring = self.sample(mx + np.cos(angles) * radius, mz + np.sin(angles) * radius)
            if np.abs(ring - my).max() > 1.0:
                problems.append(f"mesa       {kind}_{index} top not flat within r {radius} m ({np.abs(ring - my).max():.1f} m)")
        return problems

    # -- export data -----------------------------------------------------------------------------------------------
    def write_masks(self, path):
        """The same masks as an RGBA PNG (rows from -Z, like the RAW height map), for a Unity Terrain."""
        import struct
        import zlib

        data = np.clip(np.round(self.masks()[::-1] * 255.0), 0, 255).astype(np.uint8)
        height, width = data.shape[:2]
        raw = np.concatenate([np.zeros((height, 1), np.uint8), data.reshape(height, width * 4)], axis=1)

        def chunk(kind, payload):
            return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))

        with open(path, "wb") as handle:
            handle.write(b"\x89PNG\r\n\x1a\n")
            handle.write(chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)))
            handle.write(chunk(b"IDAT", zlib.compress(raw.tobytes(), 7)))
            handle.write(chunk(b"IEND", b""))

    def write_raw(self, path):
        """16-bit little-endian RAW (Unity Terrain import: Mac/Windows byte order = Windows), rows from -Z to +Z."""
        low, high = float(self.h.min()), float(self.h.max())
        data = np.round((self.h - low) / (high - low) * 65535.0).astype("<u2")
        data.tofile(path)
        return low, high


def chunk_ranges(count):
    cells = count - 1
    return [(k * CHUNK_CELLS, min(cells, (k + 1) * CHUNK_CELLS)) for k in range((cells + CHUNK_CELLS - 1) // CHUNK_CELLS)]


# -- meshes (Blender) -------------------------------------------------------------------------------------------------

def make_chunks(t, collection, material):
    """One object per chunk and LOD (vertices relative to the city centre). Returns [(name, [LOD objects], step
    factors)]."""
    from .kit import sw

    names = t.cell_swatches()
    lookup = {name: sw(name) for name in set(names.ravel())}
    index = np.vectorize(lookup.get)(names).astype(np.int32)
    chunks = []
    ranges = chunk_ranges(t.count)
    last = len(ranges) - 1
    for ci, (z0, z1) in enumerate(ranges):
        for cj, (x0, x1) in enumerate(ranges):
            name = f"Terrain_{ci:02d}_{cj:02d}"
            factors = OUTER_FACTORS if ci in (0, last) or cj in (0, last) else LOD_FACTORS
            objects = [_chunk_mesh(t, index, z0, z1, x0, x1, factor, f"{name}_LOD{level}", collection, material)
                       for level, factor in enumerate(factors)]
            chunks.append((name, objects, factors))
    return chunks


def _steps(a, b, factor):
    values = list(range(a, b + 1, factor))
    if values[-1] != b:
        values.append(b)
    return np.array(values)


def _chunk_mesh(t, index, z0, z1, x0, x1, factor, name, collection, material):
    import bpy

    from .pf import palette

    iz, ix = _steps(z0, z1, factor), _steps(x0, x1, factor)
    rows, cols = len(iz), len(ix)
    X, Z, H = (a[np.ix_(iz, ix)] for a in (t.x, t.z, t.h))
    verts = np.stack([-X, -Z, H], axis=-1).reshape(-1, 3)   # Unity -> Blender (geo.to_blender)
    grid = np.arange(rows * cols).reshape(rows, cols)
    faces = np.stack([grid[:-1, :-1], grid[:-1, 1:], grid[1:, 1:], grid[1:, :-1]], axis=-1).reshape(-1, 4).tolist()
    cells = index.shape[0]
    pick_z = np.minimum(iz[:-1] + factor // 2, cells - 1)
    pick_x = np.minimum(ix[:-1] + factor // 2, cells - 1)
    swatches = index[np.ix_(pick_z, pick_x)].ravel().tolist()
    # Skirts: a strip hanging down from every chunk edge, facing out.
    verts = verts.tolist()
    centre = np.array([-(t.x[0, x0] + t.x[0, x1]) / 2, -(t.z[z0, 0] + t.z[z1, 0]) / 2])
    from .kit import sw
    skirt_swatch = sw("rock_dark")
    last = t.count - 1
    for edge, on_map_edge in ((grid[0, :], z0 == 0), (grid[-1, :], z1 == last), (grid[:, 0], x0 == 0),
                              (grid[:, -1], x1 == last)):
        depth = EDGE_SKIRT if on_map_edge else SKIRT
        bottom = []
        for v in edge:
            x, y, h = verts[v]
            bottom.append(len(verts))
            verts.append([x, y, h - depth])
        for k in range(len(edge) - 1):
            a, b, c, d = int(edge[k]), int(edge[k + 1]), bottom[k + 1], bottom[k]
            pa, pb, pc = np.array(verts[a]), np.array(verts[b]), np.array(verts[c])
            normal = np.cross(pb - pa, pc - pb)
            mid = (pa[:2] + pb[:2]) / 2
            if np.dot(normal[:2], mid - centre) < 0:
                a, b, c, d = b, a, d, c
            faces.append([a, b, c, d])
            swatches.append(skirt_swatch)
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    attribute = mesh.attributes.new("swatch", "INT", "FACE")
    attribute.data.foreach_set("value", swatches)
    # Biome masks as vertex colour (point domain; skirt vertices copy their edge vertex).
    masks = t.masks()[np.ix_(iz, ix)].reshape(-1, 4)
    skirt_count = len(verts) - masks.shape[0]
    edge_rows = np.concatenate([grid[0, :], grid[-1, :], grid[:, 0], grid[:, -1]])
    colours = np.concatenate([masks, masks[edge_rows[:skirt_count]]]) if skirt_count else masks
    layer = mesh.color_attributes.new("Masks", "FLOAT_COLOR", "POINT")
    layer.data.foreach_set("color", colours.astype(np.float32).ravel())
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    palette.apply_uvs(obj, palette_only=True)
    # One UV layer keeps the files small: keep "Palette" (the Blender material reads it); in Unity it is UV0.
    mesh.uv_layers.remove(mesh.uv_layers["UVMap"])
    mesh.materials.append(material)
    mesh.shade_smooth()
    mesh.set_sharp_from_angle(angle=math.radians(40.0))
    return obj


# -- far backdrop (no colliders) -----------------------------------------------------------------------------------------

def backdrop_heights(t, x, z):
    """Far scenery round the map: under the terrain inside the map, the map edge height blended into ridged
    mountains outside it, sinking towards the horizon; the canyon carries on past the edge and bends away."""
    v = t.values
    half = v["size"] / 2
    seed = v["seed"]
    inside = (np.abs(x) < half - 1.0) & (np.abs(z) < half - 1.0)
    cx, cz = np.clip(x, -half, half), np.clip(z, -half, half)
    edge = t.sample(cx, cz)
    d_out = np.hypot(x - cx, z - cz)
    r = np.hypot(x, z)
    ridges = 1.0 - np.abs(fbm(seed + 20, x, z, 5200.0, 5))
    mountains = (380.0 + 820.0 * ridges ** 2 * smoothstep(1500.0, 7000.0, r)) * (1.0 - 0.85 * smoothstep(11000.0, BACKDROP_RADIUS, r))
    h = np.where(inside, t.sample(np.clip(x, -half, half), np.clip(z, -half, half)) - 40.0,
                 edge + (mountains - edge) * smoothstep(0.0, 1400.0, d_out))
    # The canyon goes on outside the map and turns north, rising into the mountains (out of sight round the bend).
    (ax, az), (bx, bz) = t.canyon[-3], t.canyon[-1]
    path = [t.canyon[-4], t.canyon[-3], (bx, bz), (bx + 900.0, bz - 150.0), (bx + 1600.0, bz - 900.0),
            (bx + 1900.0, bz - 2200.0)]
    dc, along, total = polyline_distance(x, z, path)
    floor = v["canyon"]["floor"][1] + 520.0 * smoothstep(0.35, 1.0, along / total)
    wall = smoothstep(0.0, 1.0, (dc - 45.0) / 40.0)
    carved = floor + (h - floor) * wall
    outside = ~inside
    h = np.where(outside & (h > floor), carved, h)
    return h


def make_backdrop(t, collection, material, rings=44, segments=160):
    """Polar mesh from just inside the map edge out to BACKDROP_RADIUS (one LOD, no collider)."""
    import bpy

    from .kit import sw
    from .pf import palette

    half = t.values["size"] / 2
    radii = (half * 0.85) * (BACKDROP_RADIUS / (half * 0.85)) ** (np.arange(rings + 1) / rings)
    angles = np.linspace(0.0, math.tau, segments, endpoint=False)
    rr, aa = np.meshgrid(radii, angles, indexing="ij")
    x, z = rr * np.cos(aa), rr * np.sin(aa)
    h = backdrop_heights(t, x, z)
    verts = np.stack([-x, -z, h], axis=-1).reshape(-1, 3).tolist()
    index = np.arange((rings + 1) * segments).reshape(rings + 1, segments)
    faces, swatches = [], []
    for i in range(rings):
        for j in range(segments):
            k = (j + 1) % segments
            quad = [int(index[i, j]), int(index[i + 1, j]), int(index[i + 1, k]), int(index[i, k])]
            pa, pb, pc = (np.array(verts[q]) for q in quad[:3])
            if np.cross(pb - pa, pc - pb)[2] < 0:
                quad.reverse()
            faces.append(quad)
            rise = abs(h[i + 1, j] - h[i, j]) / max(1.0, radii[i + 1] - radii[i])
            swatches.append(sw("rock_dark" if rise > 0.5 else "rock" if rise > 0.15 else "sand"))
    mesh = bpy.data.meshes.new("Backdrop")
    mesh.from_pydata(verts, [], faces)
    mesh.attributes.new("swatch", "INT", "FACE").data.foreach_set("value", swatches)
    # Zero biome masks (no flow, hollow, made ground or scree): without them Unity reads white (all masks on).
    mesh.color_attributes.new("Masks", "FLOAT_COLOR", "POINT").data.foreach_set(
        "color", np.zeros(len(verts) * 4, dtype=np.float32))
    obj = bpy.data.objects.new("Backdrop", mesh)
    collection.objects.link(obj)
    palette.apply_uvs(obj, palette_only=True)
    mesh.uv_layers.remove(mesh.uv_layers["UVMap"])
    mesh.materials.append(material)
    mesh.shade_smooth()
    mesh.set_sharp_from_angle(angle=math.radians(40.0))
    return obj


def make_skyline(t, collection, material, count=260):
    """The megacity beyond the walls: simple towers far out in two arcs (west, north-west and south-west; the canyon
    side stays open desert), dark glass with lit bands and a few neon rings. No colliders."""
    import bpy

    from .kit import Building
    from .pf import palette

    rng = np.random.default_rng(t.values["seed"] + 99)
    holder = Building(dict(name="Skyline", type="skyline", seed=int(t.values["seed"]) + 99, plot=(1, 1)),
                      dict(lit_cool=0.35))
    for _ in range(count):
        angle = math.radians(rng.uniform(110.0, 250.0))   # around -X (west), Unity yaw-free angles
        radius = rng.uniform(5200.0, 11500.0)
        x, z = math.cos(angle) * radius, math.sin(angle) * radius
        ground = float(backdrop_heights(t, np.array([x]), np.array([z]))[0])
        height = rng.uniform(140.0, 420.0) * (1.6 if rng.random() < 0.15 else 1.0) * (1.25 - radius / 23000.0)
        w, d = rng.uniform(40.0, 110.0), rng.uniform(40.0, 110.0)
        y0 = ground - 30.0
        holder.span(x - w / 2, x + w / 2, y0, ground + height, z - d / 2, z + d / 2, "glass", (0,),
                    swatches={"bottom": "glass"})
        for k in range(int(rng.integers(1, 4))):
            yy = ground + height * rng.uniform(0.25, 0.95)
            colour = ["lit_warm", "lit_cool", "neon_pink", "neon_cyan", "sodium"][int(rng.integers(0, 5))]
            holder.span(x - w / 2 - 1.5, x + w / 2 + 1.5, yy, yy + rng.uniform(2.0, 6.0), z - d / 2 - 1.5, z + d / 2 + 1.5,
                        colour, (0,))
        if rng.random() < 0.5:
            holder.box((x, ground + height + 0.5, z), (3.0, 3.0, 3.0), "warning", (0,))
    obj = holder.lods[0].to_object("Skyline", collection, bevel_width=0.0)
    palette.apply_uvs(obj, palette_only=True)
    for name in ("UVMap",):
        obj.data.uv_layers.remove(obj.data.uv_layers[name])
    if "screen" in obj.data.attributes:
        obj.data.attributes.remove(obj.data.attributes["screen"])
    if obj.data.uv_layers.get("Screen") is not None:
        obj.data.uv_layers.remove(obj.data.uv_layers["Screen"])
    obj.data.materials.clear()
    obj.data.materials.append(material)
    from . import wear
    wear.clear(obj)   # zero wear masks: Unity reads a missing vertex colour as white (full grime)
    return obj
