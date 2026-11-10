"""
Module builders (weapons, pods, racks, utilities, turret, winch, rotors, engines). Units are metres in Unity space
(x right, y up, z forward). Module origin = its attach point: it snaps onto any socket of its slot type with no offset,
pointing forward (+z), hanging down (-y).

The parts are machined, not blocked out: turned profiles (Builder.lathe) for barrels, nozzles, hubs and stores; bent
hoses and feed chutes (Builder.tube); swept fins; aerofoil rotor blades. Keep each module inside its old envelope: the
game reads the barrel tip (the model's largest z) as the muzzle, and the audit checks the ground clearance.
"""
import math

from mathutils import Vector

from .geo import Builder, rot_x, rot_y, rot_z
from .parts import C, Part

Z = (0.0, 0.0, 1.0)
DOWN = (0.0, -1.0, 0.0)


def _turned(b, x, y, profile, swatch, segments=12, **caps):
    """Lathe along +z through (x, y): profile distances are z."""
    b.lathe((x, y, 0.0), Z, profile, swatch, segments=segments, **caps)


def _circle(count, radius, phase=0.0):
    return [(radius * math.cos(phase + 2 * math.pi * k / count), radius * math.sin(phase + 2 * math.pi * k / count))
            for k in range(count)]


def _mount(b, length=0.5, pads=None):
    """Hardpoint adapter under the socket that every hanging module shares: a pylon fairing (0 to 0.14 m down), an
    ejector rack beam (to 0.2 m: the module body starts at -0.18 or higher, so it always cuts into it) with two lug
    pins, and with pads = (x, y) four sway braces whose pads sit on the store at that point."""
    b.box((0, -0.07, 0), (0.13, 0.14, length), C("hull_dark"))
    b.box((0, -0.165, 0), (0.085, 0.07, length * 1.12), C("gunmetal"))
    for z in (-length * 0.3, length * 0.3):
        b.cylinder((-0.062, -0.15, z), (0.062, -0.15, z), 0.02, C("steel"), segments=6)
    if pads:
        px, py = pads
        for z in (-length * 0.44, length * 0.44):
            for side in (1, -1):
                b.cylinder((side * 0.03, -0.165, z), (side * px, py + 0.02, z), 0.016, C("gunmetal"), segments=6)
                b.cylinder((side * px, py + 0.035, z), (side * px, py - 0.012, z), 0.03, C("steel"), segments=8)


def _fins(b, x, y, z_back, root, span, sweep, inner, thickness, swatch, count=4, phase=45.0, tip=0.45):
    """Swept fins round a store on the z axis through (x, y): the root runs from z_back to z_back + root at radius
    `inner`; the tip is `span` further out, its leading edge `sweep` behind the root's and its length root * tip."""
    for k in range(count):
        turn = rot_z(phase + 360.0 * k / count)
        outline = ((inner, z_back), (inner, z_back + root), (inner + span, z_back + root - sweep),
                   (inner + span, z_back + root - sweep - root * tip))
        sides = []
        for half, squeeze in ((-thickness / 2, 1.0), (thickness / 2, 1.0)):
            ring = []
            for r, z in outline:
                # Thinner at the tip: a wedge section, not a plank.
                offset = turn @ Vector((half * (0.45 if r > inner else squeeze), r, 0.0))
                ring.append((x + offset.x, y + offset.y, z))
            sides.append(ring)
        b.raw_loft(sides, swatch)


# ---------------------------------------------------------------------------------------------------------------------
# Rotors
# ---------------------------------------------------------------------------------------------------------------------

# Aerofoil section: (along the chord, + = leading edge; thickness share, + = up).
AEROFOIL = ((0.30, 0.0), (0.22, 0.42), (0.02, 0.5), (-0.32, 0.36), (-0.70, 0.06), (-0.70, -0.06), (-0.32, -0.22),
            (0.02, -0.34), (0.22, -0.3))


def _blade(b, turn, y, stations, swatch, tip_swatch, tip_bands=2):
    """One blade along the turned +x: stations = (radius, chord, thickness, twist degrees, sweep back)."""
    sections = []
    for radius, chord, thickness, twist, back in stations:
        roll = rot_x(twist)
        ring = []
        for along, up in AEROFOIL:
            local = roll @ Vector((0.0, up * thickness, along * chord))
            ring.append(tuple(turn @ Vector((radius, y + local.y, local.z - back))))
        sections.append(ring)
    bands = len(sections) - 1
    b.raw_loft(sections, swatch, swatches=[None] * (bands - tip_bands) + [tip_swatch] * tip_bands)


