"""
Part builders. Units are metres in Unity space (x right, y up, z forward). Chassis origin = centre of mass (the flight
code turns the chopper about its root). Module origin = its attach point: it snaps onto any socket of its slot type
with no offset, pointing forward (+z), hanging down (-y).
"""
import math
import random

from mathutils import Matrix, Vector

from .geo import FORM, Builder, _rounded_rect, chamfer_ring, rot_x, rot_y, rot_z
from .palette import SCREEN_OF, screen_uvs
from .palette import swatch as S


class Part:
    """One exported model: geometry + sockets (slot, index, Unity position)."""

    def __init__(self, name, kind, builder, slot=None, module_ids=(), sockets=(), seed=0):
        self.name = name
        self.seed = seed
        self.kind = kind  # "chassis" or "module"
        self.builder = builder
        self.slot = slot
        self.module_ids = list(module_ids)
        self.sockets = list(sockets)


# ---------------------------------------------------------------------------------------------------------------------
# Style colours
# ---------------------------------------------------------------------------------------------------------------------

_SWAPS = {}


def use_style(style):
    """Colour swaps for the whole build (glow accents, hazard paint) and the form settings (see styles.py)."""
    from . import geo
    geo.configure(style)
    _SWAPS.clear()
    _SWAPS.update(style.get("accents", {}))
    _SWAPS["hazard"] = style.get("hazard", "hazard")


def C(name):
    """Swatch index after the style's swaps."""
    return S(_SWAPS.get(name, name))


# ---------------------------------------------------------------------------------------------------------------------
# Chassis
# ---------------------------------------------------------------------------------------------------------------------

def chassis(spec, style):
    """A chassis from its spec (kits.py) in a style (styles.py). Same spec + style + seed = same model.

    Body types (spec "body"): "utility" (one rounded hull, glass or visor canopy, skids), "attack" (chunky slab
    core, stepped two-seat cockpit, cheek pods, side engine nacelles, nose sensor, wheels, open tail rotor) and "heavy"
    (portly armoured-trooper hull: barrel torso, helmet face, armour bands, huge shoulder pods, legs with boots;
    optional twin rotors) and "assault" (stacked blocks that taper to the top, beastly grille snout)."""
    rng = random.Random(spec["seed"])
    b = Builder()
    W, H = spec["width"], spec["height"]
    F = spec["front"]           # nose tip, metres ahead of the centre of mass
    boom = spec["boom"]
    tail_z = -2.6 - boom
    body = spec.get("body", "utility")
    attack, heavy, assault = body == "attack", body == "heavy", body == "assault"
    layout = spec.get("rotor_layout", "single") if heavy else "single"
    if heavy:
        tail_z = -3.8 - boom
    ring = _ring_fn(style)
    tail = _boom_rings(ring, style, W, H, boom, tail_z, tall=attack or assault, thick=heavy)

    if attack:
        rotors = [((0, _attack_body(b, spec, style, ring, tail, W, H, F), 0), 1.0)]
        _wheels(b, style, W, H, F, tail_z)
    elif assault:
        rotors = [((0, _assault_body(b, spec, style, ring, tail, W, H, F), 0), 1.0)]
        _wheels(b, style, W, H, F, tail_z)
    elif heavy:
        rotors = _heavy_body(b, spec, style, ring, W, H, F, boom, tail_z, layout)
        _heavy_gear(b, W, H, F)
    else:
        rotors = [((0, _utility_body(b, spec, style, ring, tail, W, H, F), 0), 1.0)]
        if spec.get("skids", True):
            _skids(b, style, F, W, H)

    pylon_points = _sponson_pylons(b, spec, W, H) if heavy else _wings_or_stubs(b, spec, style, W, H)
    if layout in ("single", "coaxial"):
        # Tandem and quad layouts need no tail rotor; coaxial pushes with a prop instead.
        _tail(b, style, W, H, tail_z, open_rotor=attack or assault or heavy,
              tail_rotor=layout != "coaxial")
    has_boom = layout in ("single", "coaxial")
    _details(b, rng, spec, style, W, H, F, tail_z, boom_rings=tail if has_boom and not heavy else None)
    if style["cage"] > 0 and not heavy:  # the heavy body is its own armour
        if attack or assault:
            _attack_cage(b, W, H)
        else:
            _cage(b, style, spec, W, H, boom)
    if style["conduits"] > 0:
        # Pipe and cable heights: attack bodies run them between the cheek pods and the nacelles.
        ys = (0.145 * H, 0.175 * H) if attack or assault else ((0.30 * H, 0.37 * H) if heavy else (0.26 * H, 0.33 * H))
        _conduits(b, W, H, F, tail_z, ys, cable_to_tail=has_boom)
    if style["asymmetry"] > 0:
        _retrofits(b, rng, style, W, H, F)
    if style["wear"] > 0:
        _wear(b, rng, style, W, H, F)

    sockets = _seat_sockets(b, _sockets(spec, W, H, F, rotors, pylon_points, body))
    return Part(f"Chassis_{spec['name']}", "chassis", b, sockets=sockets, seed=spec["seed"])


# ---------------------------------------------------------------------------------------------------------------------
# Cockpit interior
# ---------------------------------------------------------------------------------------------------------------------

EMBED = 0.001  # cockpit details sink this far into what they sit on, so the audit sees them joined


def _basis(x, y, z):
    """Rotation matrix from its local axes (Unity space)."""
    return Matrix((x, y, z)).transposed()


def _facing(point, eye):
    """Rotation whose local -z points from `point` back to the eye, local x level: a panel at `point` faces the pilot."""
    z = (Vector(point) - Vector(eye)).normalized()
    x = Vector((0, 1, 0)).cross(z).normalized()
    return _basis(x, z.cross(x), z)


FACE_PILOT = _basis((1, 0, 0), (0, 1, 0), (0, 0, 1))   # a face looking back (-z), at a pilot behind it
FACE_UP = _basis((1, 0, 0), (0, 0, 1), (0, -1, 0))     # a top face (local y = forward)
FACE_DOWN = _basis((1, 0, 0), (0, 0, -1), (0, 1, 0))   # an underside (overhead panels; local y = backward)


class _Face:
    """Places parts on a flat face. Local x right, y up, -z out of the face; `surface` is the face's local z."""

    def __init__(self, b, origin, rotation, surface=0.0):
        self.b, self.origin, self.rotation, self.surface = b, Vector(origin), rotation, surface

    def at(self, x, y, h=0.0):
        """World point at (x, y) on the face, `h` out from it (negative = into the part behind)."""
        return tuple(self.origin + self.rotation @ Vector((x, y, self.surface - h)))

    def raised(self, h):
        """The same face, `h` further out (the top of something standing on it)."""
        return _Face(self.b, self.origin, self.rotation, self.surface - h)

    def box(self, x, y, size, swatch, lift=0.0, spin=0.0):
        """Box standing on the face (size z = height out of it), turned `spin` degrees in the face."""
        rotation = self.rotation @ rot_z(spin) if spin else self.rotation
        self.b.box(self.at(x, y, size[2] / 2 + lift - EMBED), size, swatch, rotation=rotation)

    def line(self, x0, y0, x1, y1, swatch, width=0.0035, height=0.002):
        """A glowing stroke from (x0, y0) to (x1, y1): screen graphics."""
        length = math.hypot(x1 - x0, y1 - y0)
        spin = math.degrees(math.atan2(y1 - y0, x1 - x0))
        self.box((x0 + x1) / 2, (y0 + y1) / 2, (length, width, height), swatch, spin=spin)

    def cylinder(self, x, y, height, radius, swatch, segments=10, cap=None, end_radius=None):
        self.b.cylinder(self.at(x, y, -EMBED), self.at(x, y, height), radius, swatch, segments=segments,
                        cap_end=cap, end_radius=end_radius)


def _beam(b, a, c, section, swatch):
    """A square-section bar from a to c (frames, rails); section = (width, depth)."""
    a, c = Vector(a), Vector(c)
    z = (c - a).normalized()
    up = Vector((0, 1, 0)) if abs(z.y) < 0.9 else Vector((1, 0, 0))
    x = up.cross(z).normalized()
    b.box(tuple((a + c) / 2), (section[0], section[1], (c - a).length), swatch, rotation=_basis(x, z.cross(x), z))


def _live(face, w, h, content):
    """The display's glass shows a live page (palette.SCREEN_OF) instead of baked graphics; False if it has none."""
    page = SCREEN_OF.get(content)
    if page is None:
        return False
    top = face.raised(0.0005)
    points = [top.at(-w / 2, -h / 2), top.at(w / 2, -h / 2), top.at(w / 2, h / 2), top.at(-w / 2, h / 2)]
    face.b.screen_quad(points, screen_uvs(page, w, h), page, C("glass"))
    return True


def _screen(b, centre, rotation, w, h, content, glow, rng, buttons=True):
    """A CRT display: bezel housing, dark glass, soft-key buttons down both sides (an MFD) and glowing graphics."""
    b.box(centre, (w + (0.07 if buttons else 0.03), h + 0.05, 0.03), C("carbon"), rotation=rotation)
    bezel = _Face(b, centre, rotation, surface=-0.015)
    bezel.box(0, 0, (w, h, 0.004), C("glass"))
    if buttons:
        rows = max(3, int(h / 0.026))
        for side in (-1, 1):
            for i in range(rows):
                bezel.box(side * (w / 2 + 0.019), (i - (rows - 1) / 2) * h / rows, (0.016, 0.011, 0.006),
                          C("hull_light") if (i + side) % 3 else C("gunmetal"))
        for i in range(4):
            bezel.box((i - 1.5) * w / 4.5, h / 2 + 0.013, (0.012, 0.006, 0.004), glow if i == 0 else C("gunmetal"))
    glass = bezel.raised(0.004 - EMBED)
    if not _live(glass, w, h, content):
        _screen_graphics(glass, w, h, content, glow, rng)


def _screen_graphics(face, w, h, kind, glow, rng):
    """Baked graphics in the screen's glow colour, for displays with no live page (palette.SCREEN_OF)."""
    alert, line = C("warning"), face.line
    if kind in ("horizon", "rear"):
        bank = math.radians(rng.uniform(-14, 14))
        dx, dy = math.cos(bank), math.sin(bank)
        line(-0.36 * w * dx, -0.36 * w * dy, 0.36 * w * dx, 0.36 * w * dy, glow)
        if kind == "horizon":
            for k in (-2, -1, 1, 2):
                ox, oy = -dy * k * 0.17 * h, dx * k * 0.17 * h
                for side in (-1, 1):
                    a, c = side * 0.05 * w, side * 0.15 * w
                    line(ox + a * dx, oy + a * dy, ox + c * dx, oy + c * dy, glow, width=0.0025)
            for side in (-1, 1):  # speed and altitude tapes with ticks
                x = side * 0.42 * w
                line(x, -0.38 * h, x, 0.38 * h, glow, width=0.0025)
                for i in range(7):
                    y = (i - 3) * 0.11 * h
                    line(x, y, x - side * 0.035 * w, y, glow, width=0.002)
            line(-0.06 * w, 0, -0.02 * w, 0, alert)   # own aircraft mark
            line(0.02 * w, 0, 0.06 * w, 0, alert)
        else:  # rear camera: horizon and a chevron behind
            line(-0.08 * w, -0.1 * h, 0, 0.12 * h, alert)
            line(0, 0.12 * h, 0.08 * w, -0.1 * h, alert)
    elif kind == "radar":
        r = 0.42 * h
        line(-r, 0, r, 0, glow, width=0.002)
        line(0, -r, 0, r, glow, width=0.002)
        for radius in (r, 0.55 * r):
            for i in range(8):
                a0, a1 = 2 * math.pi * i / 8, 2 * math.pi * (i + 1) / 8
                line(radius * math.cos(a0), radius * math.sin(a0), radius * math.cos(a1), radius * math.sin(a1), glow,
                     width=0.0025)
        sweep = rng.uniform(0, 2 * math.pi)
        line(0, 0, r * math.cos(sweep), r * math.sin(sweep), glow)
        for i in range(4):
            a, d = rng.uniform(0, 2 * math.pi), rng.uniform(0.25, 0.9) * r
            face.box(d * math.cos(a), d * math.sin(a), (0.008, 0.008, 0.002), alert if i == 0 else glow)
    elif kind == "bars":
        base = -0.36 * h
        line(-0.42 * w, base, 0.42 * w, base, glow, width=0.0025)
        for i in range(7):
            x, top = (i - 3) * 0.12 * w, rng.uniform(0.15, 0.7) * h
            face.box(x, base + top / 2, (0.07 * w, top, 0.002), alert if top > 0.62 * h else glow)
    elif kind == "map":
        for i in range(1, 4):
            line(-0.45 * w + i * 0.225 * w, -0.42 * h, -0.45 * w + i * 0.225 * w, 0.42 * h, glow, width=0.0012)
        for i in range(1, 3):
            line(-0.45 * w, -0.42 * h + i * 0.28 * h, 0.45 * w, -0.42 * h + i * 0.28 * h, glow, width=0.0012)
        points = [(rng.uniform(-0.4, 0.4) * w, (-0.35 + 0.23 * i) * h) for i in range(4)]
        for (x0, y0), (x1, y1) in zip(points, points[1:]):
            line(x0, y0, x1, y1, glow)
        for x, y in points:
            face.box(x, y, (0.007, 0.007, 0.0025), glow)
        face.box(points[-1][0], points[-1][1], (0.01, 0.01, 0.003), alert, spin=45)
    else:  # text: rows of words, a header bar, one alert line
        rows = max(3, int(h / 0.014))
        for row in range(rows):
            y = 0.4 * h - row * 0.8 * h / max(rows - 1, 1)
            if row == 0:
                face.box(0, y, (0.84 * w, 0.008, 0.002), glow)
                continue
            x, swatch = -0.42 * w, alert if row == rows - 2 else glow
            while x < 0.3 * w:
                word = rng.uniform(0.06, 0.2) * w
                line(x, y, x + word, y, swatch, width=0.003)
                x += word + 0.035 * w


def _gauge(b, centre, rotation, r, glow, rng):
    """Round 80s gauge facing the pilot: bezel, dark dial, glowing ticks with a red line, a needle and a hub."""
    face = _Face(b, centre, rotation)
    b.cylinder(face.at(0, 0, -0.02), face.at(0, 0, 0.006), r, C("gunmetal"), segments=14, cap_end=C("glass"))
    dial = face.raised(0.006)
    for i in range(7):
        a = math.radians(-135 + i * 45)
        dial.box(0.74 * r * math.sin(a), 0.74 * r * math.cos(a), (0.22 * r, 0.0035, 0.002),
                 C("warning") if i >= 5 else glow, spin=90 - math.degrees(a))
    a = math.radians(rng.uniform(-120, 80))
    dial.box(0.32 * r * math.sin(a), 0.32 * r * math.cos(a), (0.64 * r, 0.004, 0.003), C("warning"),
             spin=90 - math.degrees(a))
    dial.cylinder(0, 0, 0.005, 0.006, C("steel"), segments=6)


def _toggles(face, x0, y, count, spacing, glow, rng, guarded=False):
    """A plate with a row of toggle switches, a lamp over each; guarded = red flip guards (master switches)."""
    face.box(x0 + (count - 1) * spacing / 2, y, (count * spacing + 0.012, 0.055, 0.005), C("hull_light"))
    top = face.raised(0.005 - EMBED)
    for i in range(count):
        x, on = x0 + i * spacing, rng.random() < 0.6
        top.cylinder(x, y - 0.006, 0.008, 0.005, C("steel"), segments=8)
        lever = top.raised(0.008)
        tip = lever.at(x, y - 0.006 + (0.012 if on else -0.012), 0.022)
        top.b.cylinder(lever.at(x, y - 0.006, -EMBED), tip, 0.0025, C("steel"), segments=6, end_radius=0.0045)
        top.box(x, y + 0.018, (0.008, 0.006, 0.003), glow if on else C("gunmetal"))
        if guarded:
            for side in (-1, 1):
                top.box(x + side * 0.0115, y - 0.006, (0.003, 0.004, 0.03), C("warhead"))
            top.box(x, y - 0.006, (0.026, 0.004, 0.004), C("warhead"), lift=0.027)


