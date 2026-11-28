"""
Arcus Aerodyne chassis: the second maker's line (owner, 28 Nov 2026: "a different company in our cyberpunk world, more
sleek and high-tech than the rugged design"). One body per class. Every hull is one filleted skin (skin.py): engine
humps, rotor pylon, wing gloves and sponsons blend into the hull, glass sits flush in a frame, and the panel lines are
planned per chassis (structure stations, doors, hatches, cowlings, vents), not random. Shared signature: chined
two-tone hull, cyan light line along the chine, coaxial rotors, airfoil fins, ducted tail fans, retractable gear.

Bodies (spec "body"): "wisp" (Scout: smooth pod, wrap-round glass, hoop band, fan-in-fin, skids), "lance" (Gunship:
long chined stealth hull, flush stepped tandem glass, engines faired into the shoulders, ducted pusher), "aegis"
(Assault: broad armoured hull, engine pods merged into the shoulders, swept wings, blunt sensor nose, twin canted fins),
"halo" (Lifter: angular transport, glazed beak, sliding door, roof nacelles, sponsons, twin canted fins). Concept
references: Tools/PartForge/concepts/README.md.

The look values (form, lights) are the maker's (styles.MAKERS), laid over the kit style for these parts only: the
rugged chassis in the same kit do not change. Units and axes as parts.py (Unity space, metres, origin = centre of
mass). The hulls are drawn to their own shape (the spec's width, height, nose and boom do not reshape them).
"""
import math

from mathutils import Matrix, Vector

from . import skin
from .geo import FORM, Builder, rot_x

BODIES = ("wisp", "lance", "aegis", "halo")
SKINNED = {"wisp", "lance", "aegis", "halo"}  # bodies built as one filleted skin (skin.py)


def _parts():
    from . import parts
    return parts


def C(name):
    return _parts().C(name)


# ---------------------------------------------------------------------------------------------------------------------
# Shared pieces
# ---------------------------------------------------------------------------------------------------------------------

def _frame(axis):
    a = Vector(axis).normalized()
    helper = Vector((0, 1, 0)) if abs(a.y) < 0.9 else Vector((1, 0, 0))
    u = a.cross(helper).normalized()
    return a, u, a.cross(u).normalized()


def _shroud(b, centre, axis, section, swatch, swatches=None, segments=28):
    """A ring swept round `axis` through `centre`: `section` is a closed outline of (along the axis, radius) points
    (a duct lip, a fan shroud). swatches: one per band (band k = section[k] to section[k + 1])."""
    segments = max(segments, round(segments * FORM["curve_detail"]))
    o = Vector(centre)
    a, u, w = _frame(axis)
    rings = []
    for d, r in section:
        rings.append(b._verts([tuple(o + a * d + (u * math.cos(t) + w * math.sin(t)) * r)
                               for t in (2 * math.pi * i / segments for i in range(segments))]))
    count = len(rings)
    for k in range(count):
        ring, nxt = rings[k], rings[(k + 1) % count]
        band = swatch if swatches is None or swatches[k] is None else swatches[k]
        for i in range(segments):
            j = (i + 1) % segments
            b._face([ring[i], ring[j], nxt[j], nxt[i]], band)


def _lip(depth, r_out, r_in, round_share=0.35):
    """Duct section: a rounded lip in front, a fuller outer skin, a straight inner wall (along, radius)."""
    d = depth / 2
    t = r_out - r_in
    k = round_share * t
    return [(-d, r_in), (-d + k * 0.3, r_in + t * 0.55), (-d + k, r_out - t * 0.1), (-d + k * 2.2, r_out),
            (d - k, r_out), (d, r_out - t * 0.35), (d, r_in + t * 0.2), (d - k * 0.5, r_in)]


def _fan(b, centre, axis, radius, blades, swatch, hub=0.12, pitch=28.0, chord=0.16, phase=0.0, tip=None):
    """Fan blades round `axis` (thin twisted blades, a cone hub)."""
    o = Vector(centre)
    a, u, w = _frame(axis)
    for k in range(blades):
        t = phase + 2 * math.pi * k / blades
        radial = u * math.cos(t) + w * math.sin(t)
        tangent = -u * math.sin(t) + w * math.cos(t)
        sections = []
        for r, c, th, p in ((hub * 0.8, chord * 0.9, 0.03, pitch + 12), (radius * 0.55, chord, 0.022, pitch),
                            (radius, chord * 0.75, 0.012, pitch - 10)):
            along = tangent * math.cos(math.radians(p)) + a * math.sin(math.radians(p))
            thick = along.cross(radial).normalized()
            centre_r = o + radial * r
            sections.append([tuple(centre_r + along * c / 2), tuple(centre_r + thick * th / 2),
                             tuple(centre_r - along * c / 2), tuple(centre_r - thick * th / 2)])
        b.raw_loft(sections, swatch, swatches=[None, tip])
    b.lathe(tuple(o - a * 0.12), tuple(a), [(0.0, hub * 0.9), (0.08, hub), (0.2, hub * 0.85), (0.3, hub * 0.3),
                                            (0.32, 0.01)], C("steel"), segments=12)


def _mast(b, top, hub_y, z=0.0, radius=0.13):
    """Rotor mast out of a rounded collar."""
    b.lathe((0, top - 0.15, z), (0, 1, 0), [(0.0, radius * 2.4), (0.1, radius * 2.3), (0.2, radius * 1.7),
                                            (0.26, radius * 1.2)], C("hull_dark"), segments=14)
    b.cylinder((0, top, z), (0, hub_y, z), radius, C("steel"), segments=10)
    b.lathe((0, hub_y - 0.2, z), (0, 1, 0), [(0.0, radius * 1.05), (0.06, radius * 1.5), (0.14, radius * 1.5),
                                             (0.2, radius * 1.1)], C("gunmetal"), segments=12)


def _nav_lights(b, style, right, left):
    """Nav lights: small glowing pods at two points (right is +x)."""
    lights = style["lights"]
    for point, key in ((right, "right"), (left, "left")):
        b.ico(point, 0.045, C(lights[key]), subdivisions=1)


def _wheel_leg(b, legs, hinge, foot, radius, width, degrees, fairing=True):
    """One retractable leg: strut from the hinge down to the axle, a wheel, a small door fairing on the strut."""
    p = _parts()
    with p._Leg(b, legs, hinge, degrees=degrees):
        p._strut(b, hinge, foot, 0.05)
        p._wheel(b, foot, radius, width)
        if fairing:
            mid = (Vector(hinge) + Vector(foot)) / 2
            b.box((mid.x + (0.07 if mid.x >= 0 else -0.07) * (1 if abs(mid.x) > 0.01 else 0), mid.y, mid.z),
                  (0.03, abs(hinge[1] - foot[1]) * 0.7, radius * 1.6), C("hull_dark"))


def _make_sockets(spec, positions, rotors):
    """One socket per slot in the game's slot order, extra rotor hubs and engine pods after (parts._sockets rules)."""
    sockets, counts = [], {}
    for slot in spec["slots"]:
        index = counts.get(slot, 0)
        counts[slot] = index + 1
        options = positions[slot]
        scale = rotors[min(index, len(rotors) - 1)][1] if slot == "Rotor" else 1.0
        sockets.append((slot, index, tuple(round(v, 4) for v in options[min(index, len(options) - 1)]), scale))
    for index in range(counts.get("Rotor", 0), len(rotors)):
        point, scale = rotors[index]
        sockets.append(("Rotor", index, tuple(round(v, 4) for v in point), scale))
    engines = positions["Engine"]
    for index in range(counts.get("Engine", 0), len(engines) if "Engine" in counts else 0):
        sockets.append(("Engine", index, tuple(round(v, 4) for v in engines[index]), 1.0))
    return sockets


# ---------------------------------------------------------------------------------------------------------------------
# Wisp (Scout): smooth pod with huge wrap-round glass, a hoop band round the cabin, compact coaxial head, slim boom
# into a fan-in-fin tail, low skids
# ---------------------------------------------------------------------------------------------------------------------