def rotor(name, ids, blades, length, chord, tip):
    """Articulated rotor head: mast collar, swashplate, two hub plates that clamp the blade grips, pitch links and
    lead-lag dampers, a dome on top; blades with a root cuff, washout twist and a swept, painted tip."""
    b = Builder()
    g, s = C("gunmetal"), C("steel")
    b.lathe((0, 0, 0), (0, 1, 0), [
        (0.0, 0.15, s), (0.03, 0.15), (0.03, 0.23, g), (0.06, 0.23), (0.06, 0.12, s), (0.1, 0.12),
        (0.1, 0.27, g), (0.125, 0.27), (0.125, 0.17, s), (0.195, 0.17), (0.195, 0.27, g), (0.22, 0.27),
        (0.22, 0.15, C("hull_dark")), (0.3, 0.13), (0.36, 0.09), (0.4, 0.03)], s, segments=16)
    y = 0.16
    reach = 0.22 + length
    half = chord * 0.275
    for k in range(blades):
        turn = rot_y(360.0 * k / blades)
        b.box(tuple(turn @ Vector((0.36, y, 0))), (0.34, 0.085, half * 2), g, rotation=turn)
        for z in (-0.05, 0.05):
            b.cylinder(tuple(turn @ Vector((0.23, 0.095, z))), tuple(turn @ Vector((0.23, 0.235, z))), 0.018, s,
                       segments=6)
        # Pitch horn on the leading side of the grip, its link down to the swashplate.
        b.box(tuple(turn @ Vector((0.42, y - 0.01, half + 0.03))), (0.05, 0.04, 0.1), g, rotation=turn)
        b.cylinder(tuple(turn @ Vector((0.18, 0.045, 0.12))), tuple(turn @ Vector((0.42, y - 0.01, half + 0.05))),
                   0.012, s, segments=5)
        # Lead-lag damper from the trailing side of the grip back to the hub.
        b.cylinder(tuple(turn @ Vector((0.47, y, -half * 0.9))), tuple(turn @ Vector((0.19, y, -0.16))), 0.022,
                   C("hull_dark"), segments=6)
        _blade(b, turn, y, [
            (0.30, chord * 0.42, 0.085, 0.0, 0.0),
            (0.52, chord * 0.5, 0.075, 9.0, 0.0),
            (0.85, chord, 0.055, 8.0, 0.0),
            (reach - 0.62, chord, 0.046, 3.5, 0.0),
            (reach - 0.60, chord, 0.046, 3.5, 0.0),
            (reach - 0.16, chord * 0.86, 0.04, 2.5, chord * 0.14),
            (reach, chord * 0.5, 0.026, 2.0, chord * 0.42),
        ], C("carbon"), C(tip))
        # Leading-edge erosion strip (bare metal) on the outer half.
        b.box(tuple(turn @ Vector((reach * 0.68, y, chord * 0.292))), (reach * 0.5, 0.02, chord * 0.035), s,
              rotation=turn @ rot_x(5))
    return Part(name, "module", b, slot="Rotor", module_ids=ids)


# ---------------------------------------------------------------------------------------------------------------------
# Nose guns
# ---------------------------------------------------------------------------------------------------------------------

def _cradle(b, y, z, half_width, drop_to):
    """Gun cradle: a cross plate under the mount and two cheek plates down to the trunnion pins at height y."""
    g = C("gunmetal")
    b.box((0, -0.15, z), (half_width * 2 + 0.025, 0.03, 0.22), g)
    for side in (1, -1):
        b.box((side * half_width, (-0.135 + drop_to) / 2, z), (0.025, -0.135 - drop_to, 0.22), g)
        b.cylinder((side * (half_width - 0.035), y, z), (side * (half_width + 0.03), y, z), 0.045, C("steel"),
                   segments=8)


def minigun():
    b = Builder()
    _mount(b, 0.4)
    y = -0.3
    g, s = C("gunmetal"), C("steel")
    _cradle(b, y, 0.05, 0.155, -0.34)
    # Drive motor, then the receiver housing with cooling ribs, then the rotor bearing.
    profile = [(-0.34, 0.05), (-0.33, 0.075), (-0.2, 0.075), (-0.2, 0.1), (-0.17, 0.125), (-0.12, 0.125),
               (-0.12, 0.138)]
    for i in range(5):
        z = -0.07 + i * 0.09
        profile += [(z, 0.138), (z, 0.147), (z + 0.03, 0.147), (z + 0.03, 0.138)]
    profile += [(0.36, 0.138), (0.36, 0.122), (0.4, 0.122), (0.4, 0.108, s), (0.46, 0.108), (0.46, 0.06)]
    _turned(b, 0, y, profile, g, segments=14)
    for ox, oy in _circle(6, 0.07):
        _turned(b, ox, y + oy, [(0.4, 0.022), (1.2, 0.022), (1.2, 0.028, g), (1.3, 0.028), (1.3, 0.017),
                                (1.27, 0.017)], s, segments=7, cap_end=C("rubber"))
    b.cylinder((0, y, 0.4), (0, y, 1.24), 0.03, g, segments=8)
    for z0, z1, radius in ((0.72, 0.755, 0.107), (1.1, 1.145, 0.107)):
        _turned(b, 0, y, [(z0, radius * 0.9), (z0 + 0.008, radius), (z1 - 0.008, radius), (z1, radius * 0.9)], g,
                segments=14)
    # Ammunition box on the right, lid, warning stripe, and the feed chute over the cheek into the receiver.
    b.box((0.25, y + 0.02, -0.05), (0.14, 0.24, 0.36), C("hull_dark"))
    b.box((0.25, y + 0.15, -0.05), (0.15, 0.025, 0.37), g)
    b.box((0.324, y + 0.02, -0.05), (0.012, 0.06, 0.3), C("hazard"))
    b.tube([(0.25, y + 0.13, 0.02), (0.24, y + 0.2, 0.1), (0.15, y + 0.2, 0.21), (0.06, y + 0.12, 0.25)], 0.036, s,
           segments=4)
    return Part("Gun_Minigun", "module", b, slot="Nose", module_ids=[101])