def _rockers(face, x0, y, count, spacing, glow, lit=(0,)):
    """Row of rocker switches with a lit tip on some."""
    for i in range(count):
        x = x0 + i * spacing
        face.box(x, y, (0.016, 0.028, 0.008), C("carbon"))
        face.raised(0.008 - EMBED).box(x, y + 0.008, (0.011, 0.006, 0.002), glow if i in lit else C("hull_light"))


def _keypad(face, x0, y0, cols, rows, glow, pitch=0.024):
    for row in range(rows):
        for col in range(cols):
            face.box(x0 + col * pitch, y0 - row * pitch, (0.019, 0.019, 0.007),
                     glow if row == rows - 1 and col == cols - 1 else C("hull_light"))


def _knob(face, x, y, radius, glow):
    face.cylinder(x, y, 0.016, radius, C("gunmetal"), segments=12, cap=C("carbon"))
    face.raised(0.016).box(x, y + radius * 0.5, (0.003, radius, 0.002), glow)


def _readout(face, x, y, digits, glow):
    """Seven-segment style readout: a dark window with glowing digit blocks."""
    face.box(x, y, (digits * 0.013 + 0.01, 0.018, 0.004), C("glass"))
    lit = face.raised(0.004 - EMBED)
    for i in range(digits):
        lit.box(x + (i - (digits - 1) / 2) * 0.013, y, (0.008, 0.011, 0.002), glow)


def cockpit(spec, style):
    """The pilot's cockpit interior, in the chassis' own space (placed at the chassis root with no offset). Attack and
    utility bodies (Gunship, Assault, Scout) get a minimal military cockpit with a wide view; the heavy Lifter gets
    the busy 80s cabin dash. The socket "Eye" is the pilot's eye (the cockpit camera view)."""
    H, F = spec["height"], spec["front"]
    body = spec.get("body", "utility")
    if body in ("attack", "assault"):
        eye, build = (0.0, 0.56 * H + 0.02, F - 2.45), _military_cockpit   # rear (pilot) seat of the tandem canopy
    elif body == "heavy":
        eye, build = (0.0, 0.24 * H, F - 1.75), _cabin_cockpit             # behind the glazed nose
    else:
        eye, build = (0.0, 0.40 * H, F - 1.9), _scout_cockpit              # under the bubble canopy
    b = Builder()
    build(b, eye, random.Random(spec["seed"] + 7))
    return Part(f"Cockpit_{spec['name']}", "cockpit", b, sockets=[("Eye", 0, eye, 1.0)], seed=spec["seed"])


def _sweep(b, path, profiles, swatch, y0):
    """Lofts a profile along a polyline in plan (Unity x, z), corners mitred so each straight run stays flat
    (faceted). profiles: one [(u, v)] per path point, u outward (left of travel), v height above y0.
    Returns the joint frames [((x, z), outward, mitre scale)] for placing parts along the sweep."""
    runs = []
    for (x0, z0), (x1, z1) in zip(path, path[1:]):
        length = math.hypot(x1 - x0, z1 - z0)
        runs.append(Vector(((z0 - z1) / length, (x1 - x0) / length)))
    frames, rings = [], []
    for k, (x, z) in enumerate(path):
        if 0 < k < len(path) - 1:
            outward = (runs[k - 1] + runs[k]).normalized()
            scale = 1.0 / outward.dot(runs[k])
        else:
            outward, scale = runs[0] if k == 0 else runs[-1], 1.0
        frames.append(((x, z), outward, scale))
        rings.append([(x + outward.x * u * scale, y0 + v, z + outward.y * u * scale) for u, v in profiles[k]])
    smooth = FORM["loft_smooth"]
    FORM["loft_smooth"] = 0  # faceted: no curved in-between sections
    try:
        b.loft(rings, swatch)
    finally:
        FORM["loft_smooth"] = smooth
    return frames


def _display(face, w, h, content, glow, rng, keys=5):
    """A display set into a panel face: glass flush with a thin frame, soft keys down both sides."""
    face.box(0, 0, (w, h, 0.003), C("glass"))
    for side in (-1, 1):
        face.box(0, side * (h / 2 + 0.004), (w + 0.016, 0.008, 0.005), C("gunmetal"))
        face.box(side * (w / 2 + 0.004), 0, (0.008, h, 0.005), C("gunmetal"))
        for i in range(keys):
            face.box(side * (w / 2 + 0.021), (i - (keys - 1) / 2) * h / keys, (0.014, 0.011, 0.006), C("hull_light"))
    graphics = face.raised(0.003 - EMBED)
    if not _live(graphics, w, h, content):
        _screen_graphics(graphics, w, h, content, glow, rng)
        _vector_frame(graphics, w, h, glow)


def _vector_frame(face, w, h, glow):
    """A thin frame with cut corners just inside the glass (vector-display look)."""
    x, y, cut = 0.46 * w, 0.46 * h, 0.08 * min(w, h)
    points = [(-x + cut, y), (x - cut, y), (x, y - cut), (x, -y + cut), (x - cut, -y), (-x + cut, -y),
              (-x, -y + cut), (-x, y - cut)]
    for (x0, y0), (x1, y1) in zip(points, points[1:] + points[:1]):
        face.line(x0, y0, x1, y1, glow, width=0.0012)


def _scout_cockpit(b, eye, rng):
    """The Scout's open cockpit: the military cockpit with no frame in front of the pilot (the first hoop is behind
    the head) and everything about 7 cm lower, with smaller displays. The widest view of all chassis: the Scout's
    edge for spotting."""
    _military_cockpit(b, eye, rng, open_view=True)


def _military_cockpit(b, eye, rng, open_view=False):
    """Minimal military cockpit (Gunship, Assault; the Scout with open_view): built for the view. Everything sits
    low, under a glare shield at about 18 degrees below the eye line (open_view: about 25, and no frame in front);
    the canopy frame is thin and sweeps out of view.

    - one faceted shell wraps round the pilot: dash in front, low side consoles at the sides;
    - one faceted instrument panel across the front, facets facing the eye: two square displays with soft keys (flight
      and target pages), a centre strip with an up-front readout, two standby gauges, caution and warning lamps; a
      glare shield over it with an amber eyebrow light underneath;
    - master-arm switches under guards, power levers and collective on the left, a side-stick cyclic and keypad
      on the right, a narrow pedestal between the knees (small display, keys, lamps), pedals;
    - thin frame: windscreen pillars, roof rails, door hoops, a standby compass on the bow;
    - armoured seat with side plates, pads, headrest and harness; a rear bulkhead with a data module and an
      extinguisher.

    Single-colour displays (neon_green: green in the neon style, amber in dystopia), matte surfaces, little glow."""
    ex, ey, ez = eye

    def P(x, y, z):
        """Point relative to the eye."""
        return (ex + x, ey + y, ez + z)

    dark, carbon, metal = C("hull_dark"), C("carbon"), C("gunmetal")
    glow, amber, alert = C("neon_green"), C("sodium"), C("warning")

    # -- floor and the wrap-round shell -----------------------------------------------------------------------------
    b.box(P(0, -0.47, -0.10), (0.70, 0.04, 1.5), dark)
    deck = -0.37 if open_view else -0.30   # shell and console top
    drop = deck + 0.30                     # how much lower than the attack layout
    shell = [(-0.34, -0.80), (-0.34, 0.12), (-0.27, 0.33), (-0.12, 0.44), (0.12, 0.44), (0.27, 0.33), (0.34, 0.12),
             (0.34, -0.80)]
    widths = [0.12, 0.12, 0.16, 0.40, 0.40, 0.16, 0.12, 0.12]
    _sweep(b, [(ex + x, ez + z) for x, z in shell],
           [[(0, -0.46), (0, deck - 0.03), (0.02, deck), (w, deck), (w, -0.46)] for w in widths], dark, ey)

    # -- instrument panel: three facets facing the eye, glare shield on top -----------------------------------------
    r0, bottom = 0.47, deck - 0.01
    top = bottom + (0.10 if open_view else 0.13)
    lean = math.atan2(-(bottom + top) / 2, r0)   # the face looks at the eye
    screen, end = (0.085, 0.10) if open_view else (0.105, 0.13)
    dirs = [Vector((math.sin(a), math.cos(a))) for a in map(math.radians, (-24, 0, 24))]

    def along(d):
        return Vector((d.y, -d.x))

    def joint(a, c):
        return r0 * (a + c) / (1 + a.dot(c))

    path = [r0 * dirs[0] - end * along(dirs[0]), joint(dirs[0], dirs[1]), joint(dirs[1], dirs[2]),
            r0 * dirs[2] + end * along(dirs[2])]
    lip = (top - bottom) * math.tan(lean)
    frames = _sweep(b, [(ex + p.x, ez + p.y) for p in path],
                    [[(0, bottom), (lip, top), (-0.03, top + 0.012), (-0.03, top + 0.022), (depth, top + 0.018),
                      (depth, bottom)] for depth in (0.15, 0.20, 0.20, 0.15)], carbon, ey)

    def facet(j, offset, v):
        """A face on panel facet j, `offset` metres right of its middle, at height v (eye-relative)."""
        a, c, d = path[j], path[j + 1], dirs[j]
        run = (c - a).normalized()
        middle = (a + c) / 2 + run * offset
        u = (v - bottom) * math.tan(lean)
        x_axis = Vector((run.x, 0, run.y))
        y_axis = Vector((d.x * math.sin(lean), math.cos(lean), d.y * math.sin(lean)))
        return _Face(b, (ex + middle.x + d.x * u, ey + v, ez + middle.y + d.y * u),
                     _basis(x_axis, y_axis, x_axis.cross(y_axis)))

    middle = (bottom + top) / 2
    _display(facet(0, 0.01, middle), screen, screen, "horizon", glow, rng)
    _display(facet(2, -0.01, middle), screen, screen, "radar", glow, rng)
    _readout(facet(1, 0, top - 0.02), 0, 0, 8, glow)
    for x in (-0.045, 0.045):
        face = facet(1, x, bottom + 0.042)
        _gauge(b, face.at(0, 0, 0), face.rotation, 0.022, glow, rng)
    facet(1, -0.078, top - 0.02).box(0, 0, (0.022, 0.012, 0.004), amber)   # master caution
    facet(1, 0.078, top - 0.02).box(0, 0, (0.022, 0.012, 0.004), alert)    # master warning

    def at_joint(k, u, v):
        (x, z), outward, scale = frames[k]
        return (x + outward.x * u * scale, ey + v, z + outward.y * u * scale)

    for k in range(len(frames) - 1):                                       # eyebrow light under the glare shield
        _beam(b, at_joint(k, -0.015, top + 0.009), at_joint(k + 1, -0.015, top + 0.009), (0.006, 0.006), amber)

    # Master-arm switches either side of the pedestal, on the shell.
    arm = _Face(b, P(0, (deck - 0.49) / 2, 0.44), FACE_PILOT)
    for x0 in (-0.20, 0.11):
        _toggles(arm, x0, 0, 2, 0.045, alert, rng, guarded=True)
    arm.box(-0.1775, 0.045, (0.07, 0.012, 0.003), C("hazard_faded"))

    # -- side consoles ----------------------------------------------------------------------------------------------
    left = _Face(b, P(-0.40, deck, 0), FACE_UP)
    left.box(0, 0.02, (0.07, 0.12, 0.015), carbon)                         # power levers
    for i, x in enumerate((-0.016, 0.016)):
        b.cylinder(left.at(x, 0.02, 0.014), left.at(x, -0.01, 0.07), 0.005, C("steel"), segments=6)
        b.box(left.at(x, -0.01, 0.075), (0.024, 0.014, 0.018), (C("hull_light"), C("warhead"))[i])
    _toggles(left, -0.03, -0.25, 3, 0.03, glow, rng)
    left.box(0, -0.42, (0.09, 0.12, 0.004), C("hull"))
    _rockers(left.raised(0.004 - EMBED), -0.02, -0.42, 3, 0.02, glow, lit=(1,))
    right = _Face(b, P(0.40, deck, 0), FACE_UP)
    _keypad(right, -0.036, -0.06, 4, 3, glow)
    _knob(right, -0.022, -0.21, 0.013, glow)
    _knob(right, 0.022, -0.21, 0.013, glow)
    _readout(right, 0, -0.29, 5, glow)
    _toggles(right, -0.03, -0.42, 3, 0.03, alert, rng)

    # -- controls ---------------------------------------------------------------------------------------------------
    b.cylinder(P(-0.345, -0.40, -0.42), P(-0.29, -0.29, -0.02), 0.013, C("steel"), segments=8)   # collective
    b.cylinder(P(-0.292, -0.293, -0.03), P(-0.285, -0.287, 0.03), 0.018, C("rubber"), segments=10)
    b.box(P(-0.283, -0.28, 0.06), (0.045, 0.04, 0.06), carbon)
    head = _Face(b, P(-0.283, -0.26, 0.06), FACE_UP)
    head.box(-0.011, 0.01, (0.01, 0.01, 0.004), alert)
    head.box(0.011, 0.01, (0.01, 0.01, 0.004), glow)
    right.cylinder(0, 0.08, 0.04, 0.03, C("rubber"), segments=10, end_radius=0.014)            # side-stick cyclic
    b.cylinder(right.at(0, 0.08, 0.035), right.at(0, 0.075, 0.09), 0.009, C("steel"), segments=8)
    b.cylinder(right.at(0, 0.075, 0.085), right.at(0, 0.068, 0.15), 0.016, C("rubber"), segments=10)
    for i in range(3):
        b.cylinder(right.at(0, 0.074 - i * 0.002, 0.095 + i * 0.016),
                   right.at(0, 0.0735 - i * 0.002, 0.101 + i * 0.016), 0.0175, carbon, segments=10)
    b.box(right.at(0, 0.066, 0.165), (0.034, 0.04, 0.03), carbon)
    b.box(right.at(0, 0.09, 0.14), (0.01, 0.012, 0.02), C("warhead"))                           # trigger
    b.cylinder(right.at(-0.006, 0.066, 0.178), right.at(-0.006, 0.066, 0.188), 0.007, metal, segments=8,
               cap_end=glow)                                                                    # hat
    b.cylinder(right.at(0.009, 0.072, 0.178), right.at(0.009, 0.072, 0.186), 0.005, metal, segments=6,
               cap_end=alert)

    # Narrow pedestal between the knees, its top tilted to the pilot: a small display, keys and a lamp row.
    b.box(P(0, -0.39 + drop / 2, 0.34), (0.14, 0.12 + drop, 0.20), dark)
    tilt = rot_x(-25) @ FACE_UP
    b.box(P(0, -0.326 + drop, 0.335), (0.13, 0.012, 0.20), carbon, rotation=rot_x(-25))
    pedestal = _Face(b, P(0, -0.326 + drop, 0.335), tilt, surface=-0.006)
    _display(_Face(b, pedestal.at(0, 0.05, 0), tilt), 0.075, 0.045, "text", glow, rng, keys=3)
    _keypad(pedestal, -0.024, -0.02, 3, 2, glow)
    for i in range(4):
        pedestal.box(-0.036 + i * 0.024, -0.085, (0.014, 0.01, 0.004), alert if i % 2 == 0 else amber)
    for side in (-1, 1):                                                                                 # pedals
        b.box(P(side * 0.12, -0.43, 0.40), (0.02, 0.04, 0.04), metal)
        b.box(P(side * 0.12, -0.40, 0.395), (0.07, 0.11, 0.02), C("hull_light"), rotation=rot_x(25))  # top leans away

    # -- canopy frame: thin, sweeps out of the view (open view: nothing in front of the pilot) ------------------------
    for side in (-1, 1):
        hoop = P(side * 0.31, 0.28, -0.35)
        if not open_view:
            base, crown = P(side * 0.34, -0.31, 0.48), P(side * 0.30, 0.28, 0.10)
            _beam(b, base, crown, (0.03, 0.025), dark)                                 # windscreen pillar
            a, c = Vector(base), Vector(crown)
            _beam(b, a + (c - a) * 0.05, a + (c - a) * 0.11, (0.034, 0.029), C("hazard_faded"))
            _beam(b, crown, hoop, (0.025, 0.022), dark)                                # roof rail
        _beam(b, hoop, P(side * 0.30, 0.28, -0.75), (0.025, 0.022), dark)
        _beam(b, P(side * 0.40, deck - 0.01, -0.35), hoop, (0.025, 0.022), dark)        # door hoop
        _beam(b, P(side * 0.40, deck - 0.01, -0.75), P(side * 0.30, 0.28, -0.75), (0.03, 0.025), dark)  # rear arch
    _beam(b, P(-0.31, 0.28, -0.35), P(0.31, 0.28, -0.35), (0.025, 0.022), dark)
    if open_view:   # standby compass on the glare shield
        b.box(P(0, top + 0.04, r0 + 0.04), (0.05, 0.04, 0.035), carbon)
        compass = _Face(b, P(0, top + 0.04, r0 + 0.0225), FACE_PILOT)
    else:           # standby compass under the windscreen bow
        _beam(b, P(-0.30, 0.28, 0.10), P(0.30, 0.28, 0.10), (0.03, 0.025), dark)
        b.box(P(0, 0.248, 0.10), (0.05, 0.04, 0.035), carbon)
        compass = _Face(b, P(0, 0.248, 0.0825), FACE_PILOT)
    compass.box(0, 0, (0.032, 0.024, 0.003), C("glass"))
    compass.box(0, 0, (0.002, 0.02, 0.002), amber, lift=0.002)

    # -- armoured seat ----------------------------------------------------------------------------------------------
    wing = [(-0.08, -0.45), (-0.08, -0.33), (-0.14, -0.27), (-0.42, -0.27), (-0.48, 0.08), (-0.54, 0.16),
            (-0.66, 0.16), (-0.70, -0.45)]
    for side in (-1, 1):
        b.fin([(ez + z, ey + y) for z, y in wing], ex + side * 0.24, 0.025, metal)    # side armour
        b.box(P(side * 0.08, -0.02, -0.535), (0.045, 0.42, 0.006), C("hull_light"), rotation=rot_x(-8))  # harness
        b.box(P(side * 0.09, -0.288, -0.22), (0.045, 0.006, 0.22), C("hull_light"))
    b.box(P(0, -0.405, -0.36), (0.46, 0.09, 0.52), dark)                               # seat bucket
    b.box(P(0, -0.325, -0.31), (0.44, 0.07, 0.42), carbon)                             # cushion
    b.box(P(0, -0.06, -0.60), (0.46, 0.60, 0.03), metal, rotation=rot_x(-8))           # back armour
    b.box(P(0, -0.06, -0.565), (0.40, 0.56, 0.05), carbon, rotation=rot_x(-8))         # back pad
    b.box(P(0, 0.27, -0.62), (0.22, 0.13, 0.06), carbon, rotation=rot_x(-8))           # headrest
    b.box(P(0, -0.285, -0.13), (0.07, 0.012, 0.06), C("steel"))                        # buckle

    # -- rear bulkhead ----------------------------------------------------------------------------------------------
    b.box(P(0, -0.02, -0.83), (0.78, 0.86, 0.04), dark)
    bulkhead = _Face(b, P(0, -0.02, -0.81), _basis((-1, 0, 0), (0, 1, 0), (0, 0, -1)))
    bulkhead.box(-0.24, 0.05, (0.18, 0.22, 0.05), C("hull"))                           # data module
    for i in range(4):
        bulkhead.box(-0.24, -0.02 + i * 0.035, (0.13, 0.008, 0.056), metal)
    bulkhead.box(-0.19, 0.14, (0.012, 0.012, 0.056), glow)
    b.box(P(-0.30, -0.10, -0.79), (0.08, 0.03, 0.04), metal)                           # extinguisher bracket
    b.cylinder(P(-0.30, -0.25, -0.77), P(-0.30, 0.05, -0.77), 0.035, C("warhead"), segments=10, cap_end=metal)
    b.cylinder(P(-0.30, 0.05, -0.77), P(-0.30, 0.09, -0.77), 0.012, metal, segments=8)