def _wisp_tracks():
    return skin.tracks(dict(
        yt=[(3.1, -0.04), (2.92, 0.22), (2.55, 0.52), (2.0, 0.76), (1.3, 0.87), (0.5, 0.87), (-0.3, 0.76),
            (-1.1, 0.56), (-1.9, 0.36), (-2.8, 0.26), (-5.6, 0.24), (-6.0, 0.27), (-6.3, 0.30)],
        yb=[(3.1, -0.08), (2.9, -0.34), (2.4, -0.54), (1.6, -0.64), (0.6, -0.66), (-0.4, -0.58), (-1.2, -0.40),
            (-2.0, -0.16), (-2.8, -0.04), (-5.6, 0.04), (-6.0, 0.08), (-6.3, 0.12)],
        wt=[(3.1, 0.03), (2.8, 0.18), (2.2, 0.30), (1.3, 0.42), (0.3, 0.42), (-1.0, 0.26), (-2.0, 0.12), (-2.8, 0.09),
            (-6.3, 0.05)],
        wc=[(3.1, 0.05), (2.8, 0.36), (2.2, 0.56), (1.4, 0.66), (0.4, 0.66), (-0.6, 0.56), (-1.4, 0.38), (-2.2, 0.2),
            (-2.8, 0.15), (-5.6, 0.11), (-6.3, 0.09)],
        yc=[(3.1, -0.06), (2.4, 0.0), (1.0, 0.06), (-1.0, 0.08), (-2.2, 0.10), (-6.3, 0.2)],
        wb=[(3.1, 0.03), (2.6, 0.24), (1.6, 0.36), (0.4, 0.36), (-0.8, 0.26), (-2.0, 0.08), (-6.3, 0.04)],
        sp_top=[(0.7, 0.78), (0.3, 0.95), (-0.2, 1.0), (-0.9, 0.92), (-1.7, 0.62), (-2.3, 0.30)],
        sp_w=[(0.7, 0.12), (0.2, 0.26), (-0.9, 0.26), (-1.7, 0.14), (-2.3, 0.05)],
    ))


def _wisp_section(t, z):
    yt, yb = t["yt"](z), t["yb"](z)
    sec = skin.Section(skin.poly(skin.chined(yt, t["wt"](z), t["yc"](z), t["wc"](z), yb, t["wb"](z),
                                             r_top=0.22, r_chine=0.24, r_bottom=0.2)),
                       centre=(0.0, (yt + yb) / 2))
    if -2.3 <= z <= 0.7:
        top, w = t["sp_top"](z), t["sp_w"](z)
        if top > yt - 0.02:
            sec.union(skin.poly(skin.box(0.0, (top + yt - 0.12) / 2, w, (top - yt + 0.12) / 2, 0.08, top_w=w * 0.6)),
                      0.14)
    return sec


def _wisp(b, spec, style, legs):
    W, H, F = spec["width"], spec["height"], spec["front"]
    tail_z = -2.6 - spec["boom"]
    t = _wisp_tracks()
    hull, dark, groove, glass = C("hull"), C("hull_dark"), C("rubber"), C("glass")
    s = skin.Skin(hull).build(lambda z: _wisp_section(t, z), F - 0.03, -6.3, half_count=34, step=0.035,
                              nose=(0.0, -0.06, F + 0.02), tail=(0.0, 0.21, -6.4), tolerance=0.003, corner=0.2)

    s.paint("side", skin.sample(t["yc"], F + 0.1, -6.5, 0.08) + [(-6.5, -2.0), (F + 0.1, -2.0)], dark)
    # Huge wrap-round glass: the whole upper nose, in a thin dark frame.
    canopy = [(2.98, -0.16), (2.3, -0.30), (1.4, -0.26), (0.98, -0.05), (0.80, 0.45), (0.72, 1.3), (3.3, 1.3)]
    s.recess("side", canopy, glass, 0.01, wall=0.02, rim=dark, rim_width=0.035)
    # Hoop band round the cabin with a light line in it.
    s.band("side", [(0.50, -0.8), (0.50, 1.3)], 0.15, dark)
    s.band("side", [(0.50, -0.8), (0.50, 1.3)], 0.022, C("neon_cyan"))
    # Panel lines.
    g = dict(swatch=groove, facing=0.25)
    s.groove("side", [(2.78, -0.62), (2.95, -0.22)], **g)
    s.groove("side", [(2.25, -0.32), (2.25, -0.48), (1.05, -0.48), (1.05, -0.12)], **g)      # door sill
    s.groove("side", skin.round_rect(-0.35, -0.22, 0.45, 0.14, 0.04), closed=True, **g)  # bay
    s.groove("side", [(0.2, 0.40), (-1.5, 0.40)], swatch=groove, facing=0.25)          # cowling
    s.groove("side", [(-0.75, 0.40), (-0.75, 0.9)], swatch=groove, facing=0.25)
    s.groove("side", [(-1.95, -0.3), (-1.95, 0.6)], **g)                                # boom joint
    for z in (-3.6, -5.0):
        s.groove("side", [(z, -0.1), (z, 0.5)], swatch=groove, facing=0.2)
    s.groove("side", [(-2.1, 0.16), (-6.0, 0.22)], swatch=groove, facing=0.4)
    s.groove("top", [(0.12, 0.6), (0.12, -1.6)], swatch=groove, facing=0.5)
    s.vents("side", (-1.3, 0.62), (0.26, 0.14), 4, groove, facing=0.3, depth=(0.1, 1.0))
    s.into(b)
    b.snapshot_surface()

    # Pylon intakes and the exhaust.
    for side in (1, -1):
        b.lathe((side * 0.25, 0.84, 0.12), (side * 0.15, 0.0, 1.0),
                [(0.0, 0.085), (0.05, 0.095), (0.08, 0.09), (0.085, 0.075), (0.05, 0.065)], dark, segments=14,
                cap_end=groove)
    b.lathe((0, 0.66, -1.72), (0, 0.2, -1.0), [(0.0, 0.12), (0.1, 0.13), (0.2, 0.12), (0.22, 0.09), (0.12, 0.08)],
            C("gunmetal"), segments=16, cap_end=C("exhaust"))

    # Compact coaxial head.
    top = t["sp_top"](0.0)
    hub_low = top + 0.38
    hub_high = hub_low + 0.62
    _mast(b, top, hub_high, radius=0.11)
    b.lathe((0, hub_low + 0.12, 0), (0, 1, 0), [(0.0, 0.1), (0.1, 0.21), (0.3, 0.21), (0.4, 0.1)], hull,
            segments=16)
    b.lathe((0, hub_low - 0.05, 0), (0, 1, 0), [(0.0, 0.17), (0.1, 0.17)], C("gunmetal"), segments=12)

    # Fan-in-fin tail.
    yc, zc = 0.40, tail_z - 0.35
    _shroud(b, (0, yc, zc), (1, 0, 0), _lip(0.28, 0.66, 0.5), hull,
            swatches=[dark, None, None, None, None, None, dark, dark])
    _fan(b, (0, yc, zc), (1, 0, 0), 0.48, 9, C("carbon"), hub=0.11, chord=0.12)
    for k in range(3):
        turn = rot_x(90 + 120 * k)
        centre = Vector((0.04, yc, zc)) + turn @ Vector((0, 0.3, 0))
        b.box(tuple(centre), (0.02, 0.4, 0.05), C("gunmetal"), rotation=turn)
    _surface(b, (0, yc + 0.45, zc + 0.55), (0, yc + 1.42, zc - 0.5), 1.0, 0.45, 0.11, hull)
    b.box((0, yc + 1.41, zc - 0.72), (0.05, 0.03, 0.3), C("neon_cyan"))
    _surface(b, (0, yc - 0.5, zc + 0.25), (0, yc - 0.86, zc - 0.25), 0.6, 0.3, 0.12, dark)
    for side in (1, -1):
        _surface(b, (side * 0.05, 0.18, -4.85), (side * 0.74, 0.21, -5.1), 0.46, 0.27, 0.12, hull)
        _surface(b, (side * 0.71, 0.13, -5.0), (side * 0.76, 0.42, -5.2), 0.3, 0.2, 0.1, dark)
    _nav_lights(b, style, (0.77, 0.44, -5.3), (-0.77, 0.44, -5.3))

    # Chin sensor: a dark glass blister.
    if spec.get("sensor", False):
        b.ico((0, -0.5, F - 0.85), 0.14, glass, subdivisions=2)
        b.box((0, -0.5, F - 0.72), (0.06, 0.04, 0.02), C(style["lights"]["sensor"]))

    # One stub each side, a pylon under the right one.
    pylon_points = []
    for side in (1, -1):
        _surface(b, (side * 0.56, -0.04, 0.12), (side * 1.0, -0.08, -0.12), 0.6, 0.38, 0.13, hull)
        b.block((side * 0.80, -0.12, -0.24), (0.07, 0.08, 0.36), dark, taper=1.0, taper_depth=0.8,
                rotation=rot_x(180))
        b.box((side * 1.005, -0.08, -0.3), (0.02, 0.02, 0.22), C("neon_cyan"))
    for i in range(spec["slots"].count("Pylon")):
        side = 1 if i % 2 == 0 else -1
        pylon_points.append((side * 0.80, -0.18, -0.24))

    # Low skids that hug the belly on two faired struts each; retracted they slide up flush with the keel.
    p = _parts()
    ground_y = -H / 2 - 0.48
    radius = 0.045
    with p._Leg(b, legs, (0, -H / 2, 0), lift=0.42):
        for side in (1, -1):
            x = side * 0.46 * W
            b.tube([(x, ground_y + 0.16, -1.65), (x, ground_y + 0.02, -1.3), (x, ground_y, -0.6), (x, ground_y, 0.8),
                    (x, ground_y + 0.03, F - 1.6), (x, ground_y + 0.2, F - 1.15)], radius, dark, segments=8)
            for z in (0.55, -0.95):
                b.cylinder((side * 0.24 * W, -0.42 * H, z), (x, ground_y + 0.01, z - 0.12), 0.035, dark, segments=8)
    rotors = [((0, hub_low, 0), 1.0), ((0, hub_high, 0), 1.0)]
    positions = {
        "Nose": [(0, -0.46, F - 1.15)],
        "Pylon": pylon_points,
        "Utility": [(0.36, -0.07, -1.55), (-0.36, -0.07, -1.55)],
        "Rotor": [point for point, _s in rotors],
        "Engine": [(0, t["sp_top"](-1.15), -1.15)],
        "Armour": [(0, 0, 0)],
    }
    return rotors, positions, ground_y - radius