def autocannon():
    b = Builder()
    _mount(b, 0.5)
    y = -0.3
    g, s = C("gunmetal"), C("steel")
    _cradle(b, y, 0.1, 0.135, -0.33)
    b.box((0, y, 0.12), (0.24, 0.26, 0.74), g)
    b.box((0, y + 0.135, 0.1), (0.16, 0.03, 0.5), C("hull_dark"))
    _turned(b, 0, y, [(-0.36, 0.05), (-0.34, 0.07), (-0.28, 0.07), (-0.28, 0.09), (-0.22, 0.09)], g, segments=10)
    # Barrel: jacket, stepped tube, muzzle brake with three ports.
    _turned(b, 0, y, [(0.45, 0.095, g), (0.62, 0.095), (0.62, 0.075, g), (1.0, 0.07), (1.0, 0.055, s), (1.86, 0.047),
                      (1.86, 0.07, g), (1.9, 0.085), (2.12, 0.085), (2.14, 0.07), (2.14, 0.036), (2.08, 0.036)], g,
            segments=12, cap_end=C("rubber"))
    for z in (1.95, 2.01, 2.07):
        b.box((0, y, z), (0.19, 0.05, 0.03), C("rubber"))
    # Recoil cylinders above the barrel and their yoke.
    for side in (1, -1):
        _turned(b, side * 0.07, y + 0.09, [(0.4, 0.03, g), (0.7, 0.03), (0.7, 0.02, s), (1.0, 0.02)], g, segments=7)
    b.box((0, y + 0.05, 0.99), (0.2, 0.12, 0.04), g)
    # Ammunition drum on the left with its warning band, the feed bridge, an optical sight on the right.
    _turned(b, -0.21, y, [(-0.19, 0.08), (-0.16, 0.13), (-0.02, 0.13), (-0.02, 0.134, C("hazard")), (0.06, 0.134),
                          (0.06, 0.13), (0.24, 0.13), (0.27, 0.08)], C("hull_dark"), segments=12)
    b.box((-0.14, y + 0.11, 0.3), (0.16, 0.05, 0.12), s)
    b.box((0.15, y + 0.05, 0.3), (0.07, 0.09, 0.2), C("hull_dark"))
    b.cylinder((0.15, y + 0.05, 0.39), (0.15, y + 0.05, 0.43), 0.03, g, segments=8, cap_end=C("glass"))
    return Part("Gun_Autocannon", "module", b, slot="Nose", module_ids=[102])


def pulse_laser():
    b = Builder()
    _mount(b, 0.5)
    y = -0.3
    g, s, glow = C("gunmetal"), C("steel"), C("neon_cyan")
    b.box((0, y, 0.12), (0.28, 0.24, 0.66), C("hull_dark"))
    # Heat sink fins on the rear half, capacitor banks on the front half of both sides.
    for side in (1, -1):
        for i in range(5):
            b.box((side * 0.145, y, -0.16 + i * 0.055), (0.03, 0.2, 0.022), g)
        _turned(b, side * 0.165, y - 0.02, [(0.1, 0.035), (0.12, 0.055), (0.22, 0.055), (0.22, 0.059, glow),
                                             (0.25, 0.059), (0.25, 0.055), (0.4, 0.055), (0.43, 0.035)], g,
                segments=8)
    # Emitter: a dark core tube between two rails, four focusing coils (holder + glowing ring), the lens head.
    b.cylinder((0, y, 0.4), (0, y, 1.64), 0.042, g, segments=10)
    for dy in (0.1, -0.1):
        b.box((0, y + dy, 1.05), (0.035, 0.03, 1.24), s)
    for z in (0.66, 0.92, 1.18, 1.44):
        _turned(b, 0, y, [(z - 0.03, 0.06), (z - 0.03, 0.096), (z, 0.096), (z, 0.086, glow), (z + 0.045, 0.086),
                          (z + 0.045, 0.06)], g, segments=12)
    _turned(b, 0, y, [(1.6, 0.05), (1.64, 0.088), (1.74, 0.088), (1.76, 0.076), (1.76, 0.062, C("rubber")),
                      (1.71, 0.055)], g, segments=12, cap_end=glow)
    for side in (1, -1):
        b.tube([(side * 0.08, y + 0.1, 0.38), (side * 0.11, y + 0.15, 0.62), (side * 0.07, y + 0.13, 0.82),
                (side * 0.02, y + 0.1, 0.95)], 0.017, C("rubber"), segments=6)
    return Part("Gun_PulseLaser", "module", b, slot="Nose", module_ids=[103])