def _cabin_cockpit(b, eye, rng):
    """The Lifter's cabin dash: an 80s car-dash cockpit (art direction: Nissan 300ZX binnacle and satellite pods,
    cyberpunk vehicle screens), retrofitted and worn, busy with instruments:

    - dash: binnacle hood over the main display (attitude), satellite pods with rockers and knobs, side displays
      (radar, systems), a retrofitted map monitor on a gooseneck, a glowing edge strip;
    - dash face: a cluster of six round gauges, a keypad, guarded master switches, an under-dash light;
    - centre pedestal, cyclic stick with a grip, collective with its switch head, throttle quadrant;
    - side consoles (keypad, small CRT, rockers, lamp strips), side walls with vents;
    - canopy frame (pillars with hazard bands and a cable loom, roof bow, rails, sills), a rear-camera screen where
      a car's mirror sits, an overhead breaker panel and a dome light;
    - bucket seat with bolsters, headrest and harness; rear bulkhead with avionics, a screen and an extinguisher.

    Screens and gauges face the eye (readable). Everything stays within 0.47 m of the eye sideways."""
    ex, ey, ez = eye

    def P(x, y, z):
        """Point relative to the eye."""
        return (ex + x, ey + y, ez + z)

    dark, carbon, metal = C("hull_dark"), C("carbon"), C("gunmetal")
    cyan, amber, alert = C("neon_cyan"), C("neon_green"), C("warning")   # neon_green is amber in the dystopia style

    def facing(x, y, z):
        return _facing(P(x, y, z), eye)

    # -- tub, dash and binnacle -------------------------------------------------------------------------------------
    b.box(P(0, -0.46, -0.05), (0.82, 0.06, 1.5), dark)                     # floor tub
    for i in range(4):
        b.box(P(0, -0.428, 0.06 + i * 0.07), (0.46, 0.006, 0.02), metal)    # footwell treads
    b.box(P(0, -0.38, 0.72), (0.94, 0.30, 0.50), dark)                      # dash cowl
    b.box(P(0, -0.225, 0.74), (0.96, 0.03, 0.56), carbon)                   # dash top
    b.box(P(0, -0.205, 0.99), (0.98, 0.05, 0.10), carbon)                   # far lip
    b.box(P(0, -0.215, 0.462), (0.86, 0.008, 0.012), cyan)                  # glowing edge strip

    _screen(b, P(0, -0.145, 0.60), facing(0, -0.145, 0.60), 0.22, 0.13, "horizon", cyan, rng)
    b.box(P(0, -0.05, 0.66), (0.36, 0.016, 0.15), carbon)                   # binnacle hood
    for side in (1, -1):
        b.box(P(side * 0.172, -0.13, 0.66), (0.016, 0.16, 0.15), carbon)    # hood cheeks
        b.box(P(side * 0.172, -0.06, 0.588), (0.012, 0.012, 0.006), amber)  # hood lamps

    # Satellite pods either side of the binnacle (300ZX): rockers and a readout on the left, knobs on the right.
    for side in (-1, 1):
        centre = P(side * 0.225, -0.165, 0.56)
        rotation = facing(side * 0.225, -0.165, 0.56)
        b.box(centre, (0.09, 0.09, 0.07), dark, rotation=rotation)
        pod = _Face(b, centre, rotation, surface=-0.035)
        if side < 0:
            _readout(pod, 0, 0.025, 4, amber)
            _rockers(pod, -0.027, -0.015, 3, 0.027, amber, lit=(0, 2))
        else:
            _knob(pod, -0.02, 0.012, 0.013, cyan)
            _knob(pod, 0.022, 0.012, 0.011, amber)
            for i in range(3):
                pod.box(-0.02 + i * 0.02, -0.025, (0.012, 0.008, 0.004), (alert, amber, cyan)[i])

    _screen(b, P(-0.37, -0.155, 0.64), facing(-0.37, -0.155, 0.64), 0.12, 0.095, "radar", amber, rng)
    _screen(b, P(0.37, -0.155, 0.64), facing(0.37, -0.155, 0.64), 0.12, 0.095, "bars", amber, rng)

    # Retrofitted map monitor on a gooseneck, clamped to the right of the dash (a later add-on).
    b.box(P(0.40, -0.20, 0.84), (0.05, 0.03, 0.05), C("hazard_faded"))
    monitor = P(0.36, -0.03, 0.73)
    monitor_rotation = facing(0.36, -0.03, 0.73)
    _screen(b, monitor, monitor_rotation, 0.10, 0.07, "map", cyan, rng, buttons=False)
    b.cylinder(P(0.40, -0.19, 0.84), _Face(b, monitor, monitor_rotation).at(0, -0.035, -0.01), 0.008, metal,
               segments=8)

    # -- dash face: gauge cluster, keypad, master switches, under-dash light ----------------------------------------
    face = _Face(b, P(0, -0.38, 0.47), FACE_PILOT)
    for x in (-0.41, -0.29, -0.17, 0.17, 0.29, 0.41):
        point = face.at(x, 0.085, 0)
        _gauge(b, point, _facing(point, eye), 0.034 if abs(x) < 0.4 else 0.026, amber if abs(x) < 0.25 else cyan, rng)
    _keypad(face, -0.33, 0.01, 4, 3, amber)
    face.box(-0.19, -0.005, (0.06, 0.016, 0.003), C("hazard_faded"))      # placard
    _toggles(face, 0.20, -0.02, 4, 0.045, alert, rng, guarded=True)
    face.box(0, -0.045, (0.86, 0.01, 0.01), C("sodium"))                    # under-dash light

    # Centre pedestal between the knees: knobs and a readout.
    b.box(P(0, -0.37, 0.42), (0.17, 0.12, 0.095), dark)                    # under 0.1 m: crisp, not rounded
    pedestal = _Face(b, P(0, -0.37, 0.37), FACE_PILOT)
    _knob(pedestal, -0.045, 0.02, 0.014, cyan)
    _knob(pedestal, 0.045, 0.02, 0.014, amber)
    _readout(pedestal, 0, -0.03, 5, cyan)

    # -- controls ---------------------------------------------------------------------------------------------------
    b.cylinder(P(0, -0.432, 0.28), P(0, -0.37, 0.285), 0.05, C("rubber"), segments=10, end_radius=0.02)   # boot
    b.cylinder(P(0, -0.38, 0.285), P(0, -0.29, 0.30), 0.012, C("steel"), segments=8)
    b.cylinder(P(0, -0.295, 0.30), P(0, -0.235, 0.288), 0.017, C("rubber"), segments=10)                   # grip
    for i in range(3):
        b.cylinder(P(0, -0.285 + i * 0.016, 0.298 - i * 0.003), P(0, -0.279 + i * 0.016, 0.297 - i * 0.003),
                   0.0185, carbon, segments=10)                                                             # finger ridges
    b.box(P(0, -0.222, 0.293), (0.042, 0.03, 0.05), carbon, rotation=rot_x(-12))                           # grip head
    b.box(P(0, -0.245, 0.315), (0.01, 0.022, 0.012), C("warhead"))                                          # trigger
    b.cylinder(P(0, -0.208, 0.285), P(0, -0.198, 0.284), 0.008, metal, segments=8, cap_end=cyan)            # hat
    b.cylinder(P(0.014, -0.209, 0.300), P(0.014, -0.201, 0.299), 0.005, metal, segments=6, cap_end=alert)   # weapon release
    b.cylinder(P(-0.36, -0.20, -0.40), P(-0.31, -0.17, 0.02), 0.014, C("steel"), segments=8)               # collective
    b.cylinder(P(-0.312, -0.172, 0.0), P(-0.305, -0.168, 0.06), 0.02, C("rubber"), segments=10)            # twist grip
    b.box(P(-0.30, -0.155, 0.09), (0.05, 0.045, 0.07), carbon)                                              # switch head
    head = _Face(b, P(-0.30, -0.1325, 0.09), FACE_UP)
    head.box(-0.012, 0.01, (0.01, 0.01, 0.004), alert)
    head.box(0.012, 0.01, (0.01, 0.01, 0.004), amber)
    head.box(0, -0.015, (0.02, 0.008, 0.004), C("hull_light"))

    # -- side consoles and walls ------------------------------------------------------------------------------------
    for side in (-1, 1):
        x = side * 0.40
        b.box(P(x, -0.32, -0.04), (0.14, 0.24, 1.02), dark)
        top = _Face(b, P(x, -0.20, 0), FACE_UP)
        top.box(-side * 0.062, 0.05, (0.006, 0.5, 0.004), cyan if side < 0 else C("sodium"))   # inner lamp strip
        _rockers(top, -0.03, -0.45, 3, 0.03, amber, lit=(1,))
        b.box(P(side * 0.455, -0.12, -0.05), (0.03, 0.16, 1.0), dark)                         # side wall
        wall = _Face(b, P(side * 0.44, -0.12, -0.05), _basis((0, 0, -side), (0, 1, 0), (side, 0, 0)))
        for i in range(6):
            wall.box(-0.25 + i * 0.025, -0.02, (0.008, 0.07, 0.006), metal)                  # vent slats
        wall.box(0.25, 0.055, (0.15, 0.012, 0.004), C("sodium"))                             # side lamp
        wall.box(0.05, 0.03, (0.06, 0.03, 0.003), C("primer") if side > 0 else C("hazard_faded"))
    left = _Face(b, P(-0.40, -0.20, 0), FACE_UP)
    left.box(0, 0.25, (0.08, 0.14, 0.02), carbon)                                              # throttle quadrant
    for i, x in enumerate((-0.018, 0.018)):
        b.cylinder(left.at(x, 0.25, 0.019), left.at(x, 0.22, 0.075), 0.005, C("steel"), segments=6)
        b.box(left.at(x, 0.22, 0.08), (0.026, 0.016, 0.02), (C("hull_light"), C("warhead"))[i])
    right = _Face(b, P(0.40, -0.20, 0), FACE_UP)
    _keypad(right, -0.024, -0.02, 3, 4, cyan)
    _knob(right, 0.0, -0.17, 0.016, amber)
    _screen(b, P(0.40, -0.16, 0.21), facing(0.40, -0.16, 0.21), 0.09, 0.065, "text", amber, rng, buttons=False)

    # -- canopy frame -----------------------------------------------------------------------------------------------
    for side in (-1, 1):
        base, top = P(side * 0.44, -0.26, 0.86), P(side * 0.40, 0.30, 0.42)
        _beam(b, base, top, (0.045, 0.035), metal)                                             # windscreen pillar
        a, c = Vector(base), Vector(top)
        for i in range(4):                                                                     # hazard band
            _beam(b, a + (c - a) * (0.10 + i * 0.04), a + (c - a) * (0.14 + i * 0.04), (0.05, 0.04),
                  C("hazard_faded") if i % 2 == 0 else dark)
        _beam(b, top, P(side * 0.38, 0.30, -0.80), (0.035, 0.03), metal)                       # roof rail
        _beam(b, P(side * 0.44, -0.20, -0.80), P(side * 0.38, 0.30, -0.80), (0.04, 0.03), metal)
        _beam(b, P(side * 0.425, -0.05, 0.70), P(side * 0.44, -0.05, -0.80), (0.03, 0.03), metal)  # sill
    for k, swatch in enumerate((C("rubber"), C("warhead"), C("hazard_faded"))):                # cable loom
        offset = Vector((0.026, 0, -0.012 + k * 0.012))
        b.cylinder(Vector(P(-0.44, -0.26, 0.86)) + offset, Vector(P(-0.40, 0.30, 0.42)) + offset, 0.006, swatch,
                   segments=6)
    _beam(b, P(-0.40, 0.30, 0.42), P(0.40, 0.30, 0.42), (0.05, 0.04), metal)                   # roof bow
    _beam(b, P(0, 0.30, 0.42), P(0, 0.30, -0.80), (0.03, 0.025), metal)                        # roof spine

    # Rear-camera screen where a car's mirror sits.
    b.cylinder(P(0, 0.29, 0.415), P(0, 0.235, 0.405), 0.008, metal, segments=8)
    _screen(b, P(0, 0.215, 0.40), facing(0, 0.215, 0.40), 0.17, 0.04, "rear", cyan, rng, buttons=False)

    # Overhead breaker panel, switches and a dome light.
    b.box(P(0, 0.31, 0.05), (0.30, 0.04, 0.50), dark)
    overhead = _Face(b, P(0, 0.29, 0.05), FACE_DOWN)
    for row in range(3):
        for i in range(7):
            tripped = rng.random() < 0.12
            overhead.cylinder(-0.09 + i * 0.03, -0.17 + row * 0.04, 0.012, 0.0065, metal, segments=6,
                              cap=alert if tripped else C("hull_light"))
    _toggles(overhead, -0.08, 0.06, 5, 0.04, amber, rng)
    _readout(overhead, 0, 0.17, 6, cyan)
    b.box(P(0, 0.278, -0.35), (0.06, 0.02, 0.10), C("sodium"))                                 # dome light

    # -- seat and rear bulkhead -------------------------------------------------------------------------------------
    b.box(P(0, -0.36, -0.30), (0.44, 0.10, 0.48), carbon)
    for side in (-1, 1):
        b.box(P(side * 0.20, -0.30, -0.30), (0.06, 0.08, 0.46), carbon)                        # cushion bolsters
        b.box(P(side * 0.20, -0.08, -0.53), (0.06, 0.56, 0.12), carbon, rotation=rot_x(-8))    # back bolsters
        b.box(P(side * 0.15, -0.42, -0.30), (0.03, 0.04, 0.5), metal)                          # seat rails
        b.box(P(side * 0.07, -0.307, -0.25), (0.045, 0.006, 0.36), C("hazard_faded"))          # harness
    b.box(P(0, -0.08, -0.58), (0.44, 0.60, 0.10), carbon, rotation=rot_x(-8))
    b.box(P(0, 0.28, -0.62), (0.26, 0.18, 0.09), C("rubber"))
    b.box(P(0, -0.303, -0.10), (0.05, 0.012, 0.04), C("steel"))                               # buckle
    b.box(P(0, 0.08, -0.86), (0.90, 0.70, 0.05), dark)
    bulkhead = _Face(b, P(0, 0.08, -0.835), _basis((-1, 0, 0), (0, 1, 0), (0, 0, -1)))
    bulkhead.box(-0.25, -0.08, (0.22, 0.30, 0.06), C("hull_light"))                            # avionics box
    for i in range(5):
        bulkhead.box(-0.25, -0.18 + i * 0.03, (0.16, 0.008, 0.066), metal)
    bulkhead.box(-0.19, 0.04, (0.012, 0.012, 0.066), amber)
    b.box(P(-0.30, -0.10, -0.81), (0.08, 0.03, 0.04), metal)                                   # extinguisher bracket
    b.cylinder(P(-0.30, -0.25, -0.79), P(-0.30, 0.05, -0.79), 0.035, C("warhead"), segments=10, cap_end=metal)
    b.cylinder(P(-0.30, 0.05, -0.79), P(-0.30, 0.09, -0.79), 0.012, metal, segments=8)
    _screen(b, P(0, 0.20, -0.82), _basis((-1, 0, 0), (0, 1, 0), (0, 0, -1)), 0.14, 0.08, "text", cyan, rng,
            buttons=False)