# ---------------------------------------------------------------------------------------------------------------------
# Airfoil surfaces (fins, stabilisers, stub wings): real sections, thick at the root, a rounded leading edge
# ---------------------------------------------------------------------------------------------------------------------

def _airfoil(count=9, blunt=0.004):
    """Symmetric airfoil loop: (share of chord from the leading edge, half thickness share, absolute half thickness
    at the trailing edge)."""
    xs = [(1 - math.cos(math.pi * i / count)) / 2 for i in range(count + 1)]

    def half(x):
        return 5 * (0.2969 * math.sqrt(x) - 0.1260 * x - 0.3516 * x * x + 0.2843 * x ** 3 - 0.1036 * x ** 4)

    upper = [(x, half(x), blunt * x) for x in xs]
    lower = [(x, -half(x), -blunt * x) for x in reversed(xs[1:])]
    return upper + lower


def _surface(b, root_le, tip_le, root_chord, tip_chord, thickness, swatch, chord_dir=(0, 0, -1), rows=4,
             tip_swatch=None):
    """An airfoil surface from the root leading edge to the tip leading edge (chords run along chord_dir; thickness
    is a share of the chord). The root end is meant to sit inside the hull or a fillet."""
    root_le, tip_le = Vector(root_le), Vector(tip_le)
    span = (tip_le - root_le).normalized()
    # Chords stay streamwise (a swept fin keeps level sections); thickness is square to span and chord.
    chord = Vector(chord_dir).normalized()
    side = span.cross(chord).normalized()
    loop = _airfoil()
    sections = []
    for i in range(rows + 1):
        t = i / rows
        le = root_le.lerp(tip_le, t)
        c = root_chord + (tip_chord - root_chord) * t
        th = thickness * c * (1.0 - 0.25 * t)
        sections.append([tuple(le + chord * (u * c) + side * (s * th + e)) for u, s, e in loop])
    b.raw_loft(sections, swatch, cap_back=swatch if tip_swatch is None else tip_swatch)


# ---------------------------------------------------------------------------------------------------------------------
# Lance (Gunship): long chined stealth hull, flush stepped tandem glass, engines faired into the shoulders
# ---------------------------------------------------------------------------------------------------------------------

def _lance_tracks():
    return skin.tracks(dict(
        # Hull: roof, its half-width, chine height and half-width (the widest line, crisp), belly and its half-width.
        yt=[(4.2, -0.2), (3.9, 0.06), (3.4, 0.40), (2.95, 0.62), (2.5, 0.70), (2.32, 0.72), (2.02, 0.90),
            (1.6, 1.0), (0.9, 1.02), (0.3, 1.0), (-0.6, 0.92), (-1.6, 0.74), (-2.6, 0.44), (-3.6, 0.32),
            (-5.5, 0.30), (-7.4, 0.30), (-7.9, 0.27)],
        wt=[(4.2, 0.02), (3.6, 0.10), (2.8, 0.19), (2.2, 0.25), (1.7, 0.37), (0.75, 0.37), (0.35, 0.22), (-1.2, 0.20),
            (-2.6, 0.14), (-4.0, 0.08), (-7.4, 0.07), (-7.9, 0.04)],
        yc=[(4.2, -0.22), (3.6, -0.16), (2.6, -0.08), (1.4, -0.02), (0.0, 0.0), (-1.4, 0.02), (-2.6, 0.12),
            (-4.0, 0.16), (-7.4, 0.18), (-7.9, 0.18)],
        wc=[(4.2, 0.03), (3.8, 0.20), (3.2, 0.40), (2.4, 0.58), (1.4, 0.70), (0.75, 0.72), (0.3, 0.62), (-1.2, 0.60),
            (-2.2, 0.42), (-3.2, 0.24), (-4.5, 0.17), (-7.4, 0.13), (-7.9, 0.08)],
        yb=[(4.2, -0.24), (3.8, -0.36), (3.0, -0.52), (2.0, -0.62), (0.5, -0.66), (-1.0, -0.62), (-2.0, -0.42),
            (-3.0, -0.12), (-4.5, 0.0), (-7.4, 0.06), (-7.9, 0.1)],
        wb=[(4.2, 0.02), (3.6, 0.14), (2.6, 0.26), (1.0, 0.34), (-0.8, 0.32), (-2.2, 0.16), (-3.4, 0.08),
            (-7.4, 0.06), (-7.9, 0.04)],
        # Rotor pylon on the spine.
        sp_top=[(0.9, 0.88), (0.5, 1.06), (0.0, 1.15), (-0.6, 1.13), (-1.4, 0.98), (-2.3, 0.68), (-3.0, 0.36)],
        sp_w=[(0.9, 0.14), (0.3, 0.30), (-1.0, 0.31), (-2.3, 0.18), (-3.0, 0.08)],
        # Engines: one hump each side, faired into the shoulder (steep front for the intake, round nozzle behind).
        ex=[(0.3, 0.47), (-1.0, 0.47), (-2.3, 0.40)],
        ey=[(0.3, 0.68), (-1.0, 0.68), (-2.3, 0.58)],
        er=[(0.30, 0.0), (0.26, 0.205), (0.18, 0.215), (-1.6, 0.215), (-2.1, 0.17), (-2.36, 0.12), (-2.46, 0.0)],
        # Wing glove (the stub wing root fillet).
        gr=[(0.4, 0.0), (0.12, 0.2), (-1.05, 0.2), (-1.45, 0.0)],
        # Dorsal ridge the fin grows out of.
        dr=[(-4.5, 0.0), (-5.3, 0.09), (-6.9, 0.13), (-7.4, 0.10), (-7.6, 0.0)],
    ))


def _lance_section(t, z):
    yt, yb = t["yt"](z), t["yb"](z)
    sec = skin.Section(skin.poly(skin.chined(yt, t["wt"](z), t["yc"](z), t["wc"](z), yb, t["wb"](z),
                                             r_top=0.07, r_chine=0.035, r_bottom=0.09)),
                       centre=(0.0, (yt + yb) / 2))
    if -3.0 <= z <= 0.9:
        top, w = t["sp_top"](z), t["sp_w"](z)
        if top > yt - 0.02:
            sec.union(skin.poly(skin.box(0.0, (top + yt - 0.12) / 2, w, (top - yt + 0.12) / 2, 0.07, top_w=w * 0.55)),
                      0.12)
    r = t["er"](z)
    if r > 0.002:
        for s in (1, -1):
            sec.union(skin.ellipse(s * t["ex"](z), t["ey"](z), r, r * 0.92), min(0.16, r * 0.8))
    g = t["gr"](z)
    if g > 0.002:
        for s in (1, -1):
            sec.union(skin.ellipse(s * 0.60, -0.06, g, min(0.10, g)), min(0.12, g * 0.7))
    d = t["dr"](z)
    if d > 0.002:
        sec.union(skin.poly(skin.box(0.0, yt + d / 2 - 0.03, 0.055, d / 2 + 0.03, 0.02, top_w=0.035)), 0.07)
    return sec