# ---------------------------------------------------------------------------------------------------------------------
# Pylon stores
# ---------------------------------------------------------------------------------------------------------------------

def rocket_pod():
    b = Builder()
    y, radius = -0.48, 0.3
    _mount(b, 0.7, pads=(0.17, y + math.sqrt(radius ** 2 - 0.17 ** 2)))
    g = C("gunmetal")
    # Body: tail cone, two straps, warning band, a nose lip round the recessed tube face.
    _turned(b, 0, y, [(-0.92, 0.12, C("hull_dark")), (-0.78, 0.27), (-0.7, radius), (-0.5, radius),
                      (-0.5, 0.305, g), (-0.44, 0.305), (-0.44, radius), (0.1, radius), (0.1, 0.305, g),
                      (0.16, 0.305), (0.16, radius), (0.36, radius), (0.36, 0.305, C("hazard")), (0.5, 0.305),
                      (0.5, radius), (0.69, radius), (0.75, 0.288), (0.75, 0.258, C("rubber")), (0.7, 0.258)],
            C("hull"), segments=16, cap_start=C("rubber"), cap_end=C("rubber"))
    tubes = [(0.0, 0.0)] + _circle(6, 0.105) + _circle(12, 0.205, math.pi / 12)
    for ox, oy in tubes:
        _turned(b, ox, y + oy, [(0.69, 0.042), (0.722, 0.042), (0.722, 0.031, C("rubber")), (0.7, 0.031)], g,
                segments=7, cap_end=C("rubber"))
    for z in (-0.25, 0.25):
        b.box((0, y + radius + 0.005, z), (0.06, 0.06, 0.09), C("steel"))
    return Part("Pod_Rockets", "module", b, slot="Pylon", module_ids=[111])


def swarm_pod():
    b = Builder()
    y = -0.38
    _mount(b, 0.7, pads=(0.16, y + 0.21))
    g = C("gunmetal")
    square = {"front": C("rubber")}  # per-side swatches also keep the box square (a launcher, not a moulded shell)
    b.box((0, y, 0), (0.5, 0.42, 1.24), C("hull"), swatches=square)
    for z in (-0.38, 0.3):
        b.box((0, y, z), (0.52, 0.425, 0.04), g)
    for side in (1, -1):
        b.box((side * 0.255, y - 0.02, -0.03), (0.03, 0.3, 0.6), C("hull_light"))
        b.box((side * 0.272, y + 0.09, -0.03), (0.012, 0.03, 0.52), C("neon_pink"))
    # Tube face: a bezel, twelve tubes, two of them still sealed with frangible covers.
    for dy in (0.195, -0.195):
        b.box((0, y + dy, 0.63), (0.5, 0.03, 0.06), C("hull_dark"))
    for side in (1, -1):
        b.box((side * 0.235, y, 0.63), (0.03, 0.42, 0.06), C("hull_dark"))
    for row in range(3):
        for col in range(4):
            x = -0.165 + col * 0.11
            yy = y - 0.12 + row * 0.12
            sealed = (row, col) in ((0, 3), (2, 1))
            _turned(b, x, yy, [(0.6, 0.046), (0.655, 0.046), (0.655, 0.036, C("rubber")), (0.63, 0.036)], g,
                    segments=7, cap_end=C("missile") if sealed else C("rubber"))
    # Rear blast door with louvres, suspension lugs.
    b.box((0, y, -0.63), (0.44, 0.36, 0.04), g)
    for i in range(4):
        b.box((0, y - 0.12 + i * 0.08, -0.655), (0.38, 0.03, 0.02), C("rubber"))
    for z in (-0.25, 0.25):
        b.box((0, y + 0.225, z), (0.1, 0.04, 0.12), C("steel"))
    return Part("Pod_Swarm", "module", b, slot="Pylon", module_ids=[117])