def cockpits(specs, style):
    """The interior part of every chassis that has one."""
    return [part for part in (cockpit(spec, style) for spec in specs) if part is not None]


def _ring_fn(style):
    facet, taper = style["facet"], style["top_taper"]

    def ring(z, w, h, chamfer, y=0.0, x=0.0, top=1.0):
        return chamfer_ring(z, w, h, min(chamfer * facet, 0.6), y=y, x=x, top_scale=min(1.0, top * taper))

    return ring


def _boom_rings(ring, style, W, H, boom, tail_z, tall=False, thick=False):
    """Tail boom sections: round-ish tube or a tall slab with flat sides. Attack bodies get a taller, thinner boom;
    heavy bodies a thicker one."""
    k = 1.15 if tall else 1.0
    if thick:
        W, k = W * 1.25, 1.2
    if style["boom"] == "slab":
        return [ring(-2.6, 0.30 * W, 0.42 * H * k, 0.18, y=0.20 * H),
                ring(-2.6 - boom * 0.5, 0.22 * W, 0.34 * H * k, 0.18, y=0.22 * H),
                ring(tail_z, 0.15 * W, 0.28 * H * k, 0.18, y=0.25 * H)]
    return [ring(-2.6, 0.34 * W, 0.36 * H * k, 0.30, y=0.20 * H),
            ring(-2.6 - boom * 0.5, 0.26 * W, 0.28 * H * k, 0.30, y=0.22 * H),
            ring(tail_z, 0.17 * W, 0.22 * H * k, 0.30, y=0.26 * H)]


def _utility_body(b, spec, style, ring, tail, W, H, F):
    """One hull from nose to boom, canopy on top, engines on the roof. Returns the rotor height."""
    # Nose: round (tapered) or chisel (low, wide blade with a flat top).
    if style["nose"] == "chisel":
        nose = [ring(F, 0.55 * W, 0.16 * H, 0.45, y=-0.24 * H),
                ring(F - 0.7, 0.86 * W, 0.50 * H, 0.32, y=-0.14 * H, top=0.75),
                ring(F - 1.6, 0.95 * W, 0.88 * H, 0.28, y=-0.03 * H, top=0.8)]
    else:
        nose = [ring(F, 0.30 * W, 0.30 * H, 0.35, y=-0.12 * H),
                ring(F - 0.6, 0.62 * W, 0.58 * H, 0.32, y=-0.08 * H),
                ring(F - 1.6, 0.92 * W, 0.90 * H, 0.28, y=-0.02 * H, top=0.8)]
    b.loft(nose + [
        ring(0.6, W, H, 0.25, top=0.85),
        ring(-1.0, 0.96 * W, 0.95 * H, 0.25, y=0.02 * H, top=0.85),
        ring(-2.0, 0.60 * W, 0.60 * H, 0.30, y=0.15 * H),
    ] + tail, C("hull"), cap_front=C("hull_dark"), cap_back=C("hull_dark"))
    b.snapshot_surface()

    _canopy(b, style, W, H, F)
    b.box((0, (-0.24 if style["nose"] == "chisel" else -0.02) * H, F + 0.01), (0.12 * W, 0.04, 0.04),
          C(style["lights"]["nose"]))

    _engines(b, spec, style, W, H)
    top = 0.5 * H + 0.12 * H + 0.16 * H
    # Rotor mast with a fairing.
    b.box((0, 0.5 * H + 0.1, -0.1), (0.5 * W, 0.25, 1.1), C("hull_dark"))
    b.cylinder((0, 0.5 * H, 0), (0, top + 0.35, 0), 0.14, C("steel"), segments=10)
    return top + 0.35


def _attack_body(b, spec, style, ring, tail, W, H, F):
    """Chunky attack hull (the "Apache" shape language, not the design): a narrow, tall, flat-sided core; a stepped
    tandem cockpit (gunner low in front, pilot higher behind); cheek pods along the sides; engines in side nacelles at
    the shoulders; a boxy nose sensor turret. Returns the rotor height."""
    cw = 0.62 * W                  # core width; cheek pods fill out to W
    pod = (W - cw) / 2
    chunk = 0.16                   # low chamfer: slab sides, hard edges
    taper = spec.get("taper", 1.0)  # < 1: every section narrows more towards its top
    grille = spec.get("snout", "sensor") == "grille"
    base_ring = ring

    def ring(z, w, h, chamfer, y=0.0, x=0.0, top=1.0):
        return base_ring(z, w, h, chamfer, y=y, x=x, top=top * taper)

    b.loft([
        ring(F, 0.55 * cw, 0.40 * H, chunk, y=-0.18 * H),
        ring(F - 0.5, 0.82 * cw, 0.62 * H, chunk, y=-0.12 * H, top=0.85),
        ring(F - 1.4, cw, 0.78 * H, chunk, y=-0.05 * H, top=0.85),
        ring(F - 2.6, cw, 0.92 * H, chunk, y=0.02 * H, top=0.85),
        ring(-0.6, cw, 1.00 * H, chunk, y=0.05 * H, top=0.85),
        ring(-1.8, 0.80 * cw, 0.82 * H, chunk, y=0.12 * H, top=0.9),
    ] + tail, C("hull"), cap_front=C("hull_dark"), cap_back=C("hull_dark"))

    # Cheek pods (avionics bays) along both sides, low.
    for side in (1, -1):
        x = side * (cw / 2 + pod / 2 - 0.03)
        b.loft([
            ring(F - 1.0, pod * 0.6, 0.28 * H, 0.3, y=-0.20 * H, x=x),
            ring(F - 1.6, pod, 0.48 * H, 0.22, y=-0.12 * H, x=x),
            ring(-0.6, pod, 0.48 * H, 0.22, y=-0.12 * H, x=x),
            ring(-1.4, pod * 0.7, 0.30 * H, 0.3, y=-0.08 * H, x=x),
        ], C("hull"), cap_front=C("hull_dark"), cap_back=C("hull_dark"))
    b.snapshot_surface()
    for side in (1, -1):
        for i in range(3):
            b.on_surface(side, -0.02 * H - i * 0.08, -0.9, (0.04, 0.035, 0.5), C("hull_dark"))

    _tandem_cockpit(b, style, cw, H, F)

    if grille:
        # Beastly grille snout instead of the sensor turret; a small sensor ball moves under the chin, to the side.
        _grille_snout(b, style, cw, H, F + 0.45, scale=0.95)
        b.ico((0.3 * cw, -0.42 * H, F - 0.6), 0.16, C("gunmetal"), subdivisions=1)
        b.box((0.3 * cw, -0.42 * H, F - 0.44), (0.08, 0.06, 0.03), C(style["lights"]["sensor"]))
    else:
        _sensor_turret(b, style, cw, H, F)

    # Engine nacelles at the shoulders (their inner side sunk into the core), with exhaust deflectors angled out.
    y = 0.36 * H
    nacelle_w = 0.26 * W
    for side in (1, -1):
        x = side * _nacelle_x(b, W, H)
        b.loft([
            ring(0.4, nacelle_w * 0.75, 0.26 * H, 0.35, y=y, x=x),
            ring(0.1, nacelle_w, 0.34 * H, 0.3, y=y, x=x),
            ring(-1.4, nacelle_w * 0.95, 0.32 * H, 0.3, y=y, x=x),
            ring(-1.8, nacelle_w * 0.8, 0.26 * H, 0.3, y=y, x=x),
        ], C("hull"), cap_front=C("rubber"), cap_back=C("exhaust"))
        b.box((x + side * 0.08, y, -1.95), (nacelle_w * 0.7, 0.24 * H, 0.45), C("gunmetal"), rotation=rot_y(side * 25))

    # Avionics hump behind the mast, mast and fairing.
    top = 0.05 * H + 0.5 * H
    b.box((0, top + 0.08 * H, -1.1), (0.55 * cw, 0.22 * H, 1.4), C("hull"))
    for i in range(3):
        b.box((0, top + 0.19 * H + 0.005, -0.7 - i * 0.3), (0.4 * cw, 0.02, 0.06), C("hull_dark"))
    b.box((0, top + 0.08, -0.05), (0.45 * cw, 0.2, 0.9), C("hull_dark"))
    b.cylinder((0, top - 0.1, 0), (0, top + 0.85, 0), 0.15, C("steel"), segments=10)
    return top + 0.85


def _grille_snout(b, style, cw, H, F, scale=1.0):
    """Beastly snout with a Space Marine-style grille: a low block that tapers to the top, a brow ridge, two lenses,
    horizontal grille slats, angled jaw plates with vents and a chin wedge. F = front face of the grille."""
    k = scale
    b.block((0, -0.14 * H, F - 0.55), (0.62 * cw * k, 0.6 * H * k, 1.1), C("hull"), taper=0.72, taper_depth=0.8)
    b.block((0, (-0.14 + 0.32 * k) * H, F - 0.62), (0.7 * cw * k, 0.12 * H, 0.75), C("hull_light"), taper=0.75,
            rotation=rot_x(8))
    eyes = C(style["lights"]["sensor"])
    for side in (1, -1):
        lens = (side * 0.16 * cw * k, (-0.14 + 0.21 * k) * H, F - 0.2)
        b.cylinder(lens, (lens[0], lens[1], F - 0.02), 0.085, C("gunmetal"), segments=8, cap_end=eyes)
    b.box((0, -0.2 * H, F + 0.01), (0.46 * cw * k, 0.36 * H * k, 0.08), C("hull_dark"))
    for i in range(6):
        b.box((0, -0.2 * H + (i - 2.5) * 0.065 * H * k, F + 0.06), (0.5 * cw * k, 0.024, 0.06), C("gunmetal"))
    for side in (1, -1):
        jaw = rot_y(side * -28)
        b.block((side * 0.29 * cw * k, -0.2 * H, F - 0.12), (0.09, 0.48 * H * k, 0.55), C("hull"), taper=0.8, rotation=jaw)
        for i in range(3):
            b.box((side * (0.33 * cw * k + 0.03), -0.2 * H + (i - 1) * 0.1 * H * k, F - 0.2), (0.03, 0.06 * H, 0.32),
                  C("hull_dark"), rotation=jaw)
    b.box((0, -0.2 * H - 0.22 * H * k, F - 0.15), (0.48 * cw * k, 0.08, 0.45), C("hull_light"), rotation=rot_x(-25))
    b.box((0, -0.2 * H - 0.19 * H * k, F + 0.08), (0.2 * cw * k, 0.06, 0.06), C("hazard"))


def _sensor_turret(b, style, cw, H, F):
    """Chunky nose sensor box with two lenses (attack body)."""
    b.box((0, -0.14 * H, F + 0.2), (0.5 * cw, 0.40 * H, 0.45), C("hull_dark"))
    b.box((0, -0.14 * H + 0.22 * H, F + 0.2), (0.3 * cw, 0.06, 0.3), C("hull"))
    b.cylinder((-0.11 * cw, -0.13 * H, F + 0.42), (-0.11 * cw, -0.13 * H, F + 0.55), 0.11, C("gunmetal"), segments=10,
               cap_end=C("glass"))
    b.cylinder((0.12 * cw, -0.16 * H, F + 0.42), (0.12 * cw, -0.16 * H, F + 0.5), 0.07, C("gunmetal"), segments=8,
               cap_end=C(style["lights"]["sensor"]))


def _assault_body(b, spec, style, ring, tail, W, H, F):
    """Blocky assault hull: every element is a block that tapers slightly to the top. A beastly snout with a
    Space Marine-style grille (horizontal slats, angled jaw plates, chin wedge, brow ridge, two lenses); stepped
    cockpit blocks; side blocks; engine blocks at the shoulders. Returns the rotor height."""
    cw = 0.66 * W
    pod = (W - cw) / 2
    visor = style["canopy"] == "visor"

    # Hull blocks, back to front, and the bridge into the boom.
    b.loft([ring(-2.15, 0.62 * cw, 0.62 * H, 0.15, y=0.12 * H)] + tail, C("hull"), cap_front=C("hull_dark"),
           cap_back=C("hull_dark"))
    main_back, main_front = -2.3, F - 2.4
    b.block((0, -0.05 * H, (main_back + main_front) / 2), (cw, 0.95 * H, main_front - main_back), C("hull"), taper=0.84)
    b.block((0, -0.06 * H, F - 1.75), (0.86 * cw, 0.82 * H, 1.4), C("hull"), taper=0.82)
    # Side blocks (low) and shoulder engine blocks.
    for side in (1, -1):
        b.block((side * (cw / 2 + pod / 2 - 0.02), -0.18 * H, -0.3), (pod, 0.55 * H, 3.4), C("hull"), taper=0.8)
        b.block((side * (cw / 2 + pod / 2 - 0.02), 0.13 * H, -0.3), (pod * 0.8, 0.08, 3.0), C("hull_dark"), taper=0.9)
        x = side * (cw / 2 + 0.12 * W)
        b.block((x, 0.36 * H, -0.75), (0.28 * W, 0.38 * H, 2.3), C("hull"), taper=0.78, front=C("rubber"), back=C("exhaust"))
        b.block((x, 0.36 * H + 0.21 * H, -0.75), (0.2 * W, 0.05, 1.9), C("hull_light"), taper=0.9)
        b.box((x + side * 0.08, 0.36 * H, -1.95), (0.2 * W, 0.26 * H, 0.4), C("gunmetal"), rotation=rot_y(side * 25))

    b.snapshot_surface()

    # Stepped cockpit blocks (gunner low in front, pilot higher behind).
    shell = C("hull_dark") if visor else C("glass")
    b.block((0, 0.42 * H, F - 1.8), (0.66 * cw, 0.28 * H, 1.1), shell, taper=0.7, taper_depth=0.8)
    b.block((0, 0.6 * H, F - 3.0), (0.72 * cw, 0.32 * H, 1.3), shell, taper=0.7, taper_depth=0.8)
    if visor:
        for y, z, width in ((0.48 * H, F - 1.3, 0.4 * cw), (0.68 * H, F - 2.4, 0.46 * cw)):
            b.box((0, y, z), (width, 0.05, 0.07), C("glass"), rotation=rot_x(35))

    _grille_snout(b, style, cw, H, F)

    # Hump behind the mast and the mast.
    top = 0.43 * H
    b.block((0, top + 0.12 * H, -1.1), (0.62 * cw, 0.28 * H, 1.9), C("hull"), taper=0.75)
    for i in range(3):
        b.box((0, top + 0.27 * H, -0.7 - i * 0.32), (0.4 * cw, 0.02, 0.07), C("hull_dark"))
    b.block((0, top + 0.08, -0.05), (0.42 * cw, 0.25, 0.9), C("hull_dark"), taper=0.8)
    b.cylinder((0, top - 0.1, 0), (0, top + 0.95, 0), 0.16, C("steel"), segments=10)
    return top + 0.95


