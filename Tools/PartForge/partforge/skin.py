"""
Skin: one continuous, filleted hull surface for the Arcus line (owner, 28 Nov 2026: "shapes must look designed and
blended, not primitives stuck together"; "the boosters should feel like they are merged with the fuselage"; "panel
lining more intentional").

How it works
- Every station along the hull (z) has a 2D cross-section: outlines (rounded polygons, ellipses) joined by a smooth
  union. The fillet between the fuselage and an engine hump, a canopy or a wing glove is part of the section, so the
  hull is one surface with real fillets and clean quad rows: no boolean, no remesh, no loose pieces.
- A section is traced by rays from a centre point (the shape must be star-shaped round it), resampled by arc length
  (more points round tight corners, so chines stay crisp) and mirrored: the skin is exactly symmetric.
- Stations are traced densely, then thinned where the surface runs straight (few rows in the boom).
- Design lines are cut into the skin with planes (bmesh bisect), so every colour edge is a straight, crisp mesh edge:
  paint() fills a region (two-tone split, glass), groove() cuts a shallow dark V (panel lines), band() lays a flush
  strip (light lines, frames), recess() pushes a region in (intakes, vents).
- Lines are drawn in a view: "side" (z, y), "top" (|x|, z) or "front" (|x|, y). Top and front lines are mirrored.
  `facing` keeps a line on the faces that look into the view (|normal| share along the view axis), `depth` limits it
  along the view axis (side: |x|, top: y, front: z).

Units and axes: Unity space (x right, y up, z forward), metres.
"""
import math
import os

import bmesh
import numpy as np
from mathutils import Vector


# ---------------------------------------------------------------------------------------------------------------------
# Tracks: a value along z, smooth through key stations (monotone cubic: no overshoot, no ripples)
# ---------------------------------------------------------------------------------------------------------------------

class Track:
    def __init__(self, *pairs):
        if len(pairs) == 1 and isinstance(pairs[0], (int, float)):
            pairs = ((0.0, float(pairs[0])),)
        keys = sorted(((float(z), float(v)) for z, v in pairs), key=lambda k: k[0])
        self.z = [k[0] for k in keys]
        self.v = [k[1] for k in keys]
        n = len(keys)
        if n == 1:
            self.m = [0.0]
            return
        h = [self.z[i + 1] - self.z[i] for i in range(n - 1)]
        d = [(self.v[i + 1] - self.v[i]) / h[i] for i in range(n - 1)]
        m = [0.0] * n
        m[0], m[-1] = d[0], d[-1]
        for i in range(1, n - 1):
            if d[i - 1] * d[i] <= 0.0:
                m[i] = 0.0
            else:
                w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
                m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])
        # Ends: no overshoot either.
        for i, j in ((0, 0), (n - 1, n - 2)):
            if d[j] == 0.0 or m[i] / d[j] > 3.0:
                m[i] = 3.0 * d[j] if d[j] != 0.0 else 0.0
        self.m = m

    def __call__(self, z):
        zs, vs, ms = self.z, self.v, self.m
        if z <= zs[0] or len(zs) == 1:
            return vs[0]
        if z >= zs[-1]:
            return vs[-1]
        lo, hi = 0, len(zs) - 1
        while hi - lo > 1:
            mid = (lo + hi) // 2
            if zs[mid] <= z:
                lo = mid
            else:
                hi = mid
        h = zs[hi] - zs[lo]
        t = (z - zs[lo]) / h
        t2, t3 = t * t, t * t * t
        return ((2 * t3 - 3 * t2 + 1) * vs[lo] + (t3 - 2 * t2 + t) * h * ms[lo] + (-2 * t3 + 3 * t2) * vs[hi]
                + (t3 - t2) * h * ms[hi])

    @property
    def span(self):
        return self.z[0], self.z[-1]


def tracks(table):
    """{name: [(z, value), ...]} -> {name: Track}. A plain number is a constant."""
    return {name: (Track(value) if isinstance(value, (int, float)) else Track(*value)) for name, value in table.items()}


# ---------------------------------------------------------------------------------------------------------------------
# 2D outlines and signed distances
# ---------------------------------------------------------------------------------------------------------------------