def _missile(b, centre, length, radius, kind="homing"):
    """One missile along z. kind: homing (glass seeker dome, canards, swept tail fins), ground (blunt seeker, long
    strakes, clipped fins) or cluster (fat ogive, warning band, submunition ports, stub fins)."""
    x, y, z = centre
    tail, nose = z - length / 2, z + length / 2
    r, m, g = radius, C("missile"), C("gunmetal")
    body = [(tail, r * 0.62, g), (tail + 0.05, r * 0.74), (tail + 0.05, r, m)]
    if kind == "homing":
        body += [(nose - 4.9 * r, r), (nose - 4.9 * r, r * 1.01, C("warhead")), (nose - 3.9 * r, r * 1.01),
                 (nose - 3.9 * r, r), (nose - 3.3 * r, r), (nose - 3.3 * r, r * 0.95, C("steel")),
                 (nose - 1.4 * r, r * 0.92), (nose - 1.4 * r, r * 0.9, C("glass")), (nose - 0.7 * r, r * 0.74),
                 (nose - 0.25 * r, r * 0.44), (nose, r * 0.1)]
        _fins(b, x, y, nose - 3.2 * r, 1.5 * r, 0.075, 0.1, r * 0.8, 0.012, C("steel"), tip=0.3)
        _fins(b, x, y, tail + 0.04, 0.34, 0.15, 0.2, r * 0.8, 0.016, C("hull_dark"), tip=0.5)
    elif kind == "ground":
        body += [(nose - 5.2 * r, r), (nose - 5.2 * r, r * 1.01, C("warhead")), (nose - 4.6 * r, r * 1.01),
                 (nose - 4.6 * r, r), (nose - 1.5 * r, r), (nose - 1.5 * r, r * 1.03, g), (nose - 1.1 * r, r * 1.03),
                 (nose - 1.1 * r, r * 0.97, C("glass")), (nose - 0.5 * r, r * 0.84), (nose - 0.15 * r, r * 0.52),
                 (nose, r * 0.12)]
        _fins(b, x, y, z - 0.42, 0.72, 0.05, 0.12, r * 0.85, 0.014, C("hull_dark"), tip=0.75)
        _fins(b, x, y, tail + 0.04, 0.26, 0.14, 0.1, r * 0.8, 0.018, C("hull_dark"), tip=0.62)
    else:
        body += [(nose - 5.0 * r, r), (nose - 5.0 * r, r * 1.01, C("hazard")), (nose - 4.3 * r, r * 1.01),
                 (nose - 4.3 * r, r), (nose - 2.6 * r, r), (nose - 1.7 * r, r * 0.9), (nose - 0.9 * r, r * 0.66),
                 (nose - 0.35 * r, r * 0.36), (nose - 0.35 * r, r * 0.3, C("steel")), (nose, r * 0.12)]
        for station in (nose - 3.1 * r, nose - 3.8 * r):
            for k in range(6):
                turn = rot_z(30 + 60 * k + (30 if station < nose - 3.5 * r else 0))
                inner, outer = turn @ Vector((0, r * 0.8, 0)), turn @ Vector((0, r * 1.015, 0))
                b.cylinder((x + inner.x, y + inner.y, station), (x + outer.x, y + outer.y, station), r * 0.17,
                           C("rubber"), segments=6)
        _fins(b, x, y, tail + 0.04, 0.3, 0.1, 0.12, r * 0.8, 0.02, C("hull_dark"), tip=0.6)
    _turned(b, x, y, body, m, segments=12, cap_start=C("rubber"))


def missile_rack(name, ids, count, length, radius, kind="homing"):
    b = Builder()
    _mount(b, 0.6)
    g = C("gunmetal")
    b.box((0, -0.21, 0), (0.09, 0.07, length * 0.78), g)
    xs = [0.0] if count == 1 else [-(radius + 0.04), radius + 0.04]
    for x in xs:
        _missile(b, (x, -0.3 - radius, 0.1), length, radius, kind)
        # Launch rail over the missile, its nose fairing, two hangers down to the body.
        b.box((x, -0.27, 0.05), (0.05, 0.07, length * 0.6), C("steel"))
        b.box((x, -0.262, 0.05 + length * 0.3 + 0.03), (0.04, 0.05, 0.08), g, rotation=rot_x(-28))
        for z in (-0.25, 0.45):
            b.box((x, -0.285, z), (0.07, 0.06, 0.09), g)
    if count > 1:
        for z in (-0.3, 0.4):
            b.box((0, -0.24, z), (xs[-1] * 2 + 0.06, 0.05, 0.1), g)
        # Umbilical from the rack to each rail.
        for x in xs:
            b.tube([(0.0, -0.19, 0.62), (x * 0.5, -0.2, 0.7), (x, -0.24, 0.62)], 0.012, C("rubber"), segments=5)
    return Part(name, "module", b, slot="Pylon", module_ids=ids)