def _heavy_body(b, spec, style, ring, W, H, F, boom, tail_z, layout):
    """Heavy lifter (CH-53 / "Titan lifter" as the base, plus greebles and cyberpunk details): a long boxy cargo hull
    with armour bands, a blunt glazed cockpit, chin sensor and probe, side sponsons, big engine pods with glowing
    intakes, dot-grid vents and chevrons, a cage and pipes on top, a lift frame with hooks under the belly.
    Rotor layouts: single (tail boom, open tail rotor), coaxial (two stacked hubs, pusher prop), tandem (front and rear
    towers) or quad (four ducted fans on outrigger arms). Returns [(rotor hub point, socket scale)]."""
    rng = random.Random(spec["seed"] + 7)
    visor = style["canopy"] == "visor"
    lights = style["lights"]
    ramp = layout in ("tandem", "quad")
    top = H / 2

    # Cargo hull: blunt nose, long box, then a boom (single, coaxial) or a short raised rear ramp (tandem, quad).
    sections = [
        ring(F, 0.60 * W, 0.52 * H, 0.35, y=-0.14 * H),
        ring(F - 0.5, 0.86 * W, 0.80 * H, 0.30, y=-0.06 * H),
        ring(F - 1.3, 0.97 * W, 0.96 * H, 0.28, y=-0.01 * H),
        ring(F - 2.2, W, H, 0.26),
        ring(-2.6, W, H, 0.26),
        ring(-3.4, 0.92 * W, 0.88 * H, 0.28, y=0.05 * H),
    ]
    if ramp:
        sections.append(ring(-4.2, 0.70 * W, 0.50 * H, 0.30, y=0.22 * H))
    else:
        sections += [ring(-3.9, 0.42 * W, 0.42 * H, 0.30, y=0.24 * H),
                     ring(-3.9 - boom * 0.5, 0.26 * W, 0.30 * H, 0.30, y=0.27 * H),
                     ring(tail_z, 0.18 * W, 0.24 * H, 0.30, y=0.30 * H)]
    b.loft(sections, C("hull"), cap_front=C("hull_dark"), cap_back=C("hull_dark"))
    for z, swatch in ((F - 2.0, "hull_dark"), (-0.5, "hull_dark"), (-2.4, "hazard")):
        b.loft([ring(z + 0.08, W * 1.03, H * 1.03, 0.26), ring(z - 0.08, W * 1.03, H * 1.03, 0.26)], C(swatch))
    b.snapshot_surface()

    # Cockpit: faceted glazing with frames (or an armoured visor with slits), chin sensor, probe, floodlights.
    b.loft([chamfer_ring(F + 0.02, 0.50 * W, 0.18 * H, 0.45, y=0.06 * H, top_scale=0.7),
            chamfer_ring(F - 0.55, 0.80 * W, 0.34 * H, 0.40, y=0.20 * H, top_scale=0.75),
            chamfer_ring(F - 1.25, 0.86 * W, 0.30 * H, 0.40, y=0.30 * H, top_scale=0.75)],
           C("hull_dark") if visor else C("glass"), open_back=True)  # the back is inside the hull; the pilot looks through it
    for x in (-0.2 * W, 0.0, 0.2 * W):
        b.box((x, 0.18 * H, F - 0.3), (0.05, 0.3 * H, 0.05), C("hull_dark"), rotation=rot_x(-40))
    b.box((0, 0.27 * H, F - 0.7), (0.84 * W, 0.05, 0.05), C("hull_dark"))
    if visor:
        # View slits flush on the front slope (a ray from the front finds it).
        for y, width in ((0.10 * H, 0.4 * W), (0.30 * H, 0.55 * W)):
            b.lay((0, y, F + 5.0), (0, 0, -1), (0.04, width, 0.07), C("glass"), forward=(0, 1, 0), everything=True)
    b.ico((0.22 * W, -0.36 * H, F - 0.45), 0.28, C("gunmetal"), subdivisions=1)
    b.box((0.22 * W, -0.36 * H, F - 0.17), (0.14, 0.1, 0.04), C(lights["sensor"]))
    b.cylinder((0.36 * W, -0.08 * H, F - 0.4), (0.36 * W, -0.08 * H, F + 1.4), 0.05, C("gunmetal"), segments=6)
    b.cylinder((0.36 * W, -0.08 * H, F + 1.4), (0.36 * W, -0.08 * H, F + 1.6), 0.08, C("hazard"), segments=8,
               end_radius=0.03)
    for i in range(4):
        b.lay(((i - 1.5) * 0.12 * W, -0.33 * H, F + 5.0), (0, 0, -1), (0.05, 0.1, 0.06), C(lights["nose"]),
              forward=(0, 1, 0), everything=True)

    # Cabin windows (a few lit), door outline, ladder.
    z = F - 2.5
    while z > -2.3:
        for side in (1, -1):
            lit = rng.random() < 0.3
            b.on_surface(side, 0.14 * H, z, (0.03, 0.12 * H, 0.32), C("sodium" if lit else "glass"))
        z -= 0.85
    # Door: two frame lines and a sill; the door area is kept free of plates and patches.
    b.on_surface(1, -0.05 * H, F - 2.0, (0.02, 0.6 * H, 0.04), C("hull_dark"))
    b.on_surface(1, -0.05 * H, F - 2.95, (0.02, 0.6 * H, 0.04), C("hull_dark"))
    b.on_surface(1, -0.35 * H, F - 2.475, (0.02, 0.04, 0.95), C("hull_dark"))
    b._covered.append((1, -0.37 * H, 0.27 * H, F - 3.0, F - 1.95))
    for i in range(5):
        b.on_surface(-1, -0.35 * H + i * 0.13 * H, F - 2.3, (0.04, 0.03, 0.4), C("gunmetal"), lift=0.03)
    for z in (F - 2.1, F - 2.5):  # rails that hold the rungs off the hull
        for i in range(4):
            b.on_surface(-1, -0.285 * H + i * 0.13 * H, z, (0.07, 0.15 * H, 0.03), C("gunmetal"), register=False)
    b._covered.append((-1, -0.37 * H, 0.2 * H, F - 2.52, F - 2.08))

    # Sponsons with a front hub, warning strip and chevron.
    for side in (1, -1):
        x, y = side * (W / 2 + 0.32), -0.30 * H
        b.loft([chamfer_ring(F - 2.0, 0.35, 0.18 * H, 0.45, y=y + 0.04 * H, x=x),
                chamfer_ring(F - 2.6, 0.75, 0.36 * H, 0.40, y=y, x=x),
                chamfer_ring(-2.3, 0.75, 0.36 * H, 0.40, y=y, x=x),
                chamfer_ring(-2.9, 0.50, 0.24 * H, 0.45, y=y + 0.03 * H, x=x)], C("hull"), cap_back=C("hull_dark"))
        b.box((x + side * 0.38, y - 0.12 * H, -0.4), (0.02, 0.03, 3.6), C(lights["right" if side > 0 else "left"]))
        hub = (x + side * 0.39, y + 0.02 * H, F - 2.95)
        b.cylinder((x + side * 0.36, hub[1], hub[2]), hub, 0.17, C("gunmetal"), segments=10)
        b.cylinder(hub, (hub[0] + side * 0.03, hub[1], hub[2]), 0.07, C(lights["sensor"]), segments=8)
        for k in (1, -1):
            b.box((x + side * 0.385, y + 0.05 * H + k * 0.04, -2.0 - k * 0.0), (0.02, 0.05, 0.3), C("hazard"),
                  rotation=rot_x(k * 35))

    # Engine pods on the upper sides (seated on the shoulder: the inner half of the pod floor sinks into the hull):
    # glowing intake ring, dot-grid vents, chevrons, exhaust.
    pod_x = W / 2 - 0.15
    ey = min(b.hull_y(pod_x - 0.42, z, default=top) for z in (1.2, 0.0, -1.2)) + 0.475 - 0.08
    for side in (1, -1):
        x = side * pod_x
        b.loft([chamfer_ring(1.6, 0.90, 0.80, 0.50, y=ey, x=x), chamfer_ring(1.2, 1.05, 0.95, 0.45, y=ey, x=x),
                chamfer_ring(-1.2, 1.05, 0.95, 0.45, y=ey, x=x), chamfer_ring(-1.8, 0.80, 0.70, 0.50, y=ey, x=x)],
               C("hull"), cap_front=C("rubber"), cap_back=C("hull_dark"))
        b.cylinder((x, ey, 1.55), (x, ey, 1.78), 0.42, C("gunmetal"), segments=14, cap_end=C("rubber"))
        b.cylinder((x, ey, 1.74), (x, ey, 1.82), 0.11, C(lights["left"]), segments=10)
        for k in range(6):
            turn = rot_z(30 * k)
            b.box((x, ey, 1.79), (0.05, 0.7, 0.02), C("gunmetal"), rotation=turn)
        b.cylinder((x, ey, -1.75), (x, ey, -2.2), 0.32, C("gunmetal"), segments=12, end_radius=0.27,
                   cap_end=C("exhaust"))
        for row in range(3):
            for col in range(5):
                b.box((x + side * 0.53, ey - 0.15 + row * 0.13, 0.5 - col * 0.17), (0.03, 0.06, 0.06), C("hull_dark"))
        for k in (1, -1):
            b.box((x + side * 0.535, ey + 0.05 + k * 0.06, 1.0), (0.02, 0.06, 0.32), C("hazard"), rotation=rot_x(k * 35))
        b.box((x - side * 0.3, top + 0.08, 0), (0.5, 0.3, 2.2), C("hull_dark"))
        # Pipes from the engines towards the tail.
        b.cylinder((side * 0.3 * W, top + 0.12, -1.8), (side * 0.12 * W, 0.40 * H, -3.6), 0.05, C("gunmetal"), segments=6)

    # Top: cage bars, antenna array, dish.
    # Railing: lower and narrower towards the front; posts lean back.
    rail = {1.3: (0.15 * W, 0.36), -0.4: (0.18 * W, 0.46), -2.0: (0.2 * W, 0.55)}  # z: (half width, height)
    for side in (1, -1):
        b.cylinder((side * rail[1.3][0], top + rail[1.3][1], 1.3 - 0.12),
                   (side * rail[-2.0][0], top + rail[-2.0][1], -2.0 - 0.12), 0.03, C("steel"), segments=5)
        for z, (x, h) in rail.items():
            b.cylinder((side * (x + 0.04), top + 0.02, z), (side * x, top + h, z - 0.12), 0.03, C("steel"), segments=5)
    for z in (1.3, -2.0):
        x, h = rail[z]
        b.cylinder((x, top + h, z - 0.12), (-x, top + h, z - 0.12), 0.03, C("steel"), segments=5)
    for _ in range(rng.randint(3, 5)):
        x, z = rng.uniform(-0.3, 0.3) * W, rng.uniform(F - 2.0, F - 1.2)
        b.cylinder((x, top - 0.02, z), (x, top + rng.uniform(0.5, 1.0), z - 0.1), 0.015, C("steel"), segments=5)
    b.cylinder((-0.25 * W, top + 0.02, -3.0), (-0.25 * W, top + 0.25, -3.0), 0.05, C("gunmetal"), segments=6)
    b.cylinder((-0.25 * W, top + 0.25, -3.0), (-0.25 * W, top + 0.35, -2.95), 0.32, C("hull_light"), segments=12,
               end_radius=0.08)

    # Belly lift frame with four hanging cables and hooks.
    fy = -top - 0.03
    for dx, dz, sx, sz in ((0, 0.2, 1.4, 0.08), (0, -1.4, 1.4, 0.08), (0.7, -0.6, 0.08, 1.6), (-0.7, -0.6, 0.08, 1.6)):
        b.box((dx, fy, dz), (sx, 0.08, sz), C("gunmetal"))
    for dx, dz in ((0.66, 0.16), (-0.66, 0.16), (0.66, -1.36), (-0.66, -1.36)):
        b.cylinder((dx, fy, dz), (dx * 0.6, fy - 1.2, dz * 0.8 - 0.3), 0.015, C("steel"), segments=4)
        b.box((dx * 0.6, fy - 1.26, dz * 0.8 - 0.3), (0.1, 0.12, 0.05), C("hazard"))

    return _heavy_rotors(b, style, W, H, F, tail_z, layout)


def _heavy_rotors(b, style, W, H, F, tail_z, layout):
    top = H / 2
    if layout == "tandem":
        front_z, rear_z = F - 1.8, -3.4
        b.block((0, top + 0.3, front_z), (0.5 * W, 0.9, 1.6), C("hull"), taper=0.65, taper_depth=0.8)
        b.cylinder((0, top + 0.6, front_z), (0, top + 1.2, front_z), 0.18, C("steel"), segments=10)
        b.block((0, top + 0.75, rear_z), (0.6 * W, 1.9, 2.2), C("hull"), taper=0.55, taper_depth=0.65)
        for i in range(4):  # vents on the tower's back slope
            b.lay((0, top + 0.9 + i * 0.18, rear_z - 5.0), (0, 0, 1), (0.03, 0.32 * W, 0.04), C("hull_dark"),
                  forward=(0, 1, 0), everything=True)
        b.cylinder((0, top + 1.5, rear_z), (0, top + 2.1, rear_z), 0.18, C("steel"), segments=10)
        b.box((0, top + 0.12, (front_z + rear_z) / 2), (0.28 * W, 0.24, front_z - rear_z), C("hull_dark"))
        return [((0, top + 1.2, front_z), 1.0), ((0, top + 2.1, rear_z), 1.0)]

    if layout == "quad":
        hubs = []
        hx, hy = W / 2 + 3.1, top + 0.35
        for z in (F - 1.8, -2.9):
            for side in (1, -1):
                x = side * hx
                # Outrigger arm with a brace, motor pod, duct with a glowing lip and struts.
                b.box(((side * W / 2 + x) / 2, top + 0.15, z), (hx - W / 2, 0.25, 0.4), C("gunmetal"))
                b.cylinder((side * (W / 2 - 0.1), 0.1 * H, z), (x - side * 1.0, top + 0.1, z), 0.06, C("steel"), segments=6)
                b.cylinder((x, hy - 0.35, z), (x, hy + 0.2, z), 0.38, C("hull"), segments=12, cap_end=C("hull_dark"))
                b.duct((x, hy, z), 2.75, 2.55, 0.55, C("hull"), inner_swatch=C("hull_dark"))
                b.duct((x, hy + 0.28, z), 2.58, 2.54, 0.03, C(style["lights"]["left"]))
                for k in range(3):
                    turn = rot_y(90 + 120 * k)
                    centre = Vector((x, hy - 0.1, z)) + turn @ Vector((1.45, 0, 0))
                    b.box(tuple(centre), (2.2, 0.06, 0.12), C("gunmetal"), rotation=turn)
                hubs.append(((x, hy + 0.2, z), 0.34))
        return hubs

    # Single or coaxial: transmission housing and mast at the centre.
    b.block((0, top + 0.2, -0.3), (0.55 * W, 0.45, 2.6), C("hull"), taper=0.8)
    for i in range(4):
        b.box((0, top + 0.43, 0.6 - i * 0.45), (0.38 * W, 0.02, 0.08), C("hull_dark"))
    if layout == "coaxial":
        b.cylinder((0, top + 0.4, 0), (0, top + 2.05, 0), 0.2, C("steel"), segments=10)
        b.cylinder((0, top + 1.25, 0), (0, top + 1.8, 0), 0.38, C("gunmetal"), segments=12)
        # Pusher prop behind the tail.
        prop_z, prop_y = tail_z - 0.95, 0.30 * H
        b.cylinder((0, prop_y, tail_z - 0.2), (0, prop_y, prop_z - 0.25), 0.22, C("gunmetal"), segments=10, end_radius=0.06)
        for k in range(6):
            turn = rot_z(60 * k)
            centre = Vector((0, prop_y, prop_z)) + turn @ Vector((0, 0.7, 0))
            b.box(tuple(centre), (0.14, 1.3, 0.04), C("carbon"), rotation=turn @ rot_y(20))
        return [((0, top + 1.05, 0), 1.0), ((0, top + 2.05, 0), 1.0)]
    b.cylinder((0, top + 0.4, 0), (0, top + 1.05, 0), 0.2, C("steel"), segments=10)
    return [((0, top + 1.05, 0), 1.0)]