def rounded(points, radii, arc=6):
    """Closed outline through corner points (x, y) with a fillet of radius r at each corner (0 = sharp). The fillet
    shrinks where an edge is too short for it. Returns an (n, 2) array."""
    count = len(points)
    out = []
    for i in range(count):
        p = np.array(points[i], float)
        r = radii[i]
        a = np.array(points[i - 1], float)
        b = np.array(points[(i + 1) % count], float)
        u, v = a - p, b - p
        lu, lv = np.linalg.norm(u), np.linalg.norm(v)
        if r <= 1e-6 or lu < 1e-9 or lv < 1e-9:
            out.append(p)
            continue
        u, v = u / lu, v / lv
        cos = float(np.clip(np.dot(u, v), -1.0, 1.0))
        half = math.acos(cos) / 2.0
        if half > math.radians(88.5):
            out.append(p)
            continue
        t = r / math.tan(half)
        limit = 0.48 * min(lu, lv)
        if t > limit:
            t = limit
            r = t * math.tan(half)
        start, end = p + u * t, p + v * t
        bis = (u + v) / np.linalg.norm(u + v)
        centre = p + bis * (r / math.sin(half))
        a0 = math.atan2(*(start - centre)[::-1])
        a1 = math.atan2(*(end - centre)[::-1])
        sweep = (a1 - a0 + math.pi) % (2 * math.pi) - math.pi
        for k in range(arc + 1):
            ang = a0 + sweep * k / arc
            out.append(centre + r * np.array((math.cos(ang), math.sin(ang))))
    return np.array(out)


def chined(top, top_w, chine_y, chine_w, bottom, bottom_w, r_top=0.08, r_chine=0.03, r_bottom=0.06, cx=0.0):
    """The Arcus hull section: flat top (half-width top_w), sloped upper flank to a chine (widest, at chine_y), lower
    flank in to a flat belly (half-width bottom_w). Six corners, mirrored."""
    pts = [(cx + top_w, top), (cx + chine_w, chine_y), (cx + bottom_w, bottom),
           (cx - bottom_w, bottom), (cx - chine_w, chine_y), (cx - top_w, top)]
    return rounded(pts, [r_top, r_chine, r_bottom, r_bottom, r_chine, r_top])


def faceted(half, radii, arc=6):
    """A section from its +x corners, top to bottom (x, y), mirrored; radii one per corner."""
    pts = list(half) + [(-x, y) for x, y in reversed(half)]
    return rounded(pts, list(radii) + list(reversed(radii)), arc)


def box(cx, cy, hw, hh, r, top_w=None):
    """A rounded box (top_w: half-width of the top edge, for a trapezoid)."""
    tw = hw if top_w is None else top_w
    pts = [(cx + tw, cy + hh), (cx + hw, cy - hh), (cx - hw, cy - hh), (cx - tw, cy + hh)]
    return rounded(pts, [r, r, r, r])


def _sdf_poly(P, poly):
    A = poly
    B = np.roll(poly, -1, axis=0)
    E = B - A
    EE = np.maximum((E * E).sum(-1), 1e-12)
    W = P[:, None, :] - A[None, :, :]
    t = np.clip((W * E[None]).sum(-1) / EE[None], 0.0, 1.0)
    D = W - t[..., None] * E[None]
    d = np.sqrt((D * D).sum(-1).min(1))
    py = P[:, None, 1]
    px = P[:, None, 0]
    ay, by = A[None, :, 1], B[None, :, 1]
    ax, bx = A[None, :, 0], B[None, :, 0]
    crosses = (ay > py) != (by > py)
    xint = ax + (py - ay) * (bx - ax) / np.where(np.abs(by - ay) < 1e-12, 1e-12, by - ay)
    inside = (crosses & (px < xint)).sum(1) % 2 == 1
    return np.where(inside, -d, d)


def _sdf_ellipse(P, cx, cy, rx, ry):
    q = (P - np.array((cx, cy))) / np.array((rx, ry))
    k0 = np.sqrt((q * q).sum(-1))
    q2 = (P - np.array((cx, cy))) / np.array((rx * rx, ry * ry))
    k1 = np.maximum(np.sqrt((q2 * q2).sum(-1)), 1e-9)
    return k0 * (k0 - 1.0) / k1