def bomb_rack():
    b = Builder()
    _mount(b, 0.6)
    g = C("gunmetal")
    b.box((0, -0.22, 0), (0.5, 0.06, 0.7), g)
    for x in (-0.16, 0.16):
        y = -0.36
        _turned(b, x, y, [(-0.62, 0.035, g), (-0.5, 0.06, C("hull_dark")), (-0.3, 0.128), (-0.1, 0.14), (0.2, 0.14),
                          (0.2, 0.143, C("hazard")), (0.28, 0.143), (0.28, 0.14), (0.38, 0.126), (0.5, 0.086),
                          (0.57, 0.042), (0.57, 0.03, C("steel")), (0.62, 0.02)], C("hull_dark"), segments=12)
        _fins(b, x, y, -0.62, 0.26, 0.12, 0.1, 0.03, 0.014, g, tip=0.55)
        for z in (-0.2, 0.2):
            b.box((x, -0.235, z), (0.07, 0.06, 0.06), C("steel"))
    for z in (-0.3, 0.3):
        b.box((0, -0.262, z), (0.26, 0.03, 0.05), g)
    return Part("Rack_Bombs", "module", b, slot="Pylon", module_ids=[113])


# ---------------------------------------------------------------------------------------------------------------------
# Utilities, turret, winch
# ---------------------------------------------------------------------------------------------------------------------

def dispenser(name, ids, accent, rows=2, cols=4):
    """Countermeasure box (flares, smoke, mines): a flange on the hull, a housing, a block of tubes on the back face
    (most loaded: caps in the accent colour; some fired: dark), a status light and a cable."""
    b = Builder()
    g = C("gunmetal")
    b.box((0, -0.02, 0), (0.4, 0.04, 0.54), g)
    b.box((0, -0.13, 0.02), (0.34, 0.2, 0.46), C("hull_dark"))
    b.box((0, -0.135, -0.235), (0.31, 0.18, 0.08), g)
    dx, dy = 0.27 / cols, 0.15 / rows
    radius = min(dx, dy) * 0.4
    for row in range(rows):
        for col in range(cols):
            x = -0.135 + dx * (col + 0.5)
            y = -0.21 + dy * (row + 0.5)
            fired = (row * 3 + col * 5) % 4 == 0
            _turned(b, x, y, [(-0.3, radius * 0.8), (-0.3, radius), (-0.27, radius)], g, segments=7,
                    cap_start=C("rubber") if fired else C(accent))
    for side in (1, -1):
        b.box((side * 0.172, -0.12, 0.05), (0.02, 0.08, 0.1), g)
    b.box((-0.07, -0.09, 0.252), (0.12, 0.04, 0.012), C(accent))
    b.box((0.1, -0.09, 0.254), (0.045, 0.022, 0.014), C("neon_green"))
    b.tube([(0.165, -0.16, 0.16), (0.2, -0.12, 0.17), (0.19, -0.03, 0.2)], 0.014, C("rubber"), segments=5)
    return Part(name, "module", b, slot="Utility", module_ids=ids)


def repair_drone():
    b = Builder()
    g = C("gunmetal")
    b.box((0, -0.03, 0), (0.5, 0.06, 0.5), C("hull_dark"))
    for side in (1, -1):
        b.box((side * 0.2, -0.09, 0), (0.04, 0.08, 0.3), g)
    # A small quad drone parked on its dock: body, four arms, ducted fans, a camera eye.
    b.box((0, -0.14, 0), (0.2, 0.09, 0.24), C("hull_light"))
    b.box((0, -0.19, -0.02), (0.12, 0.02, 0.14), g)
    for k in range(4):
        turn = rot_y(45 + 90 * k)
        b.box(tuple(turn @ Vector((0.155, -0.14, 0))), (0.23, 0.03, 0.035), g, rotation=turn)
        tip = turn @ Vector((0.26, -0.14, 0))
        b.duct((tip.x, -0.14, tip.z), 0.085, 0.07, 0.045, C("carbon"), segments=12)
        b.cylinder((tip.x, -0.165, tip.z), (tip.x, -0.12, tip.z), 0.024, C("steel"), segments=6)
        b.cylinder((tip.x, -0.146, tip.z), (tip.x, -0.138, tip.z), 0.066, C("carbon"), segments=8)
    b.box((0, -0.15, 0.13), (0.07, 0.05, 0.04), g)
    b.cylinder((0, -0.15, 0.14), (0, -0.15, 0.165), 0.02, g, segments=8, cap_end=C("glass"))
    b.box((0.06, -0.12, 0.122), (0.03, 0.015, 0.012), C("neon_green"))
    return Part("Utility_RepairDrone", "module", b, slot="Utility", module_ids=[133])