def _lance(b, spec, style, legs):
    F = spec["front"]
    t = _lance_tracks()
    hull, dark, groove, glass = C("hull"), C("hull_dark"), C("rubber"), C("glass")
    s = skin.Skin(hull).build(lambda z: _lance_section(t, z), F - 0.03, -7.9, half_count=36, step=0.035,
                              nose=(0.0, -0.21, F + 0.02), tail=(0.0, 0.19, -8.02), tolerance=0.003, corner=0.25)

    # Two-tone: dark below the chine, nose to tail.
    chine = skin.sample(t["yc"], F + 0.1, -8.1, 0.08)
    s.paint("side", chine + [(-8.1, -2.0), (F + 0.1, -2.0)], dark)
    # Glass: flush, wraps the roof, set in a dark frame; a frame bar at the step and down the middle.
    canopy = [(3.52, 0.10), (3.0, 0.25), (2.45, 0.33), (2.2, 0.40), (1.02, 0.44), (0.86, 0.50), (0.70, 1.4),
              (3.80, 1.4)]
    s.recess("side", canopy, glass, 0.012, wall=0.02, rim=dark, rim_width=0.04)
    s.band("side", [(2.33, 0.30), (2.13, 1.4)], 0.05, dark)
    s.band("top", [(0.0, 3.7), (0.0, 0.5)], 0.06, dark, facing=0.3)
    # Light line along the upper flank, just over the chine.
    s.band("side", [(z, y + 0.06) for z, y in skin.sample(t["yc"], 3.3, -2.2, 0.1)], 0.022, C("neon_cyan"),
           facing=0.3)

    # Panel lines (intentional: structure stations, doors, hatches, cowlings).
    g = dict(swatch=groove, facing=0.25)
    s.groove("side", [(3.30, -0.62), (3.47, 0.06)], **g)                               # nose cone joint
    s.groove("side", [(2.16, 0.40), (2.16, 0.10), (0.92, 0.10), (0.92, 0.44)], **g)    # pilot door
    s.groove("side", [(3.05, 0.22), (3.05, 0.06), (2.42, 0.06), (2.42, 0.30)], **g)    # gunner door
    s.groove("side", skin.round_rect(2.75, -0.30, 0.34, 0.10, 0.03), closed=True, **g)  # avionics bay
    s.groove("side", skin.round_rect(1.45, -0.30, 0.40, 0.10, 0.03), closed=True, **g)
    s.groove("side", [(0.56, -0.70), (0.56, 0.48)], **g)                               # cabin / engine bay
    s.groove("side", [(0.12, 0.50), (-1.95, 0.50)], **g)                               # cowling lower edge
    for z in (-0.55, -1.35):
        s.groove("side", [(z, 0.50), (z, 0.92)], **g)                                   # cowling doors
    s.groove("side", skin.round_rect(-0.45, -0.32, 0.55, 0.12, 0.03), closed=True, **g)  # fuel bay
    s.groove("side", [(-2.25, -0.6), (-2.25, 0.85)], **g)                              # boom joint
    for z in (-4.0, -5.6):
        s.groove("side", [(z, -0.2), (z, 0.6)], swatch=groove, facing=0.2)
    s.groove("side", [(-2.4, 0.21), (-7.3, 0.24)], swatch=groove, facing=0.4)        # boom seam
    s.groove("top", [(0.13, 0.55), (0.13, -2.1)], swatch=groove, facing=0.5)            # spine panel
    s.groove("top", [(0.0, -0.55), (0.13, -0.55)], swatch=groove, facing=0.5)
    s.vents("side", (-2.0, 0.66), (0.26, 0.15), 4, groove, facing=0.3, depth=(0.25, 1.0))
    s.vents("side", (-3.1, 0.20), (0.36, 0.10), 3, groove, facing=0.4)
    s.into(b)
    b.snapshot_surface()

    # Nose probe.
    b.cylinder((0, -0.21, F - 0.05), (0, -0.21, F + 0.45), 0.018, C("steel"), segments=8, end_radius=0.008)
    # Intakes: a lip ring set into the flat front of each hump, a dark throat and a spinner.
    for side in (1, -1):
        x, y = side * t["ex"](0.26), t["ey"](0.26)
        b.lathe((x, y, 0.25), (0, 0, 1), [(0.0, 0.2), (0.08, 0.218), (0.12, 0.212), (0.135, 0.185), (0.11, 0.165),
                                          (0.07, 0.158)], dark, segments=20, cap_end=groove)
        b.lathe((x, y, 0.32), (0, 0, 1), [(0.0, 0.075), (0.04, 0.055), (0.085, 0.0)], C("steel"), segments=12)
    # Exhaust nozzles at the back of the humps.
    for side in (1, -1):
        b.lathe((side * 0.39, 0.58, -2.24), (side * 0.12, 0.04, -1.0),
                [(0.0, 0.135), (0.12, 0.15), (0.24, 0.14), (0.28, 0.115), (0.28, 0.085), (0.14, 0.075)],
                C("gunmetal"), segments=16, cap_end=C("exhaust"))

    # Coaxial rotors on the pylon.
    spine_top = t["sp_top"](0.0)
    hub_low = spine_top + 0.45
    hub_high = hub_low + 0.8
    _mast(b, spine_top, hub_high)
    b.lathe((0, hub_low + 0.15, 0), (0, 1, 0), [(0.0, 0.12), (0.12, 0.26), (0.38, 0.26), (0.5, 0.12)], C("hull"),
            segments=16)
    b.lathe((0, hub_low - 0.06, 0), (0, 1, 0), [(0.0, 0.2), (0.12, 0.2)], C("gunmetal"), segments=12)

    # Tail: swept fin out of the dorsal ridge, ventral fin, stabilisers with canted tips, ducted pusher.
    _surface(b, (0, 0.38, -5.45), (0, 1.78, -6.85), 1.55, 0.62, 0.11, hull)
    b.box((0, 1.775, -7.12), (0.05, 0.03, 0.4), C("neon_cyan"))
    _surface(b, (0, 0.12, -6.15), (0, -0.48, -6.7), 0.85, 0.38, 0.12, dark)
    for side in (1, -1):
        _surface(b, (side * 0.06, 0.22, -5.85), (side * 0.98, 0.25, -6.25), 0.66, 0.36, 0.12, hull)
        _surface(b, (side * 0.95, 0.12, -6.15), (side * 1.02, 0.52, -6.42), 0.42, 0.26, 0.10, dark)
    pz, ty = -7.66, 0.19
    _shroud(b, (0, ty, pz), (0, 0, 1), _lip(0.34, 0.52, 0.40), hull,
            swatches=[dark, None, None, None, None, None, dark, dark])
    _fan(b, (0, ty, pz), (0, 0, -1), 0.39, 7, C("carbon"), hub=0.1, chord=0.11)
    for k in range(4):
        turn = Matrix.Rotation(math.radians(45 + 90 * k), 3, "Z")
        centre = Vector((0, ty, pz + 0.06)) + turn @ Vector((0, 0.26, 0))
        b.box(tuple(centre), (0.03, 0.30, 0.04), C("gunmetal"), rotation=turn)
    _nav_lights(b, style, (1.02, 0.54, -6.55), (-1.02, 0.54, -6.55))

    # Stub wings out of the gloves, a pylon pod under each.
    span = spec.get("wing_span", 1.25)
    root, tip = 0.66, 0.66 + span
    share = 0.62
    zle = 0.08 + (-0.42 - 0.08) * share
    chord = 1.05 + (0.52 - 1.05) * share
    for side in (1, -1):
        _surface(b, (side * root, -0.05, 0.08), (side * tip, -0.13, -0.42), 1.05, 0.52, 0.13, hull)
        x = root + span * share
        b.block((side * x, -0.19, zle - chord * 0.45), (0.08, 0.09, chord * 0.75), dark, taper=1.0,
                taper_depth=0.8, rotation=rot_x(180))
        b.box((side * (tip + 0.005), -0.13, -0.42 - 0.2), (0.025, 0.025, 0.3), C("neon_cyan"))
    pylon_points = []
    for i in range(spec["slots"].count("Pylon")):
        side = 1 if i % 2 == 0 else -1
        pylon_points.append((side * (root + span * share), -0.26, zle - chord * 0.45))

    # Gear: two mains fold back into the belly, a tail wheel folds forward.
    H = spec["height"]
    ground = -H / 2 - 0.72
    for side in (1, -1):
        hinge = (side * 0.28, -0.56, 1.9)
        _wheel_leg(b, legs, hinge, (side * 0.40, ground + 0.27, 1.75), 0.27, 0.16, 90.0)
    z = -5.2
    belly = b.hull_y(0, z, top=False, default=0.0) + 0.03
    _wheel_leg(b, legs, (0, belly, z), (0, ground + 0.16, z - 0.15), 0.16, 0.11, -90.0, fairing=False)

    rotors = [((0, hub_low, 0), 1.0), ((0, hub_high, 0), 1.0)]
    positions = {
        "Nose": [(0, -0.56, F - 1.25)],
        "Pylon": pylon_points,
        "Turret": [(0, -H / 2 - 0.05, -0.2)],
        "Utility": [(0.50, -0.30, -1.5), (-0.50, -0.30, -1.5)],
        "Rotor": [point for point, _s in rotors],
        "Engine": [(0, t["sp_top"](-1.3), -1.3)],
        "Armour": [(0, 0, 0)],
    }
    return rotors, positions, ground