def _sdf(P, shape):
    kind = shape[0]
    if kind == "poly":
        return _sdf_poly(P, shape[1])
    if kind == "ellipse":
        return _sdf_ellipse(P, *shape[1:])
    raise ValueError(kind)


def poly(outline):
    return ("poly", np.asarray(outline, float))


def ellipse(cx, cy, rx, ry):
    return ("ellipse", cx, cy, max(rx, 1e-4), max(ry, 1e-4))


def _smin(a, b, k):
    if k <= 1e-6:
        return np.minimum(a, b)
    h = np.maximum(k - np.abs(a - b), 0.0) / k
    return np.minimum(a, b) - h * h * k * 0.25


def _smax(a, b, k):
    return -_smin(-a, -b, k)


class Section:
    """One cross-section: a base outline, then ("union", shape, k) and ("cut", shape, k) steps; centre = the point
    the rays start from."""

    def __init__(self, base, centre):
        self.steps = [("base", base, 0.0)]
        self.centre = centre

    def union(self, shape, k=0.0):
        self.steps.append(("union", shape, k))
        return self

    def cut(self, shape, k=0.0):
        self.steps.append(("cut", shape, k))
        return self

    def sdf(self, P):
        d = None
        for op, shape, k in self.steps:
            s = _sdf(P, shape)
            if op == "base":
                d = s
            elif op == "union":
                d = _smin(d, s, k)
            else:
                d = _smax(d, -s, k)
        return d

    def trace(self, rays=241, reach=4.0, iterations=24):
        """The +x half of the outline from the top (x = 0) round to the bottom (x = 0): (rays, 2)."""
        cx, cy = self.centre
        angles = np.linspace(0.0, math.pi, rays)
        dirs = np.stack((np.sin(angles), np.cos(angles)), axis=1)
        dirs[0] = (0.0, 1.0)
        dirs[-1] = (0.0, -1.0)
        lo = np.zeros(rays)
        hi = np.full(rays, reach)
        origin = np.array((0.0, cy))
        for _ in range(iterations):
            mid = (lo + hi) * 0.5
            inside = self.sdf(origin + dirs * mid[:, None]) < 0.0
            lo = np.where(inside, mid, lo)
            hi = np.where(inside, hi, mid)
        pts = origin + dirs * ((lo + hi) * 0.5)[:, None]
        pts[0, 0] = 0.0
        pts[-1, 0] = 0.0
        return pts


def resample(half, count, corner=0.12):
    """count + 1 points along a half outline by arc length, with extra weight on turning (corner: metres of length
    one radian of turn is worth), so tight corners get more points."""
    seg = np.diff(half, axis=0)
    length = np.linalg.norm(seg, axis=1)
    ang = np.arctan2(seg[:, 1], seg[:, 0])
    turn = np.abs((np.diff(ang) + math.pi) % (2 * math.pi) - math.pi)
    vertex_turn = np.concatenate(([0.0], turn, [0.0]))
    weight = length + corner * 0.5 * (vertex_turn[:-1] + vertex_turn[1:])
    cum = np.concatenate(([0.0], np.cumsum(weight)))
    targets = np.linspace(0.0, cum[-1], count + 1)
    out = np.empty((count + 1, 2))
    for c in range(2):
        out[:, c] = np.interp(targets, cum, half[:, c])
    out[0, 0] = out[-1, 0] = 0.0
    return out


# ---------------------------------------------------------------------------------------------------------------------
# The skin mesh
# ---------------------------------------------------------------------------------------------------------------------

VIEWS = {
    # view: (u axis, v axis, depth axis, mirrored)
    "side": (2, 1, 0, False),
    "top": (0, 2, 1, True),
    "front": (0, 1, 2, True),
}