def emitter(name, ids, glow, kind="coil"):
    """EMP coil (a stack of glowing windings between insulator discs, in a cage of three struts) or shield projector
    (a wide dish with a glowing rim and a short horn)."""
    b = Builder()
    g, glow = C("gunmetal"), C(glow)
    b.box((0, -0.06, 0), (0.3, 0.12, 0.3), C("hull_dark"))
    origin = (0, -0.1, 0)
    if kind == "coil":
        b.lathe(origin, DOWN, [(0.0, 0.12), (0.03, 0.2), (0.05, 0.26), (0.07, 0.26), (0.07, 0.1), (0.1, 0.1),
                               (0.1, 0.2, glow), (0.125, 0.2), (0.125, 0.09), (0.16, 0.09), (0.16, 0.17, glow),
                               (0.185, 0.17), (0.185, 0.08), (0.22, 0.08), (0.22, 0.14, glow), (0.24, 0.14),
                               (0.24, 0.07), (0.3, 0.05), (0.3, 0.03)], g, segments=14)
        for k in range(3):
            turn = rot_y(120 * k + 30)
            top, bottom = turn @ Vector((0.23, -0.16, 0)), turn @ Vector((0.045, -0.395, 0))
            b.cylinder(tuple(top), tuple(bottom), 0.012, C("steel"), segments=5)
        b.ico((0, -0.43, 0), 0.07, glow, subdivisions=1)
    else:
        b.lathe(origin, DOWN, [(0.0, 0.12), (0.03, 0.16), (0.07, 0.3, C("hull_dark")), (0.1, 0.3),
                               (0.1, 0.27, glow), (0.085, 0.24), (0.085, 0.2, g), (0.07, 0.07), (0.2, 0.05),
                               (0.2, 0.1, glow), (0.23, 0.1), (0.23, 0.045, g), (0.3, 0.03)], g, segments=16)
        for k in range(4):
            turn = rot_y(90 * k + 45)
            top, bottom = turn @ Vector((0.25, -0.185, 0)), turn @ Vector((0.07, -0.31, 0))
            b.cylinder(tuple(top), tuple(bottom), 0.012, C("steel"), segments=5)
        b.ico((0, -0.43, 0), 0.06, glow, subdivisions=1)
    b.tube([(0.1, -0.08, -0.15), (0.13, -0.05, -0.2), (0.1, -0.01, -0.17)], 0.014, C("rubber"), segments=5)
    return Part(name, "module", b, slot="Utility", module_ids=ids)


def flak_turret():
    b = Builder()
    g, s = C("gunmetal"), C("steel")
    # Compact ball (about 0.6 m deep): belly turrets must clear the ground on every chassis (audit).
    b.lathe((0, 0, 0), DOWN, [(0.0, 0.36), (0.04, 0.36), (0.04, 0.33), (0.1, 0.3), (0.13, 0.25)], g, segments=16)
    b.ico((0, -0.36, 0), 0.26, C("hull"), subdivisions=1)
    for side in (1, -1):
        b.box((side * 0.215, -0.36, 0.03), (0.06, 0.3, 0.34), C("hull_dark"))
        b.cylinder((side * 0.2, -0.36, 0.03), (side * 0.262, -0.36, 0.03), 0.06, s, segments=8)
        b.box((side * 0.2, -0.33, -0.17), (0.1, 0.18, 0.16), C("hull_dark"))
        b.box((side * 0.253, -0.33, -0.17), (0.01, 0.05, 0.12), C("hazard"))
    b.box((0, -0.36, 0.23), (0.3, 0.15, 0.1), g)
    for x in (-0.09, 0.09):
        _turned(b, x, -0.36, [(0.14, 0.06, g), (0.32, 0.06), (0.32, 0.045, g), (0.55, 0.045), (0.55, 0.034, s),
                              (0.93, 0.031), (0.93, 0.05, g), (1.08, 0.05), (1.1, 0.042), (1.1, 0.024),
                              (1.05, 0.024)], s, segments=8, cap_end=C("rubber"))
        b.box((x, -0.36, 1.0), (0.11, 0.025, 0.03), C("rubber"))
    b.box((0, -0.19, 0.2), (0.12, 0.08, 0.1), C("hull_dark"))
    b.cylinder((0, -0.19, 0.24), (0, -0.19, 0.262), 0.028, g, segments=8, cap_end=C("neon_cyan"))
    return Part("Turret_Flak", "module", b, slot="Turret", module_ids=[121])