# ---------------------------------------------------------------------------------------------------------------------
# Aegis (Assault): broad armoured hull, engine pods merged into the shoulders, swept wings, framed armoured glass,
# twin canted fins over a ducted pusher
# ---------------------------------------------------------------------------------------------------------------------

def _aegis_tracks():
    return skin.tracks(dict(
        # Four facets a side: roof, upper flank down to the shoulder chine, a near-vertical side down to the lower
        # chine, the lower flank in to the belly. Broad and blunt: the assault hull.
        yt=[(4.0, 0.06), (3.75, 0.24), (3.35, 0.50), (2.7, 0.98), (2.3, 1.10), (1.6, 1.16), (0.6, 1.16), (-0.2, 1.12),
            (-1.2, 1.02), (-2.2, 0.84), (-3.0, 0.62), (-4.0, 0.54), (-6.4, 0.50), (-7.0, 0.48)],
        rw=[(4.0, 0.10), (3.4, 0.20), (2.6, 0.30), (1.7, 0.40), (0.8, 0.40), (0.3, 0.28), (-1.0, 0.26), (-2.4, 0.2),
            (-3.4, 0.16), (-7.0, 0.13)],
        sy=[(4.0, -0.04), (3.4, 0.08), (2.4, 0.22), (1.0, 0.30), (-1.0, 0.32), (-2.4, 0.34), (-3.4, 0.34),
            (-7.0, 0.33)],
        sw=[(4.0, 0.16), (3.6, 0.40), (2.8, 0.70), (2.0, 0.82), (1.35, 0.78), (0.95, 0.78), (0.45, 0.98), (-0.8, 1.0),
            (-2.0, 0.74),
            (-3.0, 0.36),
            (-4.0, 0.26), (-7.0, 0.22)],
        ly=[(4.0, -0.26), (3.4, -0.34), (2.4, -0.42), (1.0, -0.48), (-1.2, -0.46), (-2.4, -0.22), (-3.4, 0.12),
            (-7.0, 0.16)],
        lw=[(4.0, 0.14), (3.6, 0.36), (2.6, 0.66), (1.6, 0.80), (1.0, 0.80), (0.4, 0.94), (-1.0, 0.92), (-2.2, 0.62),
            (-3.2, 0.28),
            (-4.0, 0.22), (-7.0, 0.18)],
        yb=[(4.0, -0.34), (3.5, -0.56), (2.6, -0.76), (1.4, -0.86), (0.0, -0.88), (-1.2, -0.82), (-2.4, -0.48),
            (-3.4, 0.0), (-4.4, 0.08), (-7.0, 0.10)],
        bw=[(4.0, 0.08), (3.4, 0.26), (2.4, 0.44), (1.0, 0.54), (-1.0, 0.52), (-2.2, 0.34), (-3.4, 0.12),
            (-7.0, 0.10)],
        # Rotor pylon.
        sp_top=[(1.0, 1.0), (0.5, 1.24), (0.0, 1.33), (-0.7, 1.30), (-1.6, 1.12), (-2.5, 0.80), (-3.2, 0.48)],
        sp_w=[(1.0, 0.16), (0.4, 0.30), (-1.2, 0.30), (-2.4, 0.18), (-3.2, 0.08)],
        # Engine pods on the shoulders.
        ex=[(0.9, 0.98), (-1.0, 0.98), (-2.3, 0.88)],
        ey=[(0.9, 0.50), (-1.0, 0.50), (-2.3, 0.48)],
        er=[(1.0, 0.0), (0.93, 0.31), (0.85, 0.33), (-1.6, 0.33), (-2.1, 0.27), (-2.38, 0.19), (-2.48, 0.0)],
        # Wing glove.
        gr=[(0.45, 0.0), (0.2, 0.24), (-1.15, 0.24), (-1.55, 0.0)],
    ))


def _aegis_section(t, z):
    yt, yb = t["yt"](z), t["yb"](z)
    half = [(t["rw"](z), yt), (t["sw"](z), t["sy"](z)), (t["lw"](z), t["ly"](z)), (t["bw"](z), yb)]
    sec = skin.Section(skin.poly(skin.faceted(half, [0.08, 0.04, 0.05, 0.10])), centre=(0.0, (yt + yb) / 2))
    if -3.2 <= z <= 1.0:
        top, w = t["sp_top"](z), t["sp_w"](z)
        if top > yt - 0.02:
            sec.union(skin.poly(skin.box(0.0, (top + yt - 0.14) / 2, w, (top - yt + 0.14) / 2, 0.08, top_w=w * 0.6)),
                      0.14)
    r = t["er"](z)
    if r > 0.002:
        for s in (1, -1):
            sec.union(skin.ellipse(s * t["ex"](z), t["ey"](z), r, r * 0.93), min(0.30, r * 0.9))
    g = t["gr"](z)
    if g > 0.002:
        for s in (1, -1):
            sec.union(skin.ellipse(s * 1.0, 0.22, g, min(0.10, g)), min(0.14, g * 0.7))
    return sec