def _heavy_gear(b, W, H, F):
    """Dual main wheels under the sponsons and a dual nose wheel."""
    ground = -H / 2 - 0.75
    for side in (1, -1):
        x = side * (W / 2 + 0.32)
        b.cylinder((x, -0.40 * H, -0.6), (x, ground + 0.42, -0.6), 0.08, C("gunmetal"), segments=6)
        for dx in (-0.16, 0.16):
            b.cylinder((x + dx - 0.07, ground + 0.42, -0.6), (x + dx + 0.07, ground + 0.42, -0.6), 0.42, C("rubber"),
                       segments=14)
            b.cylinder((x + dx - 0.08, ground + 0.42, -0.6), (x + dx + 0.08, ground + 0.42, -0.6), 0.18, C("steel"),
                       segments=8)
    z = F - 1.0
    b.cylinder((0, -0.45 * H, z), (0, ground + 0.32, z - 0.1), 0.07, C("gunmetal"), segments=6)
    for dx in (-0.13, 0.13):
        b.cylinder((dx - 0.06, ground + 0.32, z - 0.1), (dx + 0.06, ground + 0.32, z - 0.1), 0.32, C("rubber"), segments=12)


def _sponson_pylons(b, spec, W, H):
    """Hardpoints on stubs off the sponsons. Pylon 0 is on the right (+x)."""
    points = []
    for i in range(spec["slots"].count("Pylon")):
        side = 1 if i % 2 == 0 else -1
        x, y = side * (W / 2 + 0.32 + 0.5), -0.30 * H
        _swept_wing(b, side, abs(x) - 0.35, abs(x) + 0.05, y, -0.24, 0.62, 0.42, 0.12, 0.12, C("hull_dark"))
        b.box((x, y - 0.1, -0.3), (0.1, 0.1, 0.5), C("gunmetal"))
        points.append((x, y - 0.15, -0.3))
    return points


def _tandem_cockpit(b, style, cw, H, F):
    """Two stepped canopies of flat panes. Visor styles armour them and leave view slits."""
    visor = style["canopy"] == "visor"
    shell = C("hull_dark") if visor else C("glass")
    front = [chamfer_ring(F - 0.6, 0.55 * cw, 0.14 * H, 0.5, y=0.16 * H, top_scale=0.6),
             chamfer_ring(F - 1.1, 0.82 * cw, 0.32 * H, 0.5, y=0.30 * H, top_scale=0.6),
             chamfer_ring(F - 1.9, 0.86 * cw, 0.34 * H, 0.5, y=0.38 * H, top_scale=0.6)]
    rear = [chamfer_ring(F - 1.85, 0.86 * cw, 0.30 * H, 0.5, y=0.50 * H, top_scale=0.6),
            chamfer_ring(F - 2.6, 0.86 * cw, 0.40 * H, 0.5, y=0.56 * H, top_scale=0.6),
            chamfer_ring(F - 3.3, 0.70 * cw, 0.20 * H, 0.5, y=0.54 * H, top_scale=0.6)]
    b.loft(front, shell)
    b.loft(rear, shell)
    # Frames between the panes and over the top.
    # Its top meets the front canopy's top: under the pilot's sight line from the rear seat (cockpit view).
    b.box((0, 0.38 * H, F - 1.88), (0.72 * cw, 0.34 * H, 0.06), C("hull_dark"))
    b.box((0, 0.73 * H, F - 2.45), (0.06, 0.04, 1.2), C("hull_dark"))
    b.box((0, 0.45 * H, F - 1.25), (0.06, 0.04, 1.0), C("hull_dark"))
    if visor:
        for z, y, width in ((F - 1.0, 0.36 * H, 0.42 * cw), (F - 2.2, 0.66 * H, 0.46 * cw)):
            b.box((0, y, z), (width, 0.05, 0.07), C("glass"), rotation=rot_x(30))
        for side in (1, -1):
            for y, z, length in ((0.38 * H, F - 1.5, 0.6), (0.58 * H, F - 2.6, 0.8)):
                b.lay((side * 50.0, y, z), (-side, 0, 0), (0.03, 0.06, length), C("glass"), everything=True)


def _wheels(b, style, W, H, F, tail_z):
    """Main wheels on struts under the cheek pods and a tail wheel (attack bodies)."""
    ground = -H / 2 - 0.75
    for side in (1, -1):
        x = side * 0.40 * W
        b.cylinder((side * 0.36 * W, -0.30 * H, F - 1.6), (x, ground + 0.3, F - 1.7), 0.06, C("gunmetal"), segments=6)
        b.cylinder((side * 0.36 * W, -0.30 * H, F - 2.4), (x, ground + 0.3, F - 1.7), 0.045, C("gunmetal"), segments=6)
        b.cylinder((x - side * 0.1, ground + 0.3, F - 1.7), (x + side * 0.1, ground + 0.3, F - 1.7), 0.3, C("rubber"),
                   segments=12)
        b.cylinder((x - side * 0.12, ground + 0.3, F - 1.7), (x + side * 0.12, ground + 0.3, F - 1.7), 0.13, C("steel"),
                   segments=8)
    z = tail_z + 0.7
    b.cylinder((0, 0.12 * H, z), (0, ground + 0.18, z - 0.2), 0.05, C("gunmetal"), segments=6)
    b.cylinder((-0.07, ground + 0.18, z - 0.2), (0.07, ground + 0.18, z - 0.2), 0.18, C("rubber"), segments=10)


def _nacelle_x(b, W, H):
    """|x| of an attack-body engine nacelle centre: its inner 20 % sinks into the core side (found on the hull at the
    nacelle height, the narrowest point along it)."""
    nacelle_w = 0.26 * W
    core = min(b.hull_x(1, 0.36 * H, z, default=0.31 * W) for z in (0.2, -0.6, -1.4))
    return core + nacelle_w * 0.3


def _attack_cage(b, W, H):
    """Slat armour on the outer side of each engine nacelle."""
    y = 0.36 * H
    x_out = _nacelle_x(b, W, H) + 0.26 * W / 2 + 0.12
    for side in (1, -1):
        _slat_cage(b, side * x_out, side, y - 0.17 * H, 0.2, -1.6, 0.22 * H, 0.34 * H, standoff=0.12)


def _canopy(b, style, W, H, F):
    rings = [
        chamfer_ring(F - 0.7, 0.45 * W, 0.25 * H, 0.45, y=0.10 * H, top_scale=0.7),
        chamfer_ring(F - 1.6, 0.80 * W, 0.40 * H, 0.45, y=0.36 * H, top_scale=0.7),
        chamfer_ring(0.9, 0.80 * W, 0.36 * H, 0.45, y=0.44 * H, top_scale=0.7),
        chamfer_ring(0.4, 0.50 * W, 0.16 * H, 0.45, y=0.46 * H, top_scale=0.7),
    ]
    if style["canopy"] != "visor":
        # Bubble: dark glass, half sunk into the nose, with a frame rib.
        b.loft(rings, C("glass"))
        b.box((0, 0.30 * H, F - 1.25), (0.82 * W, 0.08, 0.08), C("hull_dark"), rotation=rot_x(-35))
        return

    # Visor: an armoured hood; the crew sees out through narrow slits (front slope and sides).
    rings = [chamfer_ring(z, w * 1.02, h, 0.55, y=y, top_scale=0.6) for z, w, h, y in
             ((F - 0.7, 0.45 * W, 0.25 * H, 0.10 * H), (F - 1.6, 0.80 * W, 0.40 * H, 0.36 * H),
              (0.9, 0.80 * W, 0.36 * H, 0.44 * H), (0.4, 0.50 * W, 0.16 * H, 0.46 * H))]
    b.loft(rings, C("hull_dark"))
    front_top = (F - 0.7, 0.10 * H + 0.125 * H)
    back_top = (F - 1.6, 0.36 * H + 0.20 * H)
    slope = math.degrees(math.atan2(back_top[1] - front_top[1], front_top[0] - back_top[0]))
    for t, width in ((0.35, 0.34 * W), (0.62, 0.42 * W)):
        z = front_top[0] + (back_top[0] - front_top[0]) * t
        y = front_top[1] + (back_top[1] - front_top[1]) * t + 0.02
        b.box((0, y, z), (width, 0.05, 0.07), C("glass"), rotation=rot_x(slope))
    for side in (1, -1):
        b.box((side * 0.31 * W, 0.40 * H, (F - 1.6 + 0.9) / 2), (0.06, 0.06, 0.8), C("glass"))
    # Brow plate over the slits.
    b.box((0, back_top[1] - 0.01, back_top[0] + 0.05), (0.62 * W, 0.05, 0.3), C("hull"), rotation=rot_x(slope * 0.5))


def _engines(b, spec, style, W, H):
    count = spec.get("engines", 2)
    xs = [0.0] if count == 1 else [0.30 * W, -0.30 * W]
    width = 0.55 * W if count == 1 else 0.36 * W
    y = 0.5 * H + 0.12 * H
    facet = min(0.35 * style["facet"], 0.6)
    for x in xs:
        b.loft([
            chamfer_ring(0.5, width * 0.8, 0.26 * H, facet, y=y, x=x),
            chamfer_ring(0.2, width, 0.32 * H, facet, y=y, x=x),
            chamfer_ring(-1.5, width * 0.95, 0.30 * H, facet, y=y, x=x),
            chamfer_ring(-1.9, width * 0.75, 0.24 * H, facet, y=y, x=x),
        ], C("hull"), cap_front=C("rubber"), cap_back=C("exhaust"))
        # Intake grille slats.
        for i in range(3):
            b.box((x, y - 0.06 * H + i * 0.06 * H, 0.52), (width * 0.6, 0.02, 0.03), C("gunmetal"))
        if style["wear"] > 0:
            # Exhaust shroud (the soot behind it is painted by the texture bake).
            b.box((x, y + 0.02, -1.95), (width * 0.85, 0.30 * H, 0.25), C("gunmetal"))


def _swept_wing(b, side, root_x, tip_x, y, z, root_chord, tip_chord, sweep, thickness, swatch, droop=0.0):
    """Wing or stabiliser half on one side: a rounded section that narrows (chord) and moves back (sweep) and down
    (droop) towards the tip. Returns x -> z of the centre line at |x| (for hardpoints and end plates)."""
    n = max(1, int(FORM["corner_segments"]))
    sections = []
    for x, chord, thick, dy, dz in ((root_x, root_chord, thickness, 0.0, 0.0),
                                    (tip_x, tip_chord, thickness * 0.7, -droop, -sweep)):
        outline = _rounded_rect(chord / 2, thick / 2, thick * 0.9, thick * 0.45, n)
        sections.append([(side * x, y + dy + v, z + dz + u) for u, v in outline])
    b.loft(sections, swatch)
    return lambda x: z - sweep * (abs(x) - root_x) / (tip_x - root_x)


def _end_plate(b, x, y, z, chord, height, thickness, swatch, cant=0.0):
    """Swept end plate (no free-standing 90° plates): lower at the front, the leading edge raked back, the trailing
    edge raked the other way, centred on (x, y, z). cant: lean out (signed, x per metre of height)."""
    c, h = chord / 2, height / 2
    b.fin([(z - c, y - h), (z + c, y - h * 0.6), (z + c * 0.35, y + h * 0.35), (z - c * 1.15, y + h)],
          x, thickness, swatch, cant=cant)


def _slat_cage(b, x, side, y0, z_front, z_back, h_front, h_back, spacing=0.26, lean=0.12, radius=0.02,
               standoff=0.14):
    """Slat armour on one side: round bars that lean back, lower towards the front (height h_front .. h_back above
    y0, top rail sloping down to the front), on stand-offs back to whatever is behind them (found by ray; `standoff`
    when nothing is near)."""
    # Stand-off ends first, before the bars exist (the rays must not hit the cage itself).
    ends = []
    for z, h in ((z_front, h_front), (z_back, h_back)):
        y = y0 + h * 0.45
        inner = b.hull_x(side, y, z, everything=True)
        if inner is None or not 0.0 < abs(x) - inner < 0.6:
            inner = abs(x) - standoff
        ends.append((side * (inner - 0.02), y, z))
    count = max(2, int(round((z_front - z_back) / spacing)) + 1)
    tops = []
    for i in range(count):
        t = i / (count - 1)
        z = z_front + (z_back - z_front) * t
        h = h_front + (h_back - h_front) * t
        top = (x - side * 0.03, y0 + h, z - lean)
        b.cylinder((x, y0, z), top, radius, C("gunmetal"), segments=5)
        tops.append(top)
    b.cylinder((x, y0, z_front + 0.04), (x, y0, z_back - 0.04), radius, C("gunmetal"), segments=5)
    b.cylinder(tops[0], tops[-1], radius, C("gunmetal"), segments=5)
    for end in ends:
        b.cylinder(end, (x, end[1], end[2] - lean * 0.45), radius, C("gunmetal"), segments=5)


def _wings_or_stubs(b, spec, style, W, H):
    """Stub wings (pylons under the tips) or a small side mount per pylon. Pylon 0 is on the right (+x)."""
    pylons = spec["slots"].count("Pylon")
    points = []
    wing_y, wing_z = -0.05 * H, -0.2
    lights = style["lights"]
    if spec.get("wings", False):
        span = spec["wing_span"]
        thickness = 0.12 if style["facet"] <= 1.0 else 0.18
        # The root sinks into the hull side (found by ray at the wing height).
        root_x = b.hull_x(1, wing_y, wing_z, default=W / 2) - 0.1
        tip_x, sweep, droop = W / 2 + span, 0.3, 0.06
        for side in (1, -1):
            # Swept, tapered wing; a swept end plate (low at the front, raked leading edge) leaning out at the tip.
            centre_z = _swept_wing(b, side, root_x, tip_x, wing_y, wing_z, 1.15, 0.7, sweep, thickness, C("hull"),
                                   droop=droop)
            tip_y, tip_z = wing_y - droop, centre_z(tip_x)
            _end_plate(b, side * (tip_x + 0.02), tip_y - 0.04, tip_z, 0.8, 0.4, 0.06, C("hazard"), cant=side * 0.12)
            # Nav light on the plate's raked leading edge (half way up it; the plate leans out with the cant).
            light_y = tip_y - 0.04 - 0.125 * 0.4 / 2
            b.box((side * (tip_x + 0.02 + 0.12 * 0.175 + 0.01), light_y, tip_z + 0.675 * 0.4), (0.06, 0.06, 0.07),
                  C(lights["right"] if side > 0 else lights["left"]))
            # Pylon hardpoint under the outer wing, following the sweep.
            x = W / 2 + span * 0.7
            b.block((side * x, wing_y - droop * 0.7 - 0.1, centre_z(x)), (0.1, 0.1, 0.6), C("gunmetal"), taper=1.0,
                    taper_depth=0.8, rotation=rot_x(180))
        for i in range(pylons):
            side = 1 if i % 2 == 0 else -1
            x = W / 2 + span * 0.7
            points.append((side * x, wing_y - droop * 0.7 - 0.15, wing_z - sweep * (x - root_x) / (tip_x - root_x)))
    else:
        for i in range(pylons):
            side = 1 if i % 2 == 0 else -1
            root = b.hull_x(side, -0.15 * H, wing_z + 0.06, default=W / 2) - 0.06
            _swept_wing(b, side, root, W / 2 + 0.47, -0.15 * H, wing_z + 0.06, 0.62, 0.42, 0.12, 0.12,
                        C("hull_dark"))
            b.box((side * (W / 2 + 0.38), -0.15 * H - 0.1, wing_z), (0.1, 0.1, 0.5), C("gunmetal"))
            points.append((side * (W / 2 + 0.38), -0.15 * H - 0.15, wing_z))
    return points


