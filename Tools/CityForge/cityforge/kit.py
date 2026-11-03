"""
Building blocks shared by every building type: the Building (three LOD meshes, box colliders, gameplay markers) and the
parts that dress it (facades, shop fronts, roof props, pads, signs, lights).

Everything is in Unity space (x right, y up, z forward, metres; PartForge's geo.Builder maps to Blender at the end).
A part goes into the LOD levels named by `at`: L0 = full detail only, L01 = full and medium, ALL = every level.
LOD2 is the far mesh: one box per mass, nothing else.
"""
import math
import random

from mathutils import Matrix, Vector

from .pf import geo, palette
from .styles import SWATCH

L0, L01, ALL, L1, L2 = (0,), (0, 1), (0, 1, 2), (1,), (2,)
LOD_COUNT = 3

# Forms for the PartForge builder: hard boxes (no rounding), no smoothing, cheap cylinders.
CITY_FORM = dict(corner_segments=1, loft_smooth=0, curve_detail=1.0, edge_radius=0.0, edge_segments=1,
                 edge_profile=0.5, box_rounding=0.0, box_min=0.1, sharp_angle=35.0)


def sw(name):
    return palette.swatch(SWATCH.get(name, name))


class Rect:
    """Axis rectangle on the ground plane (x0..x1, z0..z1)."""

    def __init__(self, x0, x1, z0, z1):
        self.x0, self.x1, self.z0, self.z1 = min(x0, x1), max(x0, x1), min(z0, z1), max(z0, z1)

    @classmethod
    def centred(cls, cx, cz, w, d):
        return cls(cx - w / 2, cx + w / 2, cz - d / 2, cz + d / 2)

    w = property(lambda self: self.x1 - self.x0)
    d = property(lambda self: self.z1 - self.z0)
    cx = property(lambda self: (self.x0 + self.x1) / 2)
    cz = property(lambda self: (self.z0 + self.z1) / 2)

    def inset(self, m):
        return Rect(self.x0 + m, self.x1 - m, self.z0 + m, self.z1 - m)

    def overlaps(self, other, gap=0.0):
        return (self.x0 < other.x1 + gap and other.x0 < self.x1 + gap
                and self.z0 < other.z1 + gap and other.z0 < self.z1 + gap)

    def contains(self, x, z, margin=0.0):
        return self.x0 - margin <= x <= self.x1 + margin and self.z0 - margin <= z <= self.z1 + margin

    def __repr__(self):
        return f"Rect({self.x0:.1f}..{self.x1:.1f}, {self.z0:.1f}..{self.z1:.1f})"


class Face:
    """One side of a rectangular mass. u runs along the side (0..length, left to right seen from outside), y is
    height, o is the distance out from the wall (negative = in)."""

    def __init__(self, name, ox, oz, tx, tz, nx, nz, length):
        self.name, self.ox, self.oz = name, ox, oz
        self.tx, self.tz, self.nx, self.nz, self.length = tx, tz, nx, nz, length

    def point(self, u, y, o):
        return (self.ox + self.tx * u + self.nx * o, y, self.oz + self.tz * u + self.nz * o)

    def size(self, a, h, d):
        """World box size of a part a wide (along the side), h high, d deep (out from the wall)."""
        return (abs(self.tx) * a + abs(self.nx) * d, h, abs(self.tz) * a + abs(self.nz) * d)

    @property
    def normal(self):
        return Vector((self.nx, 0.0, self.nz))

    @property
    def tangent(self):
        return Vector((self.tx, 0.0, self.tz))


def faces(rect):
    """The four sides of a rectangle: front (+Z), right (+X), back (-Z), left (-X). For each, normal x tangent = up."""
    return {
        "front": Face("front", rect.x0, rect.z1, 1, 0, 0, 1, rect.w),
        "right": Face("right", rect.x1, rect.z1, 0, -1, 1, 0, rect.d),
        "back": Face("back", rect.x1, rect.z0, -1, 0, 0, -1, rect.w),
        "left": Face("left", rect.x0, rect.z0, 0, 1, -1, 0, rect.d),
    }