class Skin:
    def __init__(self, swatch):
        self.bm = bmesh.new()
        self.layer = self.bm.faces.layers.int.new("swatch")
        self.swatch = swatch
        self.rows = []

    # -- building -----------------------------------------------------------------------------------------------------
    def build(self, section_at, z_front, z_back, half_count=40, step=0.04, tolerance=0.0025, max_gap=0.45,
              nose=None, tail=None, corner=0.12, rays=241):
        """Traces sections from z_front back to z_back, keeps the rows the shape needs, and joins them into quads.
        section_at(z) -> Section. nose/tail: a tip point (x, y, z) closed with a fan, else a flat cap."""
        zs = list(np.arange(z_front, z_back, -step)) + [z_back]
        rings = []
        for z in zs:
            half = resample(section_at(z).trace(rays=rays), half_count, corner)
            full = np.concatenate((half, half[-2:0:-1] * np.array((-1.0, 1.0))))
            rings.append(np.column_stack((full, np.full(len(full), z))))
        keep = self._thin(rings, tolerance, max_gap, zs)
        rows = [rings[i] for i in keep]
        self.rows = [(zs[i], rings[i]) for i in keep]
        bm = self.bm
        vrows = [[bm.verts.new(Vector(p)) for p in ring] for ring in rows]
        n = len(vrows[0])
        for a, b in zip(vrows, vrows[1:]):
            for i in range(n):
                j = (i + 1) % n
                self._face([a[i], a[j], b[j], b[i]])
        for ring, tip, flip in ((vrows[0], nose, False), (vrows[-1], tail, True)):
            if tip is None:
                self._face(list(reversed(ring)) if not flip else ring)
            else:
                apex = bm.verts.new(Vector(tip))
                for i in range(n):
                    j = (i + 1) % n
                    self._face([ring[j], ring[i], apex] if not flip else [ring[i], ring[j], apex])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.normal_update()
        return self

    @staticmethod
    def _thin(rings, tolerance, max_gap, zs):
        keep = [0]
        i = 0
        last = len(rings) - 1
        while i < last:
            j = i + 1
            while j + 1 <= last and abs(zs[j + 1] - zs[i]) <= max_gap:
                cand = j + 1
                ok = True
                for k in range(i + 1, cand):
                    t = (zs[k] - zs[i]) / (zs[cand] - zs[i])
                    if np.abs(rings[k] - (rings[i] * (1 - t) + rings[cand] * t)).max() > tolerance:
                        ok = False
                        break
                if not ok:
                    break
                j = cand
            keep.append(j)
            i = j
        return keep

    def _face(self, verts, swatch=None):
        face = self.bm.faces.new(verts)
        face[self.layer] = self.swatch if swatch is None else swatch
        return face

    # -- projection helpers -------------------------------------------------------------------------------------------
    @staticmethod
    def _uv(co, view):
        u, v, _d, mirrored = VIEWS[view]
        a, b = co[u], co[v]
        if mirrored and u == 0:
            a = abs(a)
        return a, b

    @staticmethod
    def _depth(co, view):
        d = co[VIEWS[view][2]]
        return abs(d) if view == "side" else d

    @staticmethod
    def _facing_ok(normal, view, facing):
        if facing is None:
            return True
        axis = VIEWS[view][2]
        value = normal[axis]
        if view == "side":
            return abs(value) >= facing
        return value >= facing if facing >= 0 else value <= facing

    def _region(self, view, bounds, facing, depth, pool=None):
        """Faces (of pool, else all) whose projected box meets bounds (u0, v0, u1, v1) and that pass the facing and
        depth tests."""
        u0, v0, u1, v1 = bounds
        found = []
        for face in (self.bm.faces if pool is None else pool):
            if not face.is_valid or not self._facing_ok(face.normal, view, facing):
                continue
            if depth is not None and not (depth[0] <= self._depth(face.calc_center_median(), view) <= depth[1]):
                continue
            us, vs = zip(*(self._uv(v.co, view) for v in face.verts))
            if max(us) < u0 or min(us) > u1 or max(vs) < v0 or min(vs) > v1:
                continue
            found.append(face)
        return found

    def _planes(self, view, a, b):
        """Cut planes (co, no) through the 2D segment a-b in a view (two for mirrored views)."""
        u, v, d, mirrored = VIEWS[view]
        da = (b[0] - a[0], b[1] - a[1])
        co = [0.0, 0.0, 0.0]
        co[u], co[v] = a[0], a[1]
        direction = [0.0, 0.0, 0.0]
        direction[u], direction[v] = da[0], da[1]
        axis = [0.0, 0.0, 0.0]
        axis[d] = 1.0
        no = Vector(axis).cross(Vector(direction))
        planes = [(Vector(co), no.normalized())]
        if mirrored and u == 0:
            co2 = Vector(co)
            co2.x = -co2.x
            dir2 = Vector(direction)
            dir2.x = -dir2.x
            planes.append((co2, Vector(axis).cross(dir2).normalized()))
        return planes

    def _pool(self, view, bounds, margin, facing, depth):
        u0, v0, u1, v1 = bounds
        return set(self._region(view, (u0 - margin, v0 - margin, u1 + margin, v1 + margin), facing, depth))

    def _cut_segment(self, view, a, b, margin, facing, depth, pool):
        """Cuts the faces of pool along the segment (plane through it, limited to faces round the segment); pool is
        updated with the split faces."""
        if math.hypot(b[0] - a[0], b[1] - a[1]) < 1e-6:
            return
        bounds = (min(a[0], b[0]) - margin, min(a[1], b[1]) - margin,
                  max(a[0], b[0]) + margin, max(a[1], b[1]) + margin)
        for co, no in self._planes(view, a, b):
            faces = self._region(view, bounds, facing, depth, pool)
            if not faces:
                continue
            edges = {e for f in faces for e in f.edges}
            verts = {v for f in faces for v in f.verts}
            ret = bmesh.ops.bisect_plane(self.bm, geom=list(faces) + list(edges) + list(verts), dist=1e-5,
                                         plane_co=co, plane_no=no)
            for g in ret["geom"]:
                if isinstance(g, bmesh.types.BMFace):
                    g.normal_update()
                    pool.add(g)
        stale = [f for f in pool if not f.is_valid]
        for f in stale:
            pool.discard(f)

    def _faces_where(self, view, bounds, facing, depth, test, pool=None):
        out = []
        for face in self._region(view, bounds, facing, depth, pool):
            if test(self._uv(face.calc_center_median(), view)):
                out.append(face)
        return out

    # -- design operations --------------------------------------------------------------------------------------------
    def paint(self, view, polygon, swatch, facing=None, depth=None):
        """Faces inside a closed 2D polygon get the swatch; the polygon's edges are cut first (crisp boundary)."""
        polygon = simplify([tuple(p) for p in polygon], closed=True)
        bounds = _bounds(polygon)
        pool = self._pool(view, bounds, 0.05, facing, depth)
        for a, b in zip(polygon, polygon[1:] + polygon[:1]):
            self._cut_segment(view, a, b, 0.002, facing, depth, pool)
        faces = self._faces_where(view, bounds, facing, depth, lambda p: _inside(p, polygon), pool)
        for face in faces:
            face[self.layer] = swatch
        return faces

    def band(self, view, line, width, swatch, depth_cut=0.0, facing=None, depth=None, closed=False, ends=True):
        """A strip of the given width along a 2D polyline: cut both edges (and the middle), give it the swatch, and
        (depth_cut > 0) press it in as a shallow V: a panel line. depth_cut < 0 raises it (a rib)."""
        line = simplify([tuple(p) for p in line], closed=closed)
        if closed:
            line = line + line[:1]
        half = width / 2.0
        pool = self._pool(view, _bounds(line), width + 0.05, facing, depth)
        for a, b in zip(line, line[1:]):
            n = _normal2(a, b)
            for off in ((-half, 0.0, half) if depth_cut else (-half, half)):
                pa = (a[0] + n[0] * off, a[1] + n[1] * off)
                pb = (b[0] + n[0] * off, b[1] + n[1] * off)
                self._cut_segment(view, pa, pb, half + 0.002, facing, depth, pool)
        if ends and not closed:
            for p, q in ((line[0], line[1]), (line[-1], line[-2])):
                n = _normal2(p, q)
                self._cut_segment(view, (p[0] - n[0] * half, p[1] - n[1] * half),
                                  (p[0] + n[0] * half, p[1] + n[1] * half), 0.002, facing, depth, pool)
        us, vs = zip(*line)
        bounds = (min(us) - width, min(vs) - width, max(us) + width, max(vs) + width)

        def dist(p):
            return min(_seg_dist(p, a, b) for a, b in zip(line, line[1:]))

        faces = self._faces_where(view, bounds, facing, depth, lambda p: dist(p) < half, pool)
        for face in faces:
            face[self.layer] = swatch
        if depth_cut:
            self.bm.normal_update()
            moved = {}
            for face in faces:
                for vert in face.verts:
                    if vert in moved:
                        continue
                    d = dist(self._uv(vert.co, view))
                    if d < half - 1e-5:
                        moved[vert] = vert.normal.copy() * (depth_cut * (1.0 - d / half))
            for vert, offset in moved.items():
                vert.co -= offset
        return faces

    def groove(self, view, line, width=0.014, depth_cut=0.0, swatch=None, **kw):
        """A panel line: a narrow dark strip, flush by default (a pressed V dents the smooth shading of a game-res
        mesh; the depth belongs in a baked normal map)."""
        return self.band(view, line, width, swatch, depth_cut=depth_cut, **kw)

    def recess(self, view, polygon, swatch, depth_cut, wall=0.03, rim=None, rim_width=0.025, facing=None,
               depth=None):
        """Faces inside a closed polygon are pressed in by depth_cut (a wall wide `wall` round the edge) and get the
        swatch; rim: a flush frame band round it."""
        polygon = [tuple(p) for p in polygon]
        inner = offset(polygon, -wall)
        self.paint(view, polygon, swatch, facing, depth)
        pool = self._pool(view, _bounds(polygon), 0.05, facing, depth)
        for a, b in zip(inner, inner[1:] + inner[:1]):
            self._cut_segment(view, a, b, 0.002, facing, depth, pool)
        faces = self._faces_where(view, _bounds(polygon), facing, depth, lambda p: _inside(p, polygon), pool)
        for face in faces:
            face[self.layer] = swatch
        self.bm.normal_update()
        moved = {}
        for face in faces:
            for vert in face.verts:
                if vert in moved:
                    continue
                p = self._uv(vert.co, view)
                if not _inside(p, polygon):
                    continue
                d = min(_seg_dist(p, a, b) for a, b in zip(polygon, polygon[1:] + polygon[:1]))
                share = min(1.0, d / wall)
                if share > 1e-4:
                    moved[vert] = vert.normal.copy() * (depth_cut * share)
        for vert, off in moved.items():
            vert.co -= off
        if rim is not None:
            self.band(view, offset(polygon, rim_width / 2.0), rim_width, rim, facing=facing, depth=depth, closed=True)
        return faces

    def vents(self, view, centre, size, count, swatch, depth_cut=0.0, angle=0.0, facing=None, depth=None):
        """A grille: `count` parallel slots in a box (u, v) of `size` round centre, tilted by angle (degrees)."""
        w, h = size
        ca, sa = math.cos(math.radians(angle)), math.sin(math.radians(angle))
        pitch = h / count
        for i in range(count):
            off = -h / 2 + pitch * (i + 0.5)
            a = (centre[0] + (-w / 2) * ca - off * sa, centre[1] + (-w / 2) * sa + off * ca)
            b = (centre[0] + (w / 2) * ca - off * sa, centre[1] + (w / 2) * sa + off * ca)
            self.band(view, [a, b], pitch * 0.5, swatch, depth_cut=depth_cut, facing=facing, depth=depth)

    # -- output -------------------------------------------------------------------------------------------------------
    def into(self, builder):
        """Copies the skin into a Builder (its faces keep their swatches)."""
        bm = self.bm
        bm.normal_update()
        if os.environ.get("PF_SKIN_STATS"):
            print("SKIN rows", len(self.rows), "ring", len(self.rows[0][1]) if self.rows else 0, "faces", len(bm.faces),
                  "tris", sum(len(f.verts) - 2 for f in bm.faces))
        verts = {v: builder.bm.verts.new(v.co.copy()) for v in bm.verts}
        for face in bm.faces:
            new = builder.bm.faces.new([verts[v] for v in face.verts])
            new[builder.swatch] = face[self.layer]
        bm.free()