def winch():
    b = Builder()
    g, s = C("gunmetal"), C("steel")
    # Low profile (about 0.5 m): it hangs under the belly, between the wheels, and must clear the ground (audit).
    b.box((0, -0.03, 0), (0.6, 0.06, 0.6), C("hull_dark"))
    for side in (1, -1):
        b.box((side * 0.27, -0.22, 0), (0.04, 0.36, 0.36), g)
    # Drum: two flanges, the wound cable between them.
    profile = [(0.0, 0.15, s), (0.02, 0.15), (0.02, 0.1, C("rubber"))]
    for i in range(10):
        d = 0.02 + i * 0.046
        profile += [(d + 0.023, 0.106), (d + 0.046, 0.1)]
    profile += [(0.48, 0.15, s), (0.5, 0.15)]
    b.lathe((-0.25, -0.24, 0), (1, 0, 0), profile, C("rubber"), segments=14)
    b.lathe((-0.3, -0.24, 0), (1, 0, 0), [(0.0, 0.04), (0.6, 0.04)], s, segments=8)
    # Motor behind the drum on its bracket, warning band.
    b.box((0, -0.1, -0.21), (0.24, 0.1, 0.1), g)
    b.lathe((-0.17, -0.165, -0.21), (1, 0, 0), [(0.0, 0.05), (0.02, 0.07), (0.12, 0.07), (0.12, 0.073, C("hazard")),
                                                (0.18, 0.073), (0.18, 0.07), (0.32, 0.07), (0.34, 0.05)],
            C("hull_dark"), segments=10)
    # Fairlead bar under the drum; the hook stowed just under it: the cable pays out in game, not on the model.
    b.box((0, -0.385, 0), (0.58, 0.03, 0.1), g)
    b.cylinder((0, -0.3, 0), (0, -0.45, 0), 0.013, s, segments=5)
    b.box((0, -0.47, 0), (0.16, 0.07, 0.06), C("hazard"))
    return Part("Winch_Heavy", "module", b, slot="Winch", module_ids=[141])


# ---------------------------------------------------------------------------------------------------------------------
# Engines
# ---------------------------------------------------------------------------------------------------------------------

def engine(name, ids, nozzles, glow, radius):
    """Exhaust stack that sits on the chassis' engine socket (the deck at the back of the engines): per nozzle a
    collar, a heat-shield band, a cone with petals, and the glow deep inside the dark throat."""
    b = Builder()
    g = C("gunmetal")
    xs = [0.0] if nozzles == 1 else [(-0.28 + 0.56 * i / (nozzles - 1)) for i in range(nozzles)]
    span = xs[-1] - xs[0]
    b.box((0, -0.02, 0.1), (span + radius * (1.0 if nozzles > 1 else 2.3), radius * 1.2, 0.24), C("hull_dark"))
    r = radius
    for x in xs:
        b.lathe((x, 0, 0.2), (0, 0, -1), [
            (0.0, r * 0.9, C("hull_dark")), (0.04, r * 1.08), (0.14, r * 1.08), (0.14, r, g), (0.3, r * 1.02),
            (0.3, r * 1.1, C("steel")), (0.34, r * 1.1), (0.34, r, g), (0.6, r * 0.84), (0.65, r * 0.82),
            (0.65, r * 0.7, C("soot")), (0.5, r * 0.62)], g, segments=12, cap_end=C(glow))
        for ox, oy in _circle(8, r * 0.95):
            angle = math.degrees(math.atan2(oy, ox)) - 90
            b.box((x + ox, oy, -0.27), (r * 0.5, 0.012, 0.3), C("hull_dark"), rotation=rot_z(angle) @ rot_x(0))
    if nozzles > 1:
        b.tube([(xs[0], r * 0.9, 0.12), (0.0, r * 1.25, 0.1), (xs[-1], r * 0.9, 0.12)], 0.02, C("steel"), segments=5)
    return Part(name, "module", b, slot="Engine", module_ids=ids)


def all_modules():
    return [
        minigun(), autocannon(), pulse_laser(),
        rocket_pod(), missile_rack("Rack_HomingMissiles", [112], 2, 2.0, 0.09),
        missile_rack("Rack_GroundSeeker", [115], 1, 2.3, 0.13, kind="ground"),
        missile_rack("Rack_ClusterMissile", [116], 1, 2.1, 0.16, kind="cluster"),
        swarm_pod(), bomb_rack(),
        dispenser("Utility_AirMines", [114], "warhead", rows=1, cols=3),
        flak_turret(),
        dispenser("Utility_Flares", [131], "hazard"),
        dispenser("Utility_Smoke", [132], "hull_light", rows=2, cols=3),
        repair_drone(),
        emitter("Utility_EMP", [134], "neon_cyan", kind="coil"),
        emitter("Utility_Shield", [135], "neon_pink", kind="dish"),
        winch(),
        rotor("Rotor_Standard", [151], blades=4, length=5.2, chord=0.36, tip="hazard"),
        rotor("Rotor_Agile", [152], blades=5, length=4.6, chord=0.3, tip="neon_green"),
        rotor("Rotor_HeavyLift", [153], blades=3, length=7.2, chord=0.62, tip="hazard"),
        engine("Engine_Standard", [161], 2, "exhaust", 0.13),
        engine("Engine_Boost", [162], 2, "neon_pink", 0.15),
        engine("Engine_Turbine", [163], 1, "exhaust", 0.22),
    ]