def _aegis(b, spec, style, legs):
    F, H = spec["front"], spec["height"]
    t = _aegis_tracks()
    hull, dark, groove, glass = C("hull"), C("hull_dark"), C("rubber"), C("glass")
    s = skin.Skin(hull).build(lambda z: _aegis_section(t, z), F - 0.03, -7.0, half_count=38, step=0.035,
                              nose=(0.0, -0.12, F + 0.07), tail=(0.0, 0.3, -7.12), tolerance=0.003, corner=0.22)

    # Two-tone: dark below the lower chine.
    low = skin.sample(t["ly"], F + 0.1, -7.2, 0.08)
    s.paint("side", low + [(-7.2, -2.0), (F + 0.1, -2.0)], dark)
    # Armoured glass: a smaller canopy in a thick frame, windscreen split from the side windows, a roof bar.
    canopy = [(3.05, 0.42), (2.4, 0.60), (1.05, 0.66), (0.92, 0.72), (0.80, 1.7), (3.32, 1.7)]
    s.recess("side", canopy, glass, 0.01, wall=0.02, rim=dark, rim_width=0.05)
    s.band("side", [(2.42, 0.55), (2.22, 1.7)], 0.07, dark)
    s.band("top", [(0.0, 3.4), (0.0, 0.7)], 0.08, dark, facing=0.3)
    # Light line along the shoulder chine.
    s.band("side", [(z, y - 0.07) for z, y in skin.sample(t["sy"], 3.1, -2.1, 0.1)], 0.024, C("neon_cyan"),
           facing=0.3)

    # Armour plates and structure.
    g = dict(swatch=groove, facing=0.25)
    s.groove("side", [(3.15, -0.62), (3.32, 0.30)], **g)                               # nose cone joint
    s.groove("side", skin.round_rect(1.85, -0.04, 0.80, 0.16, 0.04), closed=True, **g)  # side armour plate
    s.groove("side", skin.round_rect(1.85, -0.30, 0.80, 0.06, 0.03), closed=True, **g)
    s.groove("side", [(0.75, -0.80), (0.75, 0.66)], **g)                                # cabin rear
    s.groove("side", [(0.62, 0.30), (-1.9, 0.30)], swatch=groove, facing=0.25)         # shoulder seam
    for z in (-0.2, -1.2):
        s.groove("side", [(z, 0.34), (z, 0.82)], swatch=groove, facing=0.25, depth=(0.55, 2.0))  # pod cowlings
    s.groove("side", skin.round_rect(-0.55, -0.12, 0.7, 0.18, 0.04), closed=True, **g)  # bay door
    s.groove("side", [(-2.4, -0.5), (-2.4, 1.0)], **g)                                  # boom joint
    for z in (-4.2, -5.4):
        s.groove("side", [(z, 0.0), (z, 0.6)], swatch=groove, facing=0.2)
    s.groove("side", [(-2.6, 0.30), (-6.6, 0.29)], swatch=groove, facing=0.4)
    s.groove("top", [(0.16, 1.0), (0.16, -2.4)], swatch=groove, facing=0.5)
    s.groove("top", [(0.0, -0.7), (0.16, -0.7)], swatch=groove, facing=0.5)
    s.groove("top", [(0.0, -1.7), (0.16, -1.7)], swatch=groove, facing=0.5)
    s.vents("side", (-1.85, 0.60), (0.34, 0.2), 5, groove, facing=0.3, depth=(0.6, 2.0))
    s.vents("side", (-0.55, 0.30), (0.5, 0.08), 2, groove, facing=0.3, depth=(0.0, 0.9))
    s.vents("top", (0.08, 2.0), (0.10, 0.32), 4, groove, angle=90.0, facing=0.5)
    s.into(b)
    b.snapshot_surface()

    # Chin sensor turret: a dark collar and a glass ball.
    b.lathe((0, -0.42, F - 0.42), (0, -1, 0.35), [(0.0, 0.2), (0.08, 0.2), (0.1, 0.17)], dark, segments=18)
    b.ico((0, -0.56, F - 0.38), 0.15, glass, subdivisions=2)
    b.lathe((0, -0.56, F - 0.38), (0, -0.2, 1), [(0.0, 0.155), (0.04, 0.155)], dark, segments=18)
    # Intakes and nozzles of the shoulder pods.
    for side in (1, -1):
        x, y = side * t["ex"](0.9), t["ey"](0.9)
        b.lathe((x, y, 0.97), (0, 0, 1), [(0.0, 0.32), (0.1, 0.34), (0.15, 0.33), (0.165, 0.29), (0.13, 0.26),
                                          (0.08, 0.25)], dark, segments=22, cap_end=groove)
        b.lathe((x, y, 1.05), (0, 0, 1), [(0.0, 0.1), (0.05, 0.075), (0.11, 0.0)], C("steel"), segments=12)
        b.lathe((side * 0.90, 0.49, -2.4), (side * 0.1, 0.03, -1.0),
                [(0.0, 0.2), (0.14, 0.215), (0.28, 0.2), (0.32, 0.165), (0.32, 0.12), (0.16, 0.11)],
                C("gunmetal"), segments=18, cap_end=C("exhaust"))

    # Coaxial rotors.
    spine_top = t["sp_top"](0.0)
    hub_low = spine_top + 0.5
    hub_high = hub_low + 0.85
    _mast(b, spine_top, hub_high, radius=0.15)
    b.lathe((0, hub_low + 0.15, 0), (0, 1, 0), [(0.0, 0.14), (0.13, 0.3), (0.42, 0.3), (0.55, 0.14)], hull,
            segments=16)
    b.lathe((0, hub_low - 0.07, 0), (0, 1, 0), [(0.0, 0.23), (0.13, 0.23)], C("gunmetal"), segments=12)

    # Broad swept wings out of the gloves, canted tips down, a pylon under each.
    span = spec.get("wing_span", 1.6)
    root, tip = 1.02, 1.02 + span
    share = 0.55
    for side in (1, -1):
        _surface(b, (side * root, 0.22, 0.25), (side * tip, 0.10, -0.75), 1.45, 0.62, 0.12, hull)
        _surface(b, (side * (tip - 0.04), 0.12, -0.70), (side * (tip + 0.16), -0.28, -0.95), 0.62, 0.34, 0.1, dark)
        b.box((side * (tip + 0.17), -0.29, -1.12), (0.03, 0.03, 0.3), C("neon_cyan"))
    zle = 0.25 + (-0.75 - 0.25) * share
    chord = 1.45 + (0.62 - 1.45) * share
    for side in (1, -1):
        b.block((side * (root + span * share), 0.10, zle - chord * 0.45), (0.10, 0.10, chord * 0.75), dark,
                taper=1.0, taper_depth=0.8, rotation=rot_x(180))
    pylon_points = []
    for i in range(spec["slots"].count("Pylon")):
        side = 1 if i % 2 == 0 else -1
        pylon_points.append((side * (root + span * share), 0.0, zle - chord * 0.45))

    # Tail: stabiliser with twin canted fins at its tips, ventral fin, ducted pusher.
    for side in (1, -1):
        _surface(b, (side * 0.08, 0.36, -5.55), (side * 1.10, 0.40, -5.95), 0.80, 0.48, 0.12, hull)
        _surface(b, (side * 1.02, 0.30, -5.75), (side * 1.30, 1.22, -6.30), 0.70, 0.38, 0.11, hull)
        b.box((side * 1.30, 1.21, -6.48), (0.04, 0.03, 0.3), C("neon_cyan"))
    _surface(b, (0, 0.16, -6.0), (0, -0.42, -6.45), 0.8, 0.4, 0.12, dark)
    pz, ty = -7.05, 0.27
    _shroud(b, (0, ty, pz), (0, 0, 1), _lip(0.40, 0.60, 0.47), hull,
            swatches=[dark, None, None, None, None, None, dark, dark])
    _fan(b, (0, ty, pz), (0, 0, -1), 0.46, 8, C("carbon"), hub=0.11, chord=0.12)
    for k in range(4):
        turn = Matrix.Rotation(math.radians(45 + 90 * k), 3, "Z")
        centre = Vector((0, ty, pz + 0.07)) + turn @ Vector((0, 0.30, 0))
        b.box(tuple(centre), (0.035, 0.36, 0.045), C("gunmetal"), rotation=turn)
    _nav_lights(b, style, (tip + 0.15, -0.27, -1.08), (-tip - 0.15, -0.27, -1.08))

    # Gear: mains fold back under the shoulders, a nose wheel folds back.
    ground = -H / 2 - 0.75
    for side in (1, -1):
        hinge = (side * 0.55, -0.62, -0.2)
        _wheel_leg(b, legs, hinge, (side * 0.70, ground + 0.3, -0.35), 0.3, 0.2, 90.0)
    _wheel_leg(b, legs, (0, -0.66, 2.55), (0, ground + 0.22, 2.43), 0.22, 0.14, -90.0, fairing=False)

    rotors = [((0, hub_low, 0), 1.0), ((0, hub_high, 0), 1.0)]
    positions = {
        "Nose": [(0, -0.84, F - 0.75)],
        "Pylon": pylon_points,
        "Turret": [(0, -H / 2 - 0.05, -1.4)],
        "Utility": [(0.62, -0.5, -1.9), (-0.62, -0.5, -1.9)],
        "Rotor": [point for point, _s in rotors],
        "Engine": [(0, t["sp_top"](-1.4), -1.4)],
        "Armour": [(0, 0, 0)],
    }
    return rotors, positions, ground


# ---------------------------------------------------------------------------------------------------------------------
# Halo (Lifter): angular attack transport (concept refs 09, 10): a faceted cabin with a big sliding door, a pointed nose
# under wrap-round glass, engine nacelles merged into the roof shoulders, a delta flange along the lower chine (pylon,
# gear and utility points under it), coaxial head, a thick boom to twin canted fins and a large ducted pusher
# ---------------------------------------------------------------------------------------------------------------------