class Building:
    """Three LOD meshes, box colliders and markers for one building spec."""

    def __init__(self, spec, style):
        self.spec, self.style = spec, style
        self.name = spec["name"]
        self.type = spec["type"]
        self.rng = random.Random(spec["seed"])
        self.lods = [geo.Builder() for _ in range(LOD_COUNT)]
        self.colliders = []   # (centre, size, yaw): yaw = Unity euler Y in degrees (local +X turns to (cos, 0, -sin))
        self.markers = []     # (kind, index, position, radius)
        self.thin = []        # (x, z, top): masts and spires allowed above every collider
        plot = spec["plot"]
        self.plot = Rect.centred(0.0, 0.0, plot[0], plot[1])

    # -- primitives -------------------------------------------------------------------------------------------------
    def box(self, centre, size, swatch, at=L0, rotation=None, swatches=None):
        if min(size) <= 0:
            return
        index = sw(swatch)
        sides = {key: sw(value) for key, value in swatches.items()} if swatches else None
        for level in at:
            self.lods[level].box(tuple(centre), tuple(size), index, rotation=rotation, swatches=sides)

    def span(self, x0, x1, y0, y1, z0, z1, swatch, at=L0, swatches=None):
        self.box(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)), swatch,
                 at, swatches=swatches)

    def on_face(self, face, u, y, o, a, h, d, swatch, at=L0):
        """A box on a wall: centred at u along the side, height y, o out from the wall; a wide, h high, d deep."""
        self.box(face.point(u, y, o), face.size(a, h, d), swatch, at)

    def cylinder(self, start, end, radius, swatch, segments=12, at=L0, end_radius=None, cap_end=None):
        for level in at:
            count = segments if level == 0 else max(6, segments // 2)
            self.lods[level].cylinder(tuple(start), tuple(end), radius, sw(swatch), segments=count,
                                      end_radius=end_radius, cap_end=sw(cap_end) if cap_end else None)

    def beam(self, a, b, thickness, swatch, at=L0):
        """A thin square bar from point a to point b (cables, braces, stairs)."""
        a, b = Vector(a), Vector(b)
        axis = b - a
        if axis.length < 1e-3:
            return
        rotation = axis.to_track_quat("Z", "Y").to_matrix()
        self.box(tuple((a + b) / 2), (thickness, thickness, axis.length), swatch, at, rotation=rotation)

    def prism(self, profile, axis, a0, a1, swatches, cap, at=L0):
        """A 2D outline extruded along an axis. axis "z": profile points are (x, y), extruded from z = a0 to a1;
        axis "x": points are (z, y), extruded along x. swatches: one per outline edge (edge i = point i to i+1)."""
        def point(p, y, a):
            return (p, y, a) if axis == "z" else (a, y, p)

        for level in at:
            builder = self.lods[level]
            first = builder._verts([point(p, y, a0) for p, y in profile])
            last = builder._verts([point(p, y, a1) for p, y in profile])
            count = len(profile)
            for i in range(count):
                j = (i + 1) % count
                builder._face([first[i], first[j], last[j], last[i]], sw(swatches[i % len(swatches)]))
            builder._face(list(reversed(first)), sw(cap))
            builder._face(last, sw(cap))

    # -- game data --------------------------------------------------------------------------------------------------
    def collider(self, x0, x1, y0, y1, z0, z1, yaw=0.0):
        self.colliders.append((((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (abs(x1 - x0), abs(y1 - y0), abs(z1 - z0)),
                               yaw))

    def mass(self, rect, y0, y1, swatch="wall", at=L2, collide=True, top=None):
        """A main volume: its far-LOD box (`at`, default LOD2 only) and its collider."""
        if at:
            self.span(rect.x0, rect.x1, y0, y1, rect.z0, rect.z1, swatch, at, swatches={"top": top} if top else None)
        if collide:
            self.collider(rect.x0, rect.x1, y0, y1, rect.z0, rect.z1)

    def round_collider(self, cx, cz, radius, y0, y1):
        """Three boxes round a cylinder (a square with its corners on the circle and a cross): at most 3 % outside the
        circle, at most 14 % inside it."""
        y, h = (y0 + y1) / 2, y1 - y0
        for w, d in ((1.414, 1.414), (1.9, 0.8), (0.8, 1.9)):
            self.colliders.append(((cx, y, cz), (w * radius, h, d * radius), 0.0))

    def marker(self, kind, position, radius=0.0):
        index = sum(1 for m in self.markers if m[0] == kind)
        self.markers.append((kind, index, tuple(position), radius))

    def lit_swatch(self, rng=None):
        rng = rng or self.rng
        return "lit_cool" if rng.random() < self.style["lit_cool"] else "lit_warm"


# -- facades ---------------------------------------------------------------------------------------------------------

def shopfront(b, face, height, lit=None, signs=True):
    """Street level: glass bays (many lit), piers, awnings with a sodium strip under the front edge, neon shop signs.
    The bright band at ground level is the main ground cue at night."""
    rng, style = b.rng, b.style
    lit = style["shop_lit"] if lit is None else lit
    count = max(1, round(face.length / rng.uniform(4.6, 6.4)))
    bay = face.length / count
    glass_h = min(3.0, height - 1.6)
    for i in range(count):
        u = (i + 0.5) * bay
        on = rng.random() < lit
        b.on_face(face, u, 0.25 + glass_h / 2, 0.03, bay - 0.9, glass_h, 0.08, b.lit_swatch() if on else "glass", L01)
        if rng.random() < 0.8:
            b.on_face(face, u, glass_h + 0.55, 0.7, bay - 0.4, 0.16, 1.4, "dark")
            b.on_face(face, u, glass_h + 0.43, 1.32, bay - 0.6, 0.08, 0.12, "sodium" if on else "dark", L01)
        if signs and rng.random() < style["neon"] * 0.6 and height > glass_h + 1.6:
            colour = rng.choice(("neon_pink", "neon_cyan", "neon_green", "sodium"))
            width = bay * rng.uniform(0.35, 0.7)
            wall_sign(b, face, u, glass_h + 1.2, width, colour)
    b.on_face(face, face.length / 2, height - 0.25, 0.15, face.length, 0.5, 0.3, "trim", L01)


def service_side(b, face, height):
    """Back of the street floor: doors, pipes, a lamp over each door."""
    rng = b.rng
    count = max(1, int(face.length // 9))
    for i in range(count):
        u = (i + 0.5) * face.length / count + rng.uniform(-1.5, 1.5)
        b.on_face(face, u, 1.2, 0.04, 1.6, 2.4, 0.1, "dark")
        b.on_face(face, u, 2.75, 0.2, 0.5, 0.15, 0.4, "sodium", L01)
    b.on_face(face, face.length / 2, height - 0.25, 0.15, face.length, 0.5, 0.3, "trim", L01)


def punched(b, face, y0, floors, storey, spacing=3.2, win=(1.5, 1.7), sill=0.95, boarded=0.0, balconies=0.0,
            units=0.12):
    """Wall with punched windows: a sill band per floor (the storey line), glass panes, a few lit, some boarded;
    optional balconies and wall air-conditioning units. LOD1 gets one glass band per floor."""
    rng, style = b.rng, b.style
    count = max(1, int((face.length - 1.2) // spacing))
    margin = (face.length - count * spacing) / 2
    for f in range(floors):
        yf = y0 + f * storey
        b.on_face(face, face.length / 2, yf + sill - 0.09, 0.07, face.length, 0.18, 0.14, "trim")
        # LOD1: one glass band per floor, as dark on average as the row of windows it stands for.
        band = win[1] * min(1.0, win[0] / spacing)
        b.on_face(face, face.length / 2, yf + sill + win[1] / 2, 0.015, face.length - 1.0, band, 0.05, "glass", L1)
        for i in range(count):
            u = margin + spacing * (i + 0.5)
            roll = rng.random()
            if roll < boarded * style["wear"]:
                swatch = rng.choice(("primer", "rust", "dark"))
            elif rng.random() < style["lit"]:
                swatch = b.lit_swatch()
            else:
                swatch = "glass"
            b.on_face(face, u, yf + sill + win[1] / 2, 0.015, win[0], win[1], 0.05, swatch)
            if f > 0 and rng.random() < balconies:
                b.on_face(face, u, yf + 0.1, 0.55, win[0] + 1.1, 0.2, 1.1, "concrete")
                b.on_face(face, u, yf + 0.7, 1.07, win[0] + 1.1, 1.0, 0.06, rng.choice(("metal", "dark", "rust")))
            elif rng.random() < units:
                b.on_face(face, u, yf + sill - 0.5, 0.3, 0.9, 0.6, 0.62, "steel")


def ribbon(b, rect, y0, floors, storey, inset=0.5, band=1.0):
    """Glass core behind a concrete band on every floor line (strong storey lines), corner columns, a few lit
    stretches of window. Returns the roof height."""
    rng, style = b.rng, b.style
    top = y0 + floors * storey
    core = rect.inset(inset)
    b.span(core.x0, core.x1, y0, top, core.z0, core.z1, "glass", L01)
    for f in range(floors + 1):
        y = y0 + f * storey
        lo, hi = (y0, y0 + band / 2) if f == 0 else ((top - 0.9, top) if f == floors else (y - band / 2, y + band / 2))
        b.span(rect.x0, rect.x1, lo, hi, rect.z0, rect.z1, "wall", L01, swatches={"top": "tar"} if f == floors else None)
    column = max(0.8, inset * 2)
    for x in (rect.x0 + column / 2, rect.x1 - column / 2):
        for z in (rect.z0 + column / 2, rect.z1 - column / 2):
            b.span(x - column / 2, x + column / 2, y0, top, z - column / 2, z + column / 2, "wall", L01)
    _lit_strips(b, rect, y0, floors, storey, inset, band)
    return top


def grid(b, rect, y0, floors, storey, inset=0.35, band=0.5, fin_spacing=3.0):
    """Glass core, thin floor bands and vertical fins: a gridded office facade. Returns the roof height."""
    top = ribbon(b, rect, y0, floors, storey, inset=inset, band=band)
    for face in faces(rect).values():
        count = max(1, int(face.length // fin_spacing))
        step = face.length / count
        for i in range(1, count):
            b.on_face(face, i * step, (y0 + top) / 2, -inset / 2 + 0.15, 0.32, top - y0, inset + 0.3, "trim")
    return top


def _lit_strips(b, rect, y0, floors, storey, inset, band):
    rng, style = b.rng, b.style
    for face in faces(rect).values():
        usable = face.length - 2.0
        for f in range(floors):
            if rng.random() > style["lit"] * 3.2:
                continue
            width = min(usable, rng.uniform(2.0, 8.0))
            u = rng.uniform(1.0 + width / 2, face.length - 1.0 - width / 2)
            y = y0 + f * storey + storey / 2 + (band / 4 if f == 0 else 0)
            b.on_face(face, u, y, -inset + 0.03, width, storey - band - 0.5, 0.05, b.lit_swatch(), L01)


def pipes(b, face, y0, y1, count=2):
    """Vertical service pipes down a wall."""
    rng = b.rng
    for _ in range(count):
        u = rng.uniform(1.0, face.length - 1.0)
        size = rng.choice((0.25, 0.35, 0.45))
        b.on_face(face, u, (y0 + y1) / 2, size / 2 - 0.02, size, y1 - y0, size, rng.choice(("metal", "rust", "steel")))


def wall_sign(b, face, u, y, width, colour):
    """Small shop sign flat on a wall: a dark board with a neon bar."""
    b.on_face(face, u, y, 0.09, width, 0.7, 0.2, "dark")
    b.on_face(face, u, y, 0.21, width * 0.9, 0.14, 0.06, colour, L01)


def blade_sign(b, face, y0, height, u=None):
    """Tall vertical sign standing off a wall near a corner, a neon outer edge and lit panels on both sides (an 80s
    street landmark)."""
    rng = b.rng
    u = 1.4 if u is None else u
    colour = rng.choice(("neon_pink", "neon_cyan"))
    depth = 1.8
    centre_y = y0 + height / 2
    b.on_face(face, u, centre_y, depth / 2 - 0.02, 0.35, height, depth, "dark", L01)
    b.on_face(face, u, centre_y, depth + 0.03, 0.42, height, 0.1, colour, L01)
    count = rng.randint(3, 5)
    for k in range(count):
        y = y0 + height * (k + 0.5) / count
        b.on_face(face, u, y, depth * 0.55, 0.45, height / count * 0.6, depth * 0.8, rng.choice(("lit_warm", colour)))
    b.on_face(face, u, y0 - 0.3, 0.3, 0.3, 0.6, 0.64, "metal")


def fire_escape(b, face, y0, floors, storey, u=None):
    """Steel platforms on every floor with stairs between them, zig-zagging up a side wall."""
    u = face.length / 2 if u is None else u
    run = 3.0
    for f in range(1, floors):
        y = y0 + f * storey
        b.on_face(face, u, y + 0.06, 0.85, 4.4, 0.12, 1.7, "metal")
        b.on_face(face, u, y + 0.6, 1.68, 4.4, 1.0, 0.05, "metal")
        if f < floors - 1:
            sign = 1 if f % 2 else -1
            start = face.point(u - sign * run / 2, y + 0.12, 0.85)
            end = face.point(u + sign * run / 2, y + storey, 0.85)
            angle = math.atan2(storey, run)
            rotation = Matrix.Rotation(sign * angle, 3, face.normal)
            length = math.hypot(run, storey)
            centre = tuple((Vector(start) + Vector(end)) / 2)
            b.box(centre, face.size(length, 0.1, 0.9), "metal", L0, rotation=rotation)


def ladder(b, x, z, y0, y1, facing, width=0.5, rung=0.9):
    """Two rails and rungs. facing: (nx, nz) the wall normal (the ladder stands 0.25 m off the wall)."""
    nx, nz = facing
    tx, tz = -nz, nx
    for side in (-1, 1):
        cx, cz = x + tx * side * width / 2 + nx * 0.25, z + tz * side * width / 2 + nz * 0.25
        b.span(cx - 0.03, cx + 0.03, y0, y1, cz - 0.03, cz + 0.03, "metal")
    count = int((y1 - y0) / rung)
    for k in range(1, count + 1):
        y = y0 + k * rung
        cx, cz = x + nx * 0.25, z + nz * 0.25
        b.box((cx, y, cz), (abs(tx) * width + 0.04, 0.04, abs(tz) * width + 0.04), "metal")


# -- roofs -----------------------------------------------------------------------------------------------------------

class Placer:
    """Free spots on a roof: random rectangles that keep clear of blocked areas and of each other."""

    def __init__(self, rng, rect, margin=1.0):
        self.rng, self.free, self.taken = rng, rect.inset(margin), []

    def block(self, rect):
        self.taken.append(rect)

    def place(self, w, d, gap=0.7, tries=40):
        if self.free.w < w or self.free.d < d:
            return None
        for _ in range(tries):
            cx = self.rng.uniform(self.free.x0 + w / 2, self.free.x1 - w / 2)
            cz = self.rng.uniform(self.free.z0 + d / 2, self.free.z1 - d / 2)
            rect = Rect.centred(cx, cz, w, d)
            if not any(rect.overlaps(other, gap) for other in self.taken):
                self.taken.append(rect)
                return rect
        return None


def parapet(b, rect, y, height=1.0, thick=0.35, at=L0):
    """Low wall round a roof edge, with a cap."""
    for x0, x1, z0, z1 in ((rect.x0, rect.x1, rect.z1 - thick, rect.z1), (rect.x0, rect.x1, rect.z0, rect.z0 + thick),
                           (rect.x0, rect.x0 + thick, rect.z0 + thick, rect.z1 - thick),
                           (rect.x1 - thick, rect.x1, rect.z0 + thick, rect.z1 - thick)):
        b.span(x0, x1, y, y + height, z0, z1, "wall", at, swatches={"top": "trim"})


def obstruction_lights(b, rect, y):
    """Red lights on the corners of a high roof: aviation markers and a height cue for the pilot."""
    if y < b.style["obstruction_height"]:
        return
    for x in (rect.x0 + 0.3, rect.x1 - 0.3):
        for z in (rect.z0 + 0.3, rect.z1 - 0.3):
            b.box((x, y + 0.2, z), (0.4, 0.4, 0.4), "warning", L01)


def vent(b, rect, y):
    rng = b.rng
    h = rng.uniform(0.6, 1.4)
    b.span(rect.x0, rect.x1, y, y + h, rect.z0, rect.z1, rng.choice(("metal", "steel", "rust")))
    b.span(rect.x0 - 0.1, rect.x1 + 0.1, y + h, y + h + 0.12, rect.z0 - 0.1, rect.z1 + 0.1, "metal")


def ac_unit(b, rect, y):
    b.span(rect.x0, rect.x1, y, y + 1.1, rect.z0, rect.z1, "steel")
    fan = rect.inset(min(rect.w, rect.d) * 0.18)
    b.span(fan.x0, fan.x1, y + 1.1, y + 1.16, fan.z0, fan.z1, "dark")


def stair_hut(b, rect, y):
    b.span(rect.x0, rect.x1, y, y + 2.9, rect.z0, rect.z1, "wall", ALL, swatches={"top": "tar"})
    b.collider(rect.x0, rect.x1, y, y + 2.9, rect.z0, rect.z1)
    b.span(rect.cx - 0.5, rect.cx + 0.5, y, y + 2.1, rect.z1, rect.z1 + 0.06, "dark")
    b.span(rect.cx - 0.2, rect.cx + 0.2, y + 2.3, y + 2.45, rect.z1, rect.z1 + 0.3, "sodium", L01)


def water_tank(b, rect, y):
    """Tank on a steel stand (old housing blocks)."""
    rng = b.rng
    r = min(rect.w, rect.d) / 2 - 0.1
    leg = 2.0
    for x in (rect.cx - r * 0.6, rect.cx + r * 0.6):
        for z in (rect.cz - r * 0.6, rect.cz + r * 0.6):
            b.span(x - 0.08, x + 0.08, y, y + leg, z - 0.08, z + 0.08, "metal")
    b.span(rect.cx - r * 0.8, rect.cx + r * 0.8, y + leg - 0.12, y + leg, rect.cz - r * 0.8, rect.cz + r * 0.8, "metal")
    height = rng.uniform(2.0, 2.8)
    swatch = rng.choice(("rust", "dark", "wall", "steel"))
    b.cylinder((rect.cx, y + leg, rect.cz), (rect.cx, y + leg + height, rect.cz), r, swatch, segments=12, at=ALL)
    b.round_collider(rect.cx, rect.cz, r, y, y + leg + height + r * 0.5)
    b.cylinder((rect.cx, y + leg + height, rect.cz), (rect.cx, y + leg + height + r * 0.5, rect.cz), r, swatch,
               segments=12, end_radius=0.15, at=L01)


def mast(b, x, z, y, height, lights=True, arms=True):
    """Thin antenna mast with cross arms and red lights at the top and half way."""
    b.span(x - 0.4, x + 0.4, y, y + 0.5, z - 0.4, z + 0.4, "metal")
    b.cylinder((x, y, z), (x, y + height, z), 0.16, "steel", segments=6, at=L01, end_radius=0.08)
    if arms:
        for k in range(1, 3):
            arm_y = y + height * (0.45 + 0.25 * k)
            b.span(x - 0.9, x + 0.9, arm_y, arm_y + 0.08, z - 0.04, z + 0.04, "steel")
    if lights:
        b.box((x, y + height + 0.15, z), (0.3, 0.3, 0.3), "warning", L01)
        if height > 10:
            b.box((x, y + height * 0.5, z + 0.18), (0.24, 0.24, 0.12), "warning", L01)
    b.thin.append((x, z, y + height + 0.6))


def dish(b, x, z, y):
    b.span(x - 0.1, x + 0.1, y, y + 1.0, z - 0.1, z + 0.1, "metal")
    b.cylinder((x, y + 1.0, z), (x, y + 1.45, z + 0.35), 0.75, "trim", segments=10, end_radius=0.2)


def helipad(b, rect, y):
    """Landing pad: deck on a plinth, yellow edge, an H, sodium edge lights. Adds a RoofPad marker (the clear circle
    the audit checks) and a collider."""
    size = min(rect.w, rect.d)
    pad = Rect.centred(rect.cx, rect.cz, size, size)
    lift = 0.6
    b.span(pad.x0 + 0.6, pad.x1 - 0.6, y, y + lift, pad.z0 + 0.6, pad.z1 - 0.6, "concrete", L01)
    deck = y + lift + 0.3
    b.span(pad.x0, pad.x1, y + lift, deck, pad.z0, pad.z1, "dark", L01)
    edge = 0.35
    for x0, x1, z0, z1 in ((pad.x0, pad.x1, pad.z1 - edge, pad.z1), (pad.x0, pad.x1, pad.z0, pad.z0 + edge),
                           (pad.x0, pad.x0 + edge, pad.z0 + edge, pad.z1 - edge),
                           (pad.x1 - edge, pad.x1, pad.z0 + edge, pad.z1 - edge)):
        b.span(x0, x1, deck, deck + 0.03, z0, z1, "hazard")
    h = size * 0.36
    bar = size * 0.07
    for dx in (-h / 2, h / 2):
        b.span(pad.cx + dx - bar / 2, pad.cx + dx + bar / 2, deck, deck + 0.03, pad.cz - h / 2, pad.cz + h / 2, "trim", L01)
    b.span(pad.cx - h / 2, pad.cx + h / 2, deck, deck + 0.03, pad.cz - bar / 2, pad.cz + bar / 2, "trim", L01)
    count = 3
    for i in range(count + 1):
        t = i / count
        for x, z in ((pad.x0 + t * size, pad.z0 + 0.15), (pad.x0 + t * size, pad.z1 - 0.15),
                     (pad.x0 + 0.15, pad.z0 + t * size), (pad.x1 - 0.15, pad.z0 + t * size)):
            b.box((x, deck + 0.08, z), (0.22, 0.16, 0.22), "sodium", L01)
    b.collider(pad.x0, pad.x1, y, deck, pad.z0, pad.z1)
    b.marker("RoofPad", (pad.cx, deck, pad.cz), size / 2)
    return pad


def roof_sign(b, rect, y, width=None, height=None):
    """Billboard on posts facing +Z, neon frame and lit text bars. Adds a collider for the board."""
    rng = b.rng
    width = width or min(rect.w * 0.8, 16.0)
    height = height or width * 0.32
    post_h = 2.6
    z = rect.cz
    for k in range(3):
        x = rect.cx - width / 2 + 0.4 + k * (width - 0.8) / 2
        b.span(x - 0.18, x + 0.18, y, y + post_h, z - 0.18, z + 0.18, "metal")
        b.beam((x, y, z - 1.4), (x, y + post_h, z), 0.12, "metal")
    y0, y1 = y + post_h, y + post_h + height
    b.span(rect.cx - width / 2, rect.cx + width / 2, y0, y1, z - 0.25, z + 0.25, "dark", ALL)
    colour = rng.choice(("neon_pink", "neon_cyan"))
    for yy in (y0 + 0.1, y1 - 0.1):
        b.span(rect.cx - width / 2, rect.cx + width / 2, yy - 0.1, yy + 0.1, z + 0.25, z + 0.35, colour, L01)
    for xx in (rect.cx - width / 2 + 0.1, rect.cx + width / 2 - 0.1):
        b.span(xx - 0.1, xx + 0.1, y0, y1, z + 0.25, z + 0.35, colour, L01)
    rows = 2
    for r in range(rows):
        yy = y0 + height * (0.33 + 0.34 * r)
        x = rect.cx - width / 2 + 0.8
        while x < rect.cx + width / 2 - 1.2:
            length = rng.uniform(0.6, 2.2)
            length = min(length, rect.cx + width / 2 - 0.8 - x)
            b.span(x, x + length, yy - height * 0.1, yy + height * 0.1, z + 0.25, z + 0.3,
                   rng.choice(("lit_warm", "lit_cool", colour)), L0)
            x += length + rng.uniform(0.3, 0.9)
    b.collider(rect.cx - width / 2, rect.cx + width / 2, y0, y1, z - 0.25, z + 0.35)


def dress_roof(b, rect, y, blocked=(), density=1.0, kinds=("vent", "ac", "vent", "ac", "hut"), margin=1.0):
    """Scatter roof props over a flat roof, clear of the blocked rectangles."""
    rng = b.rng
    placer = Placer(rng, rect, margin)
    for other in blocked:
        placer.block(other)
    count = int(rect.w * rect.d / 55 * density * b.style["props"])
    huts = 0
    for _ in range(count):
        kind = rng.choice(kinds)
        if kind == "hut" and huts:
            kind = "vent"
        if kind == "hut":
            spot = placer.place(3.2, 3.0)
            if spot:
                stair_hut(b, spot, y)
                huts += 1
        elif kind == "ac":
            spot = placer.place(rng.uniform(1.4, 2.2), rng.uniform(1.0, 1.4))
            if spot:
                ac_unit(b, spot, y)
        elif kind == "tank":
            size = rng.uniform(2.4, 3.6)
            spot = placer.place(size, size)
            if spot:
                water_tank(b, spot, y)
        elif kind == "dish":
            spot = placer.place(1.6, 1.6)
            if spot:
                dish(b, spot.cx, spot.cz, y)
        else:
            spot = placer.place(rng.uniform(0.8, 1.8), rng.uniform(0.8, 1.8))
            if spot:
                vent(b, spot, y)
    return placer


def roof_zone(b, rect, y, blocked=()):
    """A clear circle on a flat roof (objective zones, landing): a Roof marker; returns its keep-out square."""
    radius = min(rect.w, rect.d) / 2 - 2.0
    if radius < 5.0:
        return None
    radius = min(radius, 9.0)
    zone = Rect.centred(rect.cx, rect.cz, radius * 2, radius * 2)
    if any(zone.overlaps(other) for other in blocked):
        return None
    b.marker("Roof", (rect.cx, y, rect.cz), radius)
    return zone


def floodlight(b, x, z, height=10.0, facing=(0.0, 1.0)):
    """Yard light pole with a sodium head (a ground-level light cue)."""
    b.span(x - 0.15, x + 0.15, 0.0, height, z - 0.15, z + 0.15, "metal", L01)
    hx, hz = x + facing[0] * 0.6, z + facing[1] * 0.6
    b.span(hx - 0.6, hx + 0.6, height - 0.2, height + 0.2, hz - 0.35, hz + 0.35, "dark", L01)
    b.span(hx - 0.5, hx + 0.5, height - 0.26, height - 0.2, hz - 0.28, hz + 0.28, "sodium", L01)
    b.thin.append((x, z, height + 0.6))