def _skids(b, style, F, W, H):
    y = -H / 2 - 0.55
    heavy = style["wear"] > 0
    radius = 0.085 if heavy else 0.07
    for side in (1, -1):
        x = side * 0.55 * W
        b.cylinder((x, y, -1.8), (x, y, F - 1.3), radius, C("gunmetal"), segments=8)
        b.cylinder((x, y, F - 1.3), (x, y + 0.25, F - 0.95), radius, C("gunmetal"), segments=8)
        for z in (0.8, -1.2):
            b.cylinder((side * 0.32 * W, -H / 2 + 0.15, z), (x, y, z), 0.05, C("gunmetal"), segments=6)
        if heavy:
            # Boarding step plate.
            b.box((x - side * 0.08, y + radius + 0.005, 0.2), (0.3, 0.03, 0.4), C("bare"))
    if heavy:
        b.cylinder((0.55 * W, y, -1.6), (-0.55 * W, y, -1.6), 0.05, C("gunmetal"), segments=6)


def _tail(b, style, W, H, tail_z, open_rotor=False, tail_rotor=True):
    base_y = 0.26 * H
    sweep = 0.5 if style["facet"] <= 1.0 else 0.8
    # Fin (convex outline in the z, y plane) with a tip.
    b.fin([(tail_z + 0.9, base_y), (tail_z - 0.25, base_y), (tail_z - sweep, base_y + 1.35), (tail_z + 0.05, base_y + 1.3)],
          0.0, 0.1 if style["facet"] <= 1.0 else 0.14, C("hull"))
    b.box((0, base_y + 1.33, tail_z - sweep * 0.45), (0.14, 0.06, 0.55), C("hazard"))
    if not style["neon_strips"]:
        b.box((0, base_y + 1.38, tail_z - sweep * 0.45 - 0.2), (0.06, 0.05, 0.06), C(style["lights"]["right"]))
    # Stabiliser: swept and tapered, with swept end plates leaning out.
    for side in (1, -1):
        centre_z = _swept_wing(b, side, 0.0, 0.95, base_y - 0.02, tail_z + 0.75, 0.55, 0.34, 0.18, 0.07, C("hull"))
        _end_plate(b, side * 0.96, base_y + 0.06, centre_z(0.95), 0.42, 0.3, 0.05, C("hull_dark"), cant=side * 0.15)
    if not tail_rotor:
        return
    if open_rotor:
        # Open four-blade tail rotor on a gearbox at the top of the fin, right side.
        hub_y, hub_z = base_y + 1.05, tail_z - 0.3
        b.box((0.1, hub_y, hub_z), (0.14, 0.22, 0.3), C("gunmetal"))
        b.cylinder((0.1, hub_y, hub_z), (0.3, hub_y, hub_z), 0.07, C("steel"), segments=8)
        for angle in (0, 55, 180, 235):
            turn = rot_x(angle)
            centre = Vector((0.28, hub_y, hub_z)) + turn @ Vector((0, 0.45, 0))
            b.box(tuple(centre), (0.03, 0.9, 0.14), C("carbon"), rotation=turn)
        return
    # Ducted tail rotor (fenestron) on the fin.
    shroud_y, shroud_z = base_y + 0.55, tail_z - 0.05
    b.cylinder((-0.13, shroud_y, shroud_z), (0.13, shroud_y, shroud_z), 0.55, C("hull_dark"), segments=16,
               cap_start=C("rubber"), cap_end=C("rubber"))
    b.cylinder((-0.16, shroud_y, shroud_z), (0.16, shroud_y, shroud_z), 0.1, C("steel"), segments=8)


def _details(b, rng, spec, style, W, H, F, tail_z, boom_rings=None):
    """Seeded details, mirrored left/right so they read as designed. boom_rings: the tail boom sections (warning
    bands and antennas go on the boom; None = no boom)."""
    # Everything on the sides is laid flat on the hull (projected, follows its slope) and kept from overlapping.
    # Side vents (a slat stack, mirrored).
    for side in (1, -1):
        for i in range(4):
            b.on_surface(side, 0.05 * H + i * 0.09, -0.4, (0.04, 0.04, 0.6), C("hull_dark"))
    if style["neon_strips"]:
        strip_back, strip_front = -1.8, F - 1.8
        for side in (1, -1):
            b.on_surface(side, -0.2 * H, (strip_back + strip_front) / 2, (0.02, 0.04, strip_front - strip_back),
                         C("neon_green"))
    # Armour plates (mirrored; a second, smaller layer in heavy styles). Skipped where something already is.
    plates = round(spec.get("armour", 0.5) * 6)
    for n in range(plates * 2):  # more tries than plates: some spots are taken
        if n >= plates and rng.random() < 0.5:
            continue
        y = rng.uniform(-0.3, 0.15) * H
        z = rng.uniform(-1.6, F - 2.2)
        size = (0.04, rng.uniform(0.25, 0.45), rng.uniform(0.6, 1.1))
        swatch = "hull_light" if style["wear"] <= 0 or n % 3 else "concrete"
        for side in (1, -1):
            if b.on_surface(side, y, z, size, C(swatch), avoid=True) and style["armour_layers"] > 1:
                b.on_surface(side, y + 0.03, z - 0.05, (0.04, size[1] * 0.7, size[2] * 0.75), C("hull"), lift=0.035,
                             register=False)
    # Belly plate laid on the underside, the beacon on the plate.
    b.lay((0, -50.0, -0.3), (0, 1, 0), (0.06, 0.45 * W, 2.4), C("hull_dark"))
    b.lay((0, -50.0, -0.9), (0, 1, 0), (0.06, 0.12, 0.12), C(style["lights"]["right"]), everything=True)
    # Antennas on the boom top (and a small dish in cluttered styles).
    low, high = style["antennas"] if boom_rings is not None else (0, 0)
    for i in range(rng.randint(low, high)):
        z = rng.uniform(tail_z + 1.0, -2.8)
        height = rng.uniform(0.4, 0.8)
        x = rng.uniform(-0.08, 0.08) * (1 if style["asymmetry"] > 0 else 0)
        y = b.hull_y(x, z, default=0.26 * H + 0.12) - 0.02
        b.cylinder((x, y, z), (x, y + height, z - 0.15), 0.015, C("steel"), segments=5)
    if high >= 4:
        dish_z = tail_z + 1.6
        y = b.hull_y(0, dish_z, default=0.40 * H) - 0.02
        b.cylinder((0, y, dish_z), (0, y + 0.14, dish_z), 0.05, C("gunmetal"), segments=6)
        b.cylinder((0, y + 0.14, dish_z), (0, y + 0.22, dish_z + 0.05), 0.2, C("hull_light"),
                   segments=10, end_radius=0.06)
    # Sensor ball under the nose.
    if spec.get("sensor", False):
        b.ico((0, -0.32 * H, F - 0.75), 0.22, C("gunmetal"), subdivisions=1)
        b.box((0, -0.32 * H, F - 0.54), (0.12, 0.08, 0.04), C(style["lights"]["sensor"]))
    # Warning bands round the boom.
    for z in (-3.0, -3.3) if boom_rings is not None else ():
        if z > tail_z + 1.2:
            _band(b, boom_rings, z, C("hazard"))


def _band(b, rings, z, swatch, grow=0.025, width=0.08):
    """A band round a loft (e.g. the boom) at forward position z: its section there, grown by `grow`, `width` long."""
    zs = [ring[0][2] for ring in rings]
    for k in range(len(rings) - 1):
        if zs[k + 1] <= z <= zs[k]:
            t = (zs[k] - z) / (zs[k] - zs[k + 1])
            section = [tuple(a[n] + (c[n] - a[n]) * t for n in range(2)) for a, c in zip(rings[k], rings[k + 1])]
            break
    else:
        return
    cx = sum(p[0] for p in section) / len(section)
    cy = sum(p[1] for p in section) / len(section)
    grown = []
    for x, y in section:
        dx, dy = x - cx, y - cy
        length = math.hypot(dx, dy) or 1.0
        grown.append((cx + dx * (1 + grow / length), cy + dy * (1 + grow / length)))
    b.loft([[(x, y, z + width / 2) for x, y in grown], [(x, y, z - width / 2) for x, y in grown]], swatch)


def _cage(b, style, spec, W, H, boom):
    """Slat armour: bars on stand-offs round the engines and the front of the boom (stops rockets early)."""
    count = spec.get("engines", 2)
    width = 0.55 * W if count == 1 else 0.36 * W
    y = 0.5 * H + 0.12 * H
    outer = (0.30 * W if count > 1 else 0.0) + width / 2 + 0.14
    for side in (1, -1):
        _slat_cage(b, side * outer, side, y - 0.17 * H, 0.3, -1.7, 0.22 * H, 0.34 * H)
    # Boom cage.
    for side in (1, -1):
        _slat_cage(b, side * (0.17 * W + 0.12), side, 0.04 * H, -2.8, -2.8 - 5 * 0.28, 0.24 * H, 0.36 * H,
                   spacing=0.28, standoff=0.12)


def _conduits(b, W, H, F, tail_z, ys, cable_to_tail=True):
    """External pipes and a cable bundle along each side at heights ys (pipe, cable): retrofitted, function over form.
    Each line runs just off the hull (found by ray); brackets and clamps reach back to it."""
    zs = [F - 2.0 + (-1.5 - (F - 2.0)) * t / 4 for t in range(5)]
    for side in (1, -1):
        outer = 0.0
        for y, radius, swatch in ((ys[0], 0.035, "gunmetal"), (ys[1], 0.025, "rubber")):
            hull = [b.hull_x(side, y, z, default=W / 2) for z in zs]
            x = side * (max(hull) + radius + 0.015)
            outer = max(outer, abs(x) + radius)
            b.cylinder((x, y, zs[0]), (x, y, zs[-1]), radius, C(swatch), segments=6)
            for z, h in ((zs[0], hull[0]), (zs[-1], hull[-1])):
                b.cylinder((x, y, z), (side * (h - 0.02), y, z), radius, C(swatch), segments=6)
        # Clamps from the hull to round both lines.
        y = (ys[0] + ys[1]) / 2
        z = F - 2.2
        while z > -1.5:
            inner = b.hull_x(side, y, z, default=W / 2) - 0.02
            b.box((side * (inner + outer + 0.02) / 2, y, z), (outer + 0.02 - inner, ys[1] - ys[0] + 0.1, 0.06),
                  C("hull_dark"))
            z -= 0.7
    if cable_to_tail:
        b.cylinder((0.09, 0.40 * H, -2.4), (0.06, 0.38 * H, tail_z + 1.1), 0.03, C("rubber"), segments=6)


def _retrofits(b, rng, style, W, H, F):
    """One-sided add-ons, picked by the seed: the machine was fixed and extended in the field. Each one is fixed to
    the hull found by ray at its spot."""
    options = ["searchlight", "reel", "ammo", "sensor_mast"]
    rng.shuffle(options)
    for name in options[:2 + round(style["asymmetry"])]:
        if name == "searchlight":
            y, z = -0.18 * H, F - 1.9
            hx = b.hull_x(1, y, z, default=W / 2, everything=True)
            b.box((hx + 0.18, y, z), (0.4, 0.08, 0.1), C("gunmetal"))
            b.cylinder((hx + 0.4, y - 0.06, z - 0.2), (hx + 0.4, y - 0.06, z + 0.15), 0.15, C("gunmetal"),
                       segments=10, cap_end=C(style["lights"]["left"]))
        elif name == "reel":
            y = 0.02 * H
            hx = max(b.hull_x(-1, y, z, default=W / 2, everything=True) for z in (-1.3, -1.1, -0.9))
            b.box((-(hx + 0.13), y, -1.1), (0.3, 0.34, 0.5), C("hull_dark"))
            b.cylinder((-(hx + 0.31), y, -1.3), (-(hx + 0.31), y, -0.9), 0.14, C("rust"), segments=10)
        elif name == "ammo":
            for i in range(2):
                y, z = -0.3 * H, 0.2 - i * 0.42
                hx = b.hull_x(-1, y, z, default=W / 2, everything=True)
                b.box((-(hx + 0.11), y, z), (0.26, 0.3, 0.38), C("primer"))
        elif name == "sensor_mast":
            x, z = 0.3 * W, 0.35
            base = b.hull_y(x, z, default=0.62 * H, everything=True) - 0.02
            b.cylinder((x, base, z), (x, base + 0.6, z), 0.03, C("steel"), segments=5)
            b.box((x, base + 0.65, z), (0.22, 0.12, 0.16), C("hull_light"))
            b.box((x, base + 0.65, z + 0.09), (0.1, 0.05, 0.02), C(style["lights"]["sensor"]))


def _wear(b, rng, style, W, H, F):
    """Replacement panels in primer, rust patches and bare edges; not mirrored, so the machine looks used."""
    for _ in range(round(style["wear"] * 6)):
        side = rng.choice((1, -1))
        y = rng.uniform(-0.25, 0.25) * H
        z = rng.uniform(-1.5, F - 2.2)
        b.on_surface(side, y, z, (0.025, rng.uniform(0.2, 0.4), rng.uniform(0.3, 0.7)),
                     C(rng.choice(("primer", "primer", "rust", "concrete"))), avoid=True)
    # Scuffed bare metal on the nose and wing leading edges.
    b.box((0, -0.24 * H if style["nose"] == "chisel" else -0.12 * H, F - 0.03), (0.5 * W, 0.03, 0.05), C("bare"))


HANGING_SLOTS = ("Nose", "Pylon", "Turret", "Utility", "Winch")


def _seat_sockets(b, sockets):
    """Moves each socket onto the model: a hanging module's attach point goes to the underside right above it (a ray
    from below finds it, up to 0.3 m down or 0.6 m up); an engine stack that floats goes down onto the deck. Rotor
    hubs are already on the mast tops."""
    seated = []
    for slot, index, position, scale in sockets:
        x, y, z = position
        if slot in HANGING_SLOTS:
            point, _normal = b.hit((x, y - 0.3, z), (0, 1, 0), everything=True, distance=0.9)
            if point is not None:
                y = point.y + 0.01
        elif slot == "Engine":
            # Only when it floats: not near any surface and nothing right above it (not inside a part).
            _co, _n, _i, gap = b._tree(True).find_nearest((x, y, z))
            above, _normal = b.hit((x, y, z), (0, 1, 0), everything=True, distance=0.3)
            point, _normal = b.hit((x, y, z), (0, -1, 0), everything=True, distance=1.0)
            if gap is not None and gap > 0.06 and above is None and point is not None:
                y = point.y + 0.06
        seated.append((slot, index, (round(x, 4), round(y, 4), round(z, 4)), scale))
    return seated