def _halo_tracks():
    return skin.tracks(dict(
        # Long pointed nose like the Lance's: the chines run to a point, the glass slopes up from it.
        yt=[(4.6, -0.30), (4.3, -0.04), (3.9, 0.36), (3.3, 0.84), (2.7, 1.02), (2.05, 1.06), (1.55, 1.2), (0.0, 1.20),
            (-1.6, 1.15), (-2.6, 0.98), (-3.4, 0.70), (-4.4, 0.56), (-6.2, 0.52), (-6.8, 0.50)],
        rw=[(4.6, 0.02), (4.0, 0.18), (3.2, 0.44), (2.4, 0.54), (0.0, 0.56), (-1.6, 0.52), (-2.8, 0.32), (-3.6, 0.18),
            (-6.8, 0.14)],
        sy=[(4.6, -0.32), (4.0, -0.16), (3.2, 0.12), (2.4, 0.38), (0.0, 0.52), (-1.6, 0.52), (-2.8, 0.48),
            (-3.6, 0.44), (-6.8, 0.40)],
        sw=[(4.6, 0.03), (4.2, 0.28), (3.6, 0.62), (2.8, 1.0), (2.0, 1.20), (1.6, 1.24), (-1.2, 1.24), (-2.2, 1.06),
            (-3.0, 0.60), (-3.8, 0.30), (-6.8, 0.22)],
        ly=[(4.6, -0.34), (4.0, -0.48), (3.2, -0.64), (2.2, -0.74), (-1.6, -0.74), (-2.6, -0.40), (-3.4, 0.0),
            (-4.0, 0.16), (-6.8, 0.20)],
        lw=[(4.6, 0.03), (4.2, 0.26), (3.6, 0.58), (2.8, 0.96), (2.0, 1.14), (1.6, 1.18), (-1.2, 1.18), (-2.2, 1.0),
            (-3.0, 0.56), (-3.8, 0.26), (-6.8, 0.18)],
        yb=[(4.6, -0.36), (4.1, -0.64), (3.4, -0.92), (2.6, -1.10), (1.6, -1.20), (-1.4, -1.20), (-2.4, -0.95),
            (-3.2, -0.40), (-4.0, 0.06), (-6.8, 0.14)],
        bw=[(4.6, 0.02), (4.0, 0.18), (3.2, 0.44), (2.2, 0.70), (-1.4, 0.74), (-2.4, 0.56), (-3.2, 0.24), (-4.0, 0.10),
            (-6.8, 0.08)],
        sp_top=[(1.6, 1.2), (1.0, 1.45), (0.2, 1.56), (-0.6, 1.54), (-1.6, 1.36), (-2.6, 1.0), (-3.2, 0.72)],
        sp_w=[(1.6, 0.14), (0.8, 0.36), (-1.0, 0.36), (-2.6, 0.2), (-3.2, 0.1)],
        er=[(1.9, 0.0), (1.8, 0.35), (1.6, 0.37), (-1.3, 0.37), (-1.9, 0.32), (-2.35, 0.22), (-2.45, 0.0)],
        # Delta: how far the lower chine flange reaches past the hull side (owner, 28 Nov: "expand out into a delta
        # wing"): grows from under the nose, widest at the rear, ends in a steep trailing edge.
        dw=[(4.35, 0.0), (3.6, 0.12), (2.6, 0.28), (1.4, 0.50), (-0.4, 0.76), (-1.55, 0.90), (-1.95, 0.70),
            (-2.15, 0.0)],
    ))


ENGINE_X, ENGINE_Y = 0.78, 1.26


def _halo_delta(t, z):
    """Half-width of the delta's tip line at z (the hull's lower chine when there is no delta)."""
    return t["lw"](z) + max(0.0, t["dw"](z))


def _halo_section(t, z):
    yt, yb, ly = t["yt"](z), t["yb"](z), t["ly"](z)
    half = [(t["rw"](z), yt), (t["sw"](z), t["sy"](z)), (t["lw"](z), ly), (t["bw"](z), yb)]
    # Rays start on the lower chine line: the hull and the thin delta are both star-shaped round it.
    sec = skin.Section(skin.poly(skin.faceted(half, [0.10, 0.05, 0.06, 0.12])), centre=(0.0, ly))
    if -3.2 <= z <= 1.6:
        top, w = t["sp_top"](z), t["sp_w"](z)
        if top > yt - 0.02:
            sec.union(skin.poly(skin.box(0.0, (top + yt - 0.14) / 2, w, (top - yt + 0.14) / 2, 0.09, top_w=w * 0.6)),
                      0.16)
    r = t["er"](z)
    if r > 0.002:
        for s in (1, -1):
            sec.union(skin.ellipse(s * ENGINE_X, ENGINE_Y, r, r * 0.92), min(0.30, r * 0.8))
    d = t["dw"](z)
    if d > 0.004:
        tip = t["lw"](z) + d
        # A thin flat wedge through the chine: thick at the hull, a blunt rounded edge 6 cm deep at the tip (a sharp
        # tip would lose its length to the fillet).
        sec.union(skin.poly(skin.rounded([(0.0, ly + 0.12), (tip, ly), (tip, ly - 0.06), (0.0, ly - 0.16),
                                          (-tip, ly - 0.06), (-tip, ly)],
                                         [0.0, 0.025, 0.025, 0.0, 0.025, 0.025], arc=3)), min(0.16, d * 0.8))
    return sec