# ---------------------------------------------------------------------------------------------------------------------
# 2D helpers
# ---------------------------------------------------------------------------------------------------------------------

def _inside(p, polygon):
    x, y = p
    inside = False
    n = len(polygon)
    for i in range(n):
        ax, ay = polygon[i]
        bx, by = polygon[(i + 1) % n]
        if (ay > y) != (by > y):
            if x < ax + (y - ay) * (bx - ax) / (by - ay):
                inside = not inside
    return inside


def _seg_dist(p, a, b):
    ax, ay = a
    dx, dy = b[0] - ax, b[1] - ay
    ll = dx * dx + dy * dy
    t = 0.0 if ll < 1e-12 else max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - ay) * dy) / ll))
    ex, ey = p[0] - (ax + t * dx), p[1] - (ay + t * dy)
    return math.sqrt(ex * ex + ey * ey)


def _normal2(a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    ll = math.hypot(dx, dy) or 1.0
    return (-dy / ll, dx / ll)


def _bounds(polygon):
    us, vs = zip(*polygon)
    return (min(us), min(vs), max(us), max(vs))


def offset(polygon, distance):
    """A closed polygon moved outward by distance (negative = inward), mitred corners."""
    pts = [tuple(p) for p in polygon]
    area = sum(a[0] * b[1] - b[0] * a[1] for a, b in zip(pts, pts[1:] + pts[:1]))
    sign = 1.0 if area > 0 else -1.0  # counter-clockwise: outward normal is (dy, -dx)
    out = []
    n = len(pts)
    for i in range(n):
        p0, p1, p2 = pts[i - 1], pts[i], pts[(i + 1) % n]
        n1 = _normal2(p0, p1)
        n2 = _normal2(p1, p2)
        # _normal2 is the left normal; outward for a counter-clockwise polygon is the right one.
        n1 = (-n1[0] * sign, -n1[1] * sign)
        n2 = (-n2[0] * sign, -n2[1] * sign)
        mx, my = n1[0] + n2[0], n1[1] + n2[1]
        ml = math.hypot(mx, my) or 1.0
        mx, my = mx / ml, my / ml
        cos = max(0.35, mx * n1[0] + my * n1[1])
        out.append((p1[0] + mx * distance / cos, p1[1] + my * distance / cos))
    return out


def sample(track_y, z0, z1, step=0.1):
    """A side-view polyline (z, y) along a track from z0 to z1."""
    n = max(1, int(abs(z1 - z0) / step))
    return [(z0 + (z1 - z0) * i / n, track_y(z0 + (z1 - z0) * i / n)) for i in range(n + 1)]


def simplify(line, tolerance=0.0015, closed=False):
    """Fewer points on a polyline (Douglas-Peucker): every cut costs faces, so a smooth line keeps only the points
    it needs."""
    pts = [(float(p[0]), float(p[1])) for p in line]
    if len(pts) < 4:
        return pts
    if closed:
        # Split a closed loop at its farthest pair so both halves keep their ends.
        far = max(range(1, len(pts)), key=lambda i: math.hypot(pts[i][0] - pts[0][0], pts[i][1] - pts[0][1]))
        a = simplify(pts[:far + 1], tolerance)
        b = simplify(pts[far:] + pts[:1], tolerance)
        return a[:-1] + b[:-1]
    keep = [False] * len(pts)
    keep[0] = keep[-1] = True
    stack = [(0, len(pts) - 1)]
    while stack:
        i, j = stack.pop()
        best, index = 0.0, None
        for k in range(i + 1, j):
            d = _seg_dist(pts[k], pts[i], pts[j])
            if d > best:
                best, index = d, k
        if index is not None and best > tolerance:
            keep[index] = True
            stack += [(i, index), (index, j)]
    return [p for p, k in zip(pts, keep) if k]


def round_rect(cu, cv, hw, hh, r, arc=2):
    """A rounded rectangle polygon (for hatches) in a view's (u, v)."""
    pts = rounded([(cu + hw, cv + hh), (cu + hw, cv - hh), (cu - hw, cv - hh), (cu - hw, cv + hh)], [r] * 4, arc)
    return [tuple(p) for p in pts]


def ellipse_points(cu, cv, ru, rv, count=20):
    return [(cu + ru * math.cos(2 * math.pi * i / count), cv + rv * math.sin(2 * math.pi * i / count))
            for i in range(count)]