def _sockets(spec, W, H, F, rotors, pylon_points, body="utility"):
    """One socket per slot in the chassis' slot list, indexed per type (game order): (slot, index, point, scale)."""
    positions = {
        "Nose": [(0, -0.30 * H, F - 0.9)],
        "Pylon": pylon_points,
        "Turret": [(0, -H / 2 - 0.05, 0.9)],
        "Utility": [(0.30 * W, 0.08 * H, -2.0), (-0.30 * W, 0.08 * H, -2.0)],
        "Winch": [(0, -H / 2 - 0.05, -0.3)],
        "Rotor": [point for point, _scale in rotors],
        "Engine": [(0, 0.5 * H + 0.12 * H, -1.95)],
        "Armour": [(0, 0, 0)],
    }
    if body == "heavy":
        # Chin gun under the nose; utilities under the sponson backs; winch in the lift frame; engine stack on top.
        y_sponson = -0.30 * H - 0.18 * H - 0.03
        positions.update({
            "Nose": [(0, -0.40 * H, F - 0.6)],
            "Utility": [(W / 2 + 0.32, y_sponson, -1.9), (-(W / 2 + 0.32), y_sponson, -1.9)],
            "Winch": [(0, -H / 2 - 0.12, -0.6)],
            "Engine": [(0, H / 2 + 0.45, -1.9)],
        })
    if body == "assault":
        # Chin gun under the snout; belly turret between the side blocks; utilities on the side block backs.
        positions.update({
            "Nose": [(0, -0.46 * H, F - 0.6)],
            "Turret": [(0, -H / 2 - 0.05, -0.2)],
            "Utility": [(W / 2 - 0.05, -0.1 * H, -1.6), (-(W / 2 - 0.05), -0.1 * H, -1.6)],
            "Engine": [(0, 0.43 * H + 0.26 * H, -2.0)],
        })
    if body == "attack":
        # Chin gun under the gunner and the nose sensor; belly turret between the cheek pods; utilities on the pod
        # backs; engine stack at the back of the avionics hump.
        positions.update({
            "Nose": [(0, -0.40 * H, F - 0.75)],
            "Turret": [(0, -H / 2 - 0.05, -0.2)],
            "Utility": [(0.405 * W - 0.03, -0.2 * H, -1.2), (-(0.405 * W - 0.03), -0.2 * H, -1.2)],
            "Engine": [(0, 0.55 * H + 0.08 * H, -1.85)],
        })
    sockets, counts = [], {}
    for slot in spec["slots"]:
        index = counts.get(slot, 0)
        counts[slot] = index + 1
        options = positions[slot]
        scale = rotors[min(index, len(rotors) - 1)][1] if slot == "Rotor" else 1.0
        sockets.append((slot, index, tuple(round(v, 4) for v in options[min(index, len(options) - 1)]), scale))
    # Extra rotor hubs (tandem, coaxial, quad) beyond the game's one Rotor slot: the rotor module shows on each,
    # at the socket's scale (quad ducts are small).
    for index in range(counts.get("Rotor", 0), len(rotors)):
        point, scale = rotors[index]
        sockets.append(("Rotor", index, tuple(round(v, 4) for v in point), scale))
    return sockets


# ---------------------------------------------------------------------------------------------------------------------
# Modules (origin = attach point)
# ---------------------------------------------------------------------------------------------------------------------

def _mount(b, length=0.5):
    """Short hardpoint block under the socket that every hanging module shares (0 to 0.2 m down: the module body
    starts at -0.18 or higher, so it always cuts into the mount)."""
    b.box((0, -0.1, 0), (0.12, 0.2, length), C("gunmetal"))


def rotor(name, ids, blades, length, chord, tip):
    b = Builder()
    b.cylinder((0, 0, 0), (0, 0.3, 0), 0.24, C("steel"), segments=12)
    b.cylinder((0, 0.3, 0), (0, 0.4, 0), 0.13, C("gunmetal"), segments=10)
    for k in range(blades):
        angle = 360.0 * k / blades
        turn = rot_y(angle)
        pitch = turn @ rot_x(7)
        centre = turn @ Vector((0.22 + length / 2, 0.16, 0))
        b.box(tuple(centre), (length, 0.05, chord), C("carbon"), rotation=pitch)
        tip_centre = turn @ Vector((0.22 + length - 0.15, 0.16, 0))
        b.box(tuple(tip_centre), (0.3, 0.055, chord * 1.02), C(tip), rotation=pitch)
        grip = turn @ Vector((0.32, 0.16, 0))
        b.box(tuple(grip), (0.3, 0.12, chord * 0.8), C("gunmetal"), rotation=turn)
    return Part(name, "module", b, slot="Rotor", module_ids=ids)


def minigun():
    b = Builder()
    _mount(b, 0.4)
    y = -0.3
    b.cylinder((0, y, -0.2), (0, y, 0.45), 0.14, C("gunmetal"), segments=10)
    for k in range(6):
        t = 2 * math.pi * k / 6
        ox, oy = 0.07 * math.cos(t), 0.07 * math.sin(t)
        b.cylinder((ox, y + oy, 0.4), (ox, y + oy, 1.3), 0.024, C("steel"), segments=6)
    b.cylinder((0, y, 1.12), (0, y, 1.2), 0.12, C("gunmetal"), segments=10)
    b.box((0.2, y, -0.02), (0.16, 0.2, 0.35), C("hull_dark"))
    return Part("Gun_Minigun", "module", b, slot="Nose", module_ids=[101])


def autocannon():
    b = Builder()
    _mount(b, 0.5)
    y = -0.3
    b.box((0, y, 0.15), (0.26, 0.26, 0.8), C("gunmetal"))
    b.cylinder((0, y, 0.55), (0, y, 1.0), 0.08, C("gunmetal"), segments=10)
    b.cylinder((0, y, 0.55), (0, y, 2.0), 0.05, C("steel"), segments=10)
    b.box((0, y, 2.05), (0.15, 0.1, 0.18), C("gunmetal"))
    b.box((-0.19, y + 0.02, 0.1), (0.12, 0.18, 0.4), C("hazard"))
    return Part("Gun_Autocannon", "module", b, slot="Nose", module_ids=[102])


def pulse_laser():
    b = Builder()
    _mount(b, 0.5)
    y = -0.3
    b.box((0, y, 0.15), (0.3, 0.24, 0.7), C("hull_dark"))
    b.cylinder((0, y, 0.45), (0, y, 1.7), 0.06, C("gunmetal"), segments=10)
    for z in (0.7, 0.95, 1.2, 1.45):
        b.cylinder((0, y, z), (0, y, z + 0.05), 0.1, C("neon_cyan"), segments=10)
    b.cylinder((0, y, 1.7), (0, y, 1.78), 0.08, C("neon_cyan"), segments=10)
    return Part("Gun_PulseLaser", "module", b, slot="Nose", module_ids=[103])


def rocket_pod():
    b = Builder()
    _mount(b, 0.7)
    y = -0.48
    b.cylinder((0, y, -0.75), (0, y, 0.75), 0.3, C("hull"), segments=12, cap_start=C("hull_dark"), cap_end=C("rubber"))
    b.cylinder((0, y, 0.42), (0, y, 0.52), 0.305, C("hazard"), segments=12)
    rims = [(0.0, 0.0)] + [(0.17 * math.cos(t), 0.17 * math.sin(t)) for t in (2 * math.pi * k / 6 for k in range(6))]
    for x, oy in rims:
        b.cylinder((x, y + oy, 0.74), (x, y + oy, 0.8), 0.065, C("gunmetal"), segments=8, cap_end=C("rubber"))
    return Part("Pod_Rockets", "module", b, slot="Pylon", module_ids=[111])


def swarm_pod():
    b = Builder()
    _mount(b, 0.7)
    y = -0.38
    b.box((0, y, 0), (0.5, 0.42, 1.3), C("hull"), swatches={"front": C("rubber")})
    for row in range(3):
        for col in range(4):
            x = -0.165 + col * 0.11
            yy = y - 0.12 + row * 0.12
            b.cylinder((x, yy, 0.64), (x, yy, 0.69), 0.04, C("gunmetal"), segments=6, cap_end=C("rubber"))
    for side in (1, -1):
        b.box((side * 0.255, y + 0.1, 0), (0.02, 0.03, 1.1), C("neon_pink"))
    return Part("Pod_Swarm", "module", b, slot="Pylon", module_ids=[117])


def _missile(b, centre, length, radius, band="warhead"):
    x, y, z = centre
    tail, nose = z - length / 2, z + length / 2
    b.cylinder((x, y, tail), (x, y, nose - radius * 3), radius, C("missile"), segments=10, cap_start=C("exhaust"))
    b.cylinder((x, y, nose - radius * 3), (x, y, nose), radius, C("missile"), segments=10, end_radius=0.004)
    b.cylinder((x, y, nose - radius * 4), (x, y, nose - radius * 3), radius * 1.01, C(band), segments=10)
    for k in range(4):
        turn = rot_z(45 + 90 * k)
        offset = turn @ Vector((0, radius + 0.08, 0))
        b.box((x + offset.x, y + offset.y, tail + 0.15), (0.02, 0.16, 0.25), C("hull_dark"), rotation=turn)


def missile_rack(name, ids, count, length, radius, band="warhead"):
    b = Builder()
    _mount(b, 0.6)
    b.box((0, -0.2, 0), (0.1, 0.08, length * 0.8), C("gunmetal"))
    xs = [0.0] if count == 1 else [-(radius + 0.04), radius + 0.04]
    for x in xs:
        _missile(b, (x, -0.3 - radius, 0.1), length, radius, band)
        for z in (-0.25, 0.45):  # hanger lugs from the rail to the missile
            b.box((x * 0.5, -0.27, z), (abs(x) + 0.05, 0.08, 0.1), C("gunmetal"))
    return Part(name, "module", b, slot="Pylon", module_ids=ids)


def bomb_rack():
    b = Builder()
    _mount(b, 0.6)
    for x in (-0.16, 0.16):
        y = -0.36
        b.cylinder((x, y, -0.55), (x, y, 0.35), 0.14, C("hull_dark"), segments=10)
        b.cylinder((x, y, 0.35), (x, y, 0.6), 0.14, C("hull_dark"), segments=10, end_radius=0.05)
        b.cylinder((x, y, 0.2), (x, y, 0.28), 0.142, C("hazard"), segments=10)
        for k in range(4):
            turn = rot_z(45 + 90 * k)
            offset = turn @ Vector((0, 0.18, 0))
            b.box((x + offset.x, y + offset.y, -0.6), (0.015, 0.14, 0.2), C("gunmetal"), rotation=turn)
    b.box((0, -0.22, 0), (0.5, 0.06, 0.7), C("gunmetal"))
    return Part("Rack_Bombs", "module", b, slot="Pylon", module_ids=[113])


def dispenser(name, ids, accent, rows=2, cols=4):
    """Utility box (flares, smoke, mines): tubes on the back face, an accent light."""
    b = Builder()
    b.box((0, -0.12, 0), (0.36, 0.24, 0.5), C("hull_dark"))
    for row in range(rows):
        for col in range(cols):
            x = -0.12 + col * (0.24 / max(cols - 1, 1))
            y = -0.17 + row * 0.1
            b.box((x, y, -0.26), (0.05, 0.05, 0.03), C("rubber"))
    b.box((0, -0.01, 0.1), (0.3, 0.02, 0.06), C(accent))
    return Part(name, "module", b, slot="Utility", module_ids=ids)


def repair_drone():
    b = Builder()
    b.box((0, -0.06, 0), (0.5, 0.12, 0.5), C("hull_dark"))
    # A small quad drone parked on its dock.
    b.box((0, -0.15, 0), (0.18, 0.08, 0.18), C("hull_light"))
    for k in range(4):
        turn = rot_y(45 + 90 * k)
        arm = turn @ Vector((0.17, -0.15, 0))
        b.box(tuple(arm), (0.2, 0.03, 0.03), C("gunmetal"), rotation=turn)
        tip = (arm.x * 1.5, -0.15, arm.z * 1.5)
        b.cylinder((tip[0], -0.165, tip[2]), (tip[0], -0.125, tip[2]), 0.025, C("gunmetal"), segments=6)  # motor
        b.cylinder((tip[0], -0.13, tip[2]), (tip[0], -0.12, tip[2]), 0.08, C("carbon"), segments=8)
    b.box((0, -0.19, 0.09), (0.06, 0.03, 0.02), C("neon_green"))
    return Part("Utility_RepairDrone", "module", b, slot="Utility", module_ids=[133])


def emitter(name, ids, glow):
    """EMP coil or shield projector: a dish with glowing rings."""
    b = Builder()
    b.box((0, -0.08, 0), (0.3, 0.16, 0.3), C("hull_dark"))
    b.cylinder((0, -0.16, 0), (0, -0.3, 0), 0.22, C("gunmetal"), segments=12, end_radius=0.3)
    b.cylinder((0, -0.29, 0), (0, -0.47, 0), 0.1, C("gunmetal"), segments=10)
    for y in (-0.34, -0.42):
        b.cylinder((0, y, 0), (0, y - 0.03, 0), 0.24, C(glow), segments=12)
    b.ico((0, -0.5, 0), 0.08, C(glow), subdivisions=1)
    return Part(name, "module", b, slot="Utility", module_ids=ids)


def flak_turret():
    b = Builder()
    b.cylinder((0, 0, 0), (0, -0.18, 0), 0.36, C("gunmetal"), segments=12)
    b.ico((0, -0.45, 0), 0.32, C("hull"), subdivisions=1)
    for x in (-0.1, 0.1):
        b.cylinder((x, -0.45, 0.2), (x, -0.45, 1.2), 0.04, C("steel"), segments=8)
        b.cylinder((x, -0.45, 1.05), (x, -0.45, 1.2), 0.06, C("gunmetal"), segments=8)
    b.box((0, -0.3, 0.29), (0.1, 0.06, 0.04), C("neon_cyan"))
    return Part("Turret_Flak", "module", b, slot="Turret", module_ids=[121])


def winch():
    b = Builder()
    b.box((0, -0.15, 0), (0.6, 0.3, 0.6), C("hull_dark"))
    b.cylinder((-0.25, -0.45, 0), (0.25, -0.45, 0), 0.18, C("gunmetal"), segments=12)
    b.cylinder((0, -0.6, 0), (0, -1.4, 0), 0.015, C("steel"), segments=5)
    b.box((0, -1.45, 0), (0.18, 0.12, 0.06), C("hazard"))
    return Part("Winch_Heavy", "module", b, slot="Winch", module_ids=[141])


def engine(name, ids, nozzles, glow, radius):
    """Exhaust stack that sits on the chassis' engine socket (the deck at the back of the engines)."""
    b = Builder()
    xs = [0.0] if nozzles == 1 else [(-0.28 + 0.56 * i / (nozzles - 1)) for i in range(nozzles)]
    if nozzles > 1:
        b.box((0, -0.02, 0.1), (xs[-1] - xs[0] + radius, radius * 1.2, 0.24), C("hull_dark"))
    for x in xs:
        b.cylinder((x, 0, 0.2), (x, 0, -0.45), radius, C("gunmetal"), segments=10, end_radius=radius * 0.8,
                   cap_end=C(glow))
        b.cylinder((x, 0, -0.1), (x, 0, -0.18), radius * 1.05, C("hull_dark"), segments=10)
    return Part(name, "module", b, slot="Engine", module_ids=ids)


def all_modules():
    return [
        minigun(), autocannon(), pulse_laser(),
        rocket_pod(), missile_rack("Rack_HomingMissiles", [112], 2, 2.0, 0.09),
        missile_rack("Rack_GroundSeeker", [115], 1, 2.3, 0.13),
        missile_rack("Rack_ClusterMissile", [116], 1, 2.1, 0.16, band="hazard"),
        swarm_pod(), bomb_rack(),
        dispenser("Utility_AirMines", [114], "warhead", rows=1, cols=3),
        flak_turret(),
        dispenser("Utility_Flares", [131], "hazard"),
        dispenser("Utility_Smoke", [132], "hull_light", rows=2, cols=3),
        repair_drone(),
        emitter("Utility_EMP", [134], "neon_cyan"),
        emitter("Utility_Shield", [135], "neon_pink"),
        winch(),
        rotor("Rotor_Standard", [151], blades=4, length=5.2, chord=0.36, tip="hazard"),
        rotor("Rotor_Agile", [152], blades=5, length=4.6, chord=0.3, tip="neon_green"),
        rotor("Rotor_HeavyLift", [153], blades=3, length=7.2, chord=0.62, tip="hazard"),
        engine("Engine_Standard", [161], 2, "exhaust", 0.13),
        engine("Engine_Boost", [162], 2, "neon_pink", 0.15),
        engine("Engine_Turbine", [163], 1, "exhaust", 0.22),
    ]