def _halo(b, spec, style, legs):
    F, H = spec["front"], spec["height"]
    t = _halo_tracks()
    hull, dark, groove, glass = C("hull"), C("hull_dark"), C("rubber"), C("glass")
    s = skin.Skin(hull).build(lambda z: _halo_section(t, z), F - 0.03, -6.8, half_count=40, step=0.035,
                              nose=(0.0, -0.33, F + 0.03), tail=(0.0, 0.33, -6.92), tolerance=0.003, corner=0.22)

    # Two-tone: dark below the line through the delta's edge (its top keeps the hull colour).
    s.paint("side", [(z, y - 0.03) for z, y in skin.sample(t["ly"], F + 0.1, -7.0, 0.08)]
            + [(-7.0, -3.0), (F + 0.1, -3.0)], dark)
    # Cockpit glass sloping up from the pointed nose (as the Lance), split by frame bars.
    canopy = [(4.02, -0.24), (3.3, -0.02), (2.2, 0.14), (1.98, 1.7), (4.4, 1.7)]
    s.recess("side", canopy, glass, 0.012, wall=0.02, rim=dark, rim_width=0.045)
    s.band("side", [(3.0, 0.0), (2.8, 1.7)], 0.07, dark)
    s.band("top", [(0.0, 4.2), (0.0, 2.0)], 0.08, dark, facing=0.3)
    # A light line along the delta's edge.
    s.band("top", [(_halo_delta(t, z) - 0.07, z) for z in [3.6 - 0.1 * i for i in range(55)]], 0.026,
           C("neon_cyan"), facing=0.3, depth=(-1.0, -0.45))
    # Big sliding door with two windows, its rail, cabin windows further back.
    door = skin.round_rect(0.55, -0.12, 0.85, 0.55, 0.06)
    s.groove("side", door, closed=True, width=0.02, swatch=groove, facing=0.3)
    for zc in (0.98, 0.12):
        s.recess("side", skin.round_rect(zc, 0.18, 0.28, 0.17, 0.05), glass, 0.01, wall=0.015, rim=dark,
                 rim_width=0.03, facing=0.5)
    s.groove("side", [(1.55, 0.52), (-1.45, 0.52)], width=0.03, swatch=dark, facing=0.3)
    s.recess("side", skin.round_rect(-1.05, 0.18, 0.2, 0.17, 0.05), glass, 0.01, wall=0.015, rim=dark,
             rim_width=0.03, facing=0.5)
    # Light line along the shoulder chine.
    s.band("side", [(z, y - 0.08) for z, y in skin.sample(t["sy"], 4.0, -2.6, 0.1)], 0.026, C("neon_cyan"),
           facing=0.3)
    # Hazard band round the boom.
    s.band("side", [(-4.3, -1.0), (-4.3, 1.5)], 0.2, C("hazard"))

    # Panel lines.
    g = dict(swatch=groove, facing=0.25)
    s.groove("side", [(4.06, -0.64), (4.18, -0.30)], **g)                               # nose cone
    s.groove("side", [(2.1, -0.70), (2.1, 0.12)], **g)                                  # cockpit / cabin
    s.groove("side", [(-1.55, -1.15), (-1.55, 0.95)], **g)                              # cabin rear
    s.groove("side", skin.round_rect(1.8, -0.36, 0.2, 0.24, 0.04), closed=True, **g)    # cockpit hatch
    s.groove("side", skin.round_rect(-2.1, -0.15, 0.35, 0.26, 0.04), closed=True, **g)  # avionics bay
    for z in (0.9, -0.3, -1.3):
        s.groove("side", [(z, 0.98), (z, 1.72)], swatch=groove, facing=0.25, depth=(0.7, 2.0))  # nacelle cowls
    s.groove("side", [(1.6, 0.98), (-2.1, 0.98)], swatch=groove, facing=0.25, depth=(0.7, 2.0))
    # Delta: a spar line and two access panels on its top.
    s.groove("top", [(_halo_delta(t, z) - 0.32, z) for z in [2.2 - 0.1 * i for i in range(38)]],
             swatch=groove, facing=0.4, depth=(-1.0, -0.45))
    for z in (0.6, -0.9):
        s.groove("top", [(1.25, z), (_halo_delta(t, z) - 0.07, z - 0.12)], swatch=groove, facing=0.4,
                 depth=(-1.0, -0.45))
    s.groove("side", [(-3.3, -0.3), (-3.3, 1.0)], **g)                                  # boom joint
    for z in (-5.0, -5.9):
        s.groove("side", [(z, 0.0), (z, 0.7)], swatch=groove, facing=0.2)
    s.groove("side", [(-3.4, 0.40), (-6.6, 0.40)], swatch=groove, facing=0.4)
    s.groove("top", [(0.2, 1.7), (0.2, -2.6)], swatch=groove, facing=0.5)
    for z in (0.7, -1.0):
        s.groove("top", [(0.0, z), (0.2, z)], swatch=groove, facing=0.5)
    s.groove("top", [(0.0, -1.7), (0.52, -1.7), (0.52, -3.1), (0.0, -3.1)], swatch=groove, facing=-0.4)  # ramp
    s.vents("side", (-1.9, 1.28), (0.36, 0.22), 5, groove, facing=0.3, depth=(0.7, 2.0))
    s.vents("side", (-2.7, 0.62), (0.3, 0.12), 3, groove, facing=0.3)
    s.into(b)
    b.snapshot_surface()

    # Nose probe.
    b.cylinder((0, -0.33, F - 0.05), (0, -0.33, F + 0.5), 0.022, C("steel"), segments=8, end_radius=0.009)
    # Intakes and nozzles of the nacelles.
    for side in (1, -1):
        x = side * ENGINE_X
        b.lathe((x, ENGINE_Y, 1.86), (0, 0, 1), [(0.0, 0.36), (0.12, 0.39), (0.18, 0.38), (0.2, 0.33), (0.16, 0.29),
                                                 (0.1, 0.28)], dark, segments=24, cap_end=groove)
        b.lathe((x, ENGINE_Y, 1.95), (0, 0, 1), [(0.0, 0.13), (0.07, 0.1), (0.15, 0.0)], C("steel"), segments=14)
        b.lathe((side * 0.80, 1.23, -2.4), (side * 0.15, 0.06, -1.0),
                [(0.0, 0.25), (0.16, 0.27), (0.32, 0.25), (0.36, 0.2), (0.36, 0.15), (0.2, 0.14)],
                C("gunmetal"), segments=20, cap_end=C("exhaust"))

    # Coaxial head.
    top = t["sp_top"](0.0)
    hub_low = top + 0.6
    hub_high = hub_low + 1.0
    _mast(b, top, hub_high, radius=0.17)
    b.lathe((0, hub_low + 0.18, 0), (0, 1, 0), [(0.0, 0.16), (0.14, 0.34), (0.5, 0.34), (0.64, 0.16)], hull,
            segments=18)
    b.lathe((0, hub_low - 0.08, 0), (0, 1, 0), [(0.0, 0.26), (0.16, 0.26)], C("gunmetal"), segments=14)

    # A pylon under each side of the delta.
    pylon_points = []
    pz_pylon = -0.9
    px = _halo_delta(t, pz_pylon) - 0.42
    for side in (1, -1):
        b.block((side * px, -0.86, pz_pylon), (0.09, 0.1, 0.5), dark, taper=1.0, taper_depth=0.8, rotation=rot_x(180))
    for i in range(spec["slots"].count("Pylon")):
        side = 1 if i % 2 == 0 else -1
        pylon_points.append((side * px, -0.96, pz_pylon))

    # Tail: stabiliser with twin canted fins, ventral fin, large ducted pusher.
    for side in (1, -1):
        _surface(b, (side * 0.1, 0.40, -5.45), (side * 1.38, 0.44, -5.85), 0.95, 0.56, 0.12, hull)
        _surface(b, (side * 1.28, 0.32, -5.62), (side * 1.58, 1.62, -6.3), 0.85, 0.48, 0.11, hull)
        b.box((side * 1.58, 1.61, -6.52), (0.045, 0.035, 0.36), C("hazard"))
    _surface(b, (0, 0.14, -5.6), (0, -0.62, -5.95), 0.8, 0.4, 0.12, dark)
    pz, ty = -6.92, 0.34
    _shroud(b, (0, ty, pz), (0, 0, 1), _lip(0.46, 0.80, 0.64), hull,
            swatches=[dark, None, None, None, None, None, dark, dark])
    _fan(b, (0, ty, pz), (0, 0, -1), 0.62, 9, C("carbon"), hub=0.14, chord=0.15)
    for k in range(4):
        turn = Matrix.Rotation(math.radians(45 + 90 * k), 3, "Z")
        centre = Vector((0, ty, pz + 0.1)) + turn @ Vector((0, 0.40, 0))
        b.box(tuple(centre), (0.04, 0.5, 0.05), C("gunmetal"), rotation=turn)
    tip_z = -1.6
    tip_x = _halo_delta(t, tip_z) - 0.03
    _nav_lights(b, style, (tip_x, t["ly"](tip_z) - 0.03, tip_z), (-tip_x, t["ly"](tip_z) - 0.03, tip_z))

    # Gear: mains fold up into the sponsons, a nose wheel folds back.
    ground = -H / 2 - 0.7
    for side in (1, -1):
        hinge = (side * 1.15, -0.80, -0.3)
        _wheel_leg(b, legs, hinge, (side * 1.15, ground + 0.36, -0.45), 0.36, 0.2, 90.0)
    _wheel_leg(b, legs, (0, -0.95, 2.85), (0, ground + 0.28, 2.75), 0.28, 0.16, -90.0, fairing=False)

    rotors = [((0, hub_low, 0), 1.0), ((0, hub_high, 0), 1.0)]
    positions = {
        "Nose": [(0, -1.05, F - 0.8)],
        "Pylon": pylon_points,
        "Utility": [(1.2, -0.95, -1.5), (-1.2, -0.95, -1.5)],
        "Winch": [(0, -H / 2 - 0.05, -0.8)],
        "Rotor": [point for point, _s in rotors],
        "Engine": [(0, t["sp_top"](-1.4), -1.4)],
        "Armour": [(0, 0, 0)],
    }
    return rotors, positions, ground


BUILDERS = {"wisp": _wisp, "lance": _lance, "aegis": _aegis, "halo": _halo}


def chassis(spec, style):
    """An Arcus chassis (spec "body" in BODIES) in its maker's look laid over `style`. Same spec + style = same model."""
    from . import styles
    p = _parts()
    look = styles.with_maker(style, spec.get("maker", "arcus"))
    p.use_style(look)
    try:
        b = Builder()
        legs = []
        rotors, positions, ground = BUILDERS[spec["body"]](b, spec, look, legs)
        gear_faces = [i for leg in legs for i in leg.faces]
        # Belly beacon.
        b.lay((0, -50.0, -0.3), (0, 1, 0), (0.04, 0.1, 0.1), C(look["lights"]["right"]), everything=True)
        sockets = p._seat_sockets(b, _make_sockets(spec, positions, rotors))
        part = p.Part(f"Chassis_{spec['name']}", "chassis", b, sockets=sockets, seed=spec["seed"], ground=ground,
                      gear_faces=gear_faces, gear_legs=legs)
        part.form = look.get("form")
        # Skinned bodies carry their own planned panel lines: no random surface panels on top ({} = none).
        part.detail = {} if spec["body"] in SKINNED else look.get("detail")
        part.planned_lines = spec["body"] in SKINNED
        return part
    finally:
        p.use_style(style)


def cockpit_eye(spec):
    """The pilot's eye and the interior to build (the rugged cockpits fit the Arcus canopies)."""
    H, F = spec["height"], spec["front"]
    body = spec["body"]
    if body == "lance":
        return (0.0, 0.48, 1.5), "military"
    if body == "aegis":
        return (0.0, 0.62, 1.5), "military"
    if body == "halo":
        return (0.0, 0.42, F - 2.45), "cabin"
    return (0.0, 0.36, F - 1.62), "scout"
