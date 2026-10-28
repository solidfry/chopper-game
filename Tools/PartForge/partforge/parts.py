"""
Part builders. Units are metres in Unity space (x right, y up, z forward). Chassis origin = centre of mass (the flight
code turns the chopper about its root). Module origin = its attach point: it snaps onto any socket of its slot type
with no offset, pointing forward (+z), hanging down (-y).
"""
import math
import random

from mathutils import Vector

from .geo import Builder, chamfer_ring, rot_x, rot_y, rot_z
from .palette import swatch as S


class Part:
    """One exported model: geometry + sockets (slot, index, Unity position)."""

    def __init__(self, name, kind, builder, slot=None, module_ids=(), sockets=()):
        self.name = name
        self.kind = kind  # "chassis" or "module"
        self.builder = builder
        self.slot = slot
        self.module_ids = list(module_ids)
        self.sockets = list(sockets)


# ---------------------------------------------------------------------------------------------------------------------
# Chassis
# ---------------------------------------------------------------------------------------------------------------------

def chassis(spec):
    """A chassis from its spec (see kits.py). Same spec + seed = same model."""
    rng = random.Random(spec["seed"])
    b = Builder()
    W, H = spec["width"], spec["height"]
    F = spec["front"]           # nose tip, metres ahead of the centre of mass
    boom = spec["boom"]
    tail_z = -2.6 - boom

    # Fuselage and tail boom: one loft of cut-corner sections.
    b.loft([
        chamfer_ring(F, 0.30 * W, 0.30 * H, 0.35, y=-0.12 * H),
        chamfer_ring(F - 0.6, 0.62 * W, 0.58 * H, 0.32, y=-0.08 * H),
        chamfer_ring(F - 1.6, 0.92 * W, 0.90 * H, 0.28, y=-0.02 * H, top_scale=0.8),
        chamfer_ring(0.6, W, H, 0.25, top_scale=0.85),
        chamfer_ring(-1.0, 0.96 * W, 0.95 * H, 0.25, y=0.02 * H, top_scale=0.85),
        chamfer_ring(-2.0, 0.60 * W, 0.60 * H, 0.30, y=0.15 * H),
        chamfer_ring(-2.6, 0.34 * W, 0.36 * H, 0.30, y=0.20 * H),
        chamfer_ring(-2.6 - boom * 0.5, 0.26 * W, 0.28 * H, 0.30, y=0.22 * H),
        chamfer_ring(tail_z, 0.17 * W, 0.22 * H, 0.30, y=0.26 * H),
    ], S("hull"), cap_front=S("hull_dark"), cap_back=S("hull_dark"))

    # Canopy: dark glass, half sunk into the nose.
    b.loft([
        chamfer_ring(F - 0.7, 0.45 * W, 0.25 * H, 0.45, y=0.10 * H, top_scale=0.7),
        chamfer_ring(F - 1.6, 0.80 * W, 0.40 * H, 0.45, y=0.36 * H, top_scale=0.7),
        chamfer_ring(0.9, 0.80 * W, 0.36 * H, 0.45, y=0.44 * H, top_scale=0.7),
        chamfer_ring(0.4, 0.50 * W, 0.16 * H, 0.45, y=0.46 * H, top_scale=0.7),
    ], S("glass"))
    # Canopy frame rib and nose light.
    b.box((0, 0.30 * H, F - 1.25), (0.82 * W, 0.08, 0.08), S("hull_dark"), rotation=rot_x(-35))
    b.box((0, -0.02 * H, F + 0.01), (0.12 * W, 0.05, 0.04), S("neon_cyan"))

    _engines(b, spec, W, H)
    top = 0.5 * H + 0.12 * H + 0.16 * H
    # Rotor mast with a fairing.
    b.box((0, 0.5 * H + 0.1, -0.1), (0.5 * W, 0.25, 1.1), S("hull_dark"))
    b.cylinder((0, 0.5 * H, 0), (0, top + 0.35, 0), 0.14, S("steel"), segments=10)
    rotor_y = top + 0.35

    pylon_points = _wings_or_stubs(b, spec, W, H)
    if spec.get("skids", True):
        _skids(b, F, W, H)
    _tail(b, W, H, tail_z)
    _greebles(b, rng, spec, W, H, F, boom, tail_z)

    sockets = _sockets(spec, W, H, F, rotor_y, pylon_points)
    return Part(f"Chassis_{spec['name']}", "chassis", b, sockets=sockets)


def _engines(b, spec, W, H):
    count = spec.get("engines", 2)
    xs = [0.0] if count == 1 else [0.30 * W, -0.30 * W]
    width = 0.55 * W if count == 1 else 0.36 * W
    y = 0.5 * H + 0.12 * H
    for x in xs:
        b.loft([
            chamfer_ring(0.5, width * 0.8, 0.26 * H, 0.35, y=y, x=x),
            chamfer_ring(0.2, width, 0.32 * H, 0.35, y=y, x=x),
            chamfer_ring(-1.5, width * 0.95, 0.30 * H, 0.35, y=y, x=x),
            chamfer_ring(-1.9, width * 0.75, 0.24 * H, 0.35, y=y, x=x),
        ], S("hull"), cap_front=S("rubber"), cap_back=S("exhaust"))
        # Intake grille slats.
        for i in range(3):
            b.box((x, y - 0.06 * H + i * 0.06 * H, 0.52), (width * 0.6, 0.02, 0.03), S("gunmetal"))


def _wings_or_stubs(b, spec, W, H):
    """Stub wings (pylons under the tips) or a small side mount per pylon. Pylon 0 is on the right (+x)."""
    pylons = spec["slots"].count("Pylon")
    points = []
    wing_y, wing_z = -0.05 * H, -0.2
    if spec.get("wings", False):
        span = spec["wing_span"]
        for side in (1, -1):
            b.box((side * (W / 2 + span / 2 - 0.1), wing_y, wing_z), (span + 0.2, 0.12, 1.0), S("hull"))
            b.box((side * (W / 2 + span), wing_y, wing_z), (0.06, 0.32, 1.05), S("hazard"))
            b.box((side * (W / 2 + span + 0.04), wing_y + 0.1, wing_z + 0.45), (0.04, 0.05, 0.08),
                  S("neon_green" if side > 0 else "neon_pink"))
            # Pylon hardpoint under the tip.
            b.box((side * (W / 2 + span * 0.7), wing_y - 0.1, wing_z), (0.1, 0.1, 0.6), S("gunmetal"))
        for i in range(pylons):
            side = 1 if i % 2 == 0 else -1
            points.append((side * (W / 2 + spec["wing_span"] * 0.7), wing_y - 0.15, wing_z))
    else:
        for i in range(pylons):
            side = 1 if i % 2 == 0 else -1
            b.box((side * (W / 2 + 0.22), -0.15 * H, wing_z), (0.5, 0.12, 0.6), S("hull_dark"))
            b.box((side * (W / 2 + 0.38), -0.15 * H - 0.1, wing_z), (0.1, 0.1, 0.5), S("gunmetal"))
            points.append((side * (W / 2 + 0.38), -0.15 * H - 0.15, wing_z))
    return points


def _skids(b, F, W, H):
    y = -H / 2 - 0.55
    for side in (1, -1):
        x = side * 0.55 * W
        b.cylinder((x, y, -1.8), (x, y, F - 1.3), 0.07, S("gunmetal"), segments=8)
        b.cylinder((x, y, F - 1.3), (x, y + 0.25, F - 0.95), 0.07, S("gunmetal"), segments=8)
        for z in (0.8, -1.2):
            b.cylinder((side * 0.32 * W, -H / 2 + 0.15, z), (x, y, z), 0.05, S("gunmetal"), segments=6)


def _tail(b, W, H, tail_z):
    base_y = 0.26 * H
    # Fin (convex outline in the z, y plane) with a hazard tip.
    b.fin([(tail_z + 0.9, base_y), (tail_z - 0.25, base_y), (tail_z - 0.5, base_y + 1.35), (tail_z + 0.05, base_y + 1.3)],
          0.0, 0.1, S("hull"))
    b.box((0, base_y + 1.33, tail_z - 0.22), (0.12, 0.06, 0.55), S("hazard"))
    # Stabiliser.
    b.box((0, base_y - 0.02, tail_z + 0.75), (1.9, 0.07, 0.5), S("hull"))
    for side in (1, -1):
        b.box((side * 0.95, base_y + 0.08, tail_z + 0.72), (0.05, 0.3, 0.45), S("hull_dark"))
    # Ducted tail rotor (fenestron) on the fin.
    shroud_y, shroud_z = base_y + 0.55, tail_z - 0.05
    b.cylinder((-0.13, shroud_y, shroud_z), (0.13, shroud_y, shroud_z), 0.55, S("hull_dark"), segments=16,
               cap_start=S("rubber"), cap_end=S("rubber"))
    b.cylinder((-0.16, shroud_y, shroud_z), (0.16, shroud_y, shroud_z), 0.1, S("steel"), segments=8)


def _greebles(b, rng, spec, W, H, F, boom, tail_z):
    """Seeded details, mirrored left/right so they read as designed, not random."""
    # Side vents.
    for side in (1, -1):
        for i in range(4):
            b.box((side * (W / 2 + 0.01), 0.05 * H + i * 0.09, -0.4), (0.05, 0.04, 0.6), S("hull_dark"))
    # Armour plates.
    plates = round(spec.get("armour", 0.5) * 6)
    for _ in range(plates):
        y = rng.uniform(-0.3, 0.15) * H
        z = rng.uniform(-1.6, F - 2.2)
        size = (0.05, rng.uniform(0.25, 0.45), rng.uniform(0.6, 1.1))
        for side in (1, -1):
            b.box((side * (W / 2 + 0.02), y, z), size, S("hull_light"))
    # Neon strip along each side: the cyberpunk accent.
    strip_back, strip_front = -1.8, F - 1.8
    for side in (1, -1):
        b.box((side * (W / 2 + 0.025), -0.2 * H, (strip_back + strip_front) / 2), (0.02, 0.04, strip_front - strip_back),
              S("neon_green"))
    # Belly plate.
    b.box((0, -H / 2 - 0.02, -0.3), (0.6 * W, 0.06, 2.4), S("hull_dark"))
    # Antennas on the boom.
    for _ in range(rng.randint(1, 3)):
        z = rng.uniform(tail_z + 1.0, -2.8)
        height = rng.uniform(0.4, 0.8)
        y = 0.26 * H + 0.1
        b.cylinder((0, y, z), (0, y + height, z - 0.15), 0.015, S("steel"), segments=5)
    # Sensor ball under the nose (scouts).
    if spec.get("sensor", False):
        b.ico((0, -0.32 * H, F - 0.75), 0.22, S("gunmetal"), subdivisions=1)
        b.box((0, -0.32 * H, F - 0.54), (0.12, 0.08, 0.04), S("neon_cyan"))
    # Hazard bands on the boom.
    for z in (-3.0, -3.3):
        if z > tail_z + 1.2:
            b.box((0, 0.22 * H, z), (0.27 * W, 0.29 * H, 0.08), S("hazard"))


def _sockets(spec, W, H, F, rotor_y, pylon_points):
    """One socket per slot in the chassis' slot list, indexed per type (game order)."""
    positions = {
        "Nose": [(0, -0.30 * H, F - 0.9)],
        "Pylon": pylon_points,
        "Turret": [(0, -H / 2 - 0.05, 0.9)],
        "Utility": [(0.30 * W, 0.08 * H, -2.0), (-0.30 * W, 0.08 * H, -2.0)],
        "Winch": [(0, -H / 2 - 0.05, -0.3)],
        "Rotor": [(0, rotor_y, 0)],
        "Engine": [(0, 0.5 * H + 0.12 * H, -1.95)],
        "Armour": [(0, 0, 0)],
    }
    sockets, counts = [], {}
    for slot in spec["slots"]:
        index = counts.get(slot, 0)
        counts[slot] = index + 1
        options = positions[slot]
        sockets.append((slot, index, tuple(round(v, 4) for v in options[min(index, len(options) - 1)])))
    return sockets


# ---------------------------------------------------------------------------------------------------------------------
# Modules (origin = attach point)
# ---------------------------------------------------------------------------------------------------------------------

def _mount(b, length=0.5):
    """Short hardpoint block under the socket that every hanging module shares."""
    b.box((0, -0.08, 0), (0.12, 0.16, length), S("gunmetal"))


def rotor(name, ids, blades, length, chord, tip):
    b = Builder()
    b.cylinder((0, 0, 0), (0, 0.3, 0), 0.24, S("steel"), segments=12)
    b.cylinder((0, 0.3, 0), (0, 0.4, 0), 0.13, S("gunmetal"), segments=10)
    for k in range(blades):
        angle = 360.0 * k / blades
        turn = rot_y(angle)
        pitch = turn @ rot_x(7)
        centre = turn @ Vector((0.22 + length / 2, 0.16, 0))
        b.box(tuple(centre), (length, 0.05, chord), S("carbon"), rotation=pitch)
        tip_centre = turn @ Vector((0.22 + length - 0.15, 0.16, 0))
        b.box(tuple(tip_centre), (0.3, 0.055, chord * 1.02), S(tip), rotation=pitch)
        grip = turn @ Vector((0.32, 0.16, 0))
        b.box(tuple(grip), (0.3, 0.12, chord * 0.8), S("gunmetal"), rotation=turn)
    return Part(name, "module", b, slot="Rotor", module_ids=ids)


def minigun():
    b = Builder()
    _mount(b, 0.4)
    y = -0.3
    b.cylinder((0, y, -0.2), (0, y, 0.45), 0.14, S("gunmetal"), segments=10)
    for k in range(6):
        t = 2 * math.pi * k / 6
        ox, oy = 0.07 * math.cos(t), 0.07 * math.sin(t)
        b.cylinder((ox, y + oy, 0.4), (ox, y + oy, 1.3), 0.024, S("steel"), segments=6)
    b.cylinder((0, y, 1.12), (0, y, 1.2), 0.12, S("gunmetal"), segments=10)
    b.box((0.2, y, -0.02), (0.16, 0.2, 0.35), S("hull_dark"))
    return Part("Gun_Minigun", "module", b, slot="Nose", module_ids=[101])


def autocannon():
    b = Builder()
    _mount(b, 0.5)
    y = -0.3
    b.box((0, y, 0.15), (0.26, 0.26, 0.8), S("gunmetal"))
    b.cylinder((0, y, 0.55), (0, y, 1.0), 0.08, S("gunmetal"), segments=10)
    b.cylinder((0, y, 0.55), (0, y, 2.0), 0.05, S("steel"), segments=10)
    b.box((0, y, 2.05), (0.15, 0.1, 0.18), S("gunmetal"))
    b.box((-0.19, y + 0.02, 0.1), (0.12, 0.18, 0.4), S("hazard"))
    return Part("Gun_Autocannon", "module", b, slot="Nose", module_ids=[102])


def pulse_laser():
    b = Builder()
    _mount(b, 0.5)
    y = -0.3
    b.box((0, y, 0.15), (0.3, 0.24, 0.7), S("hull_dark"))
    b.cylinder((0, y, 0.45), (0, y, 1.7), 0.06, S("gunmetal"), segments=10)
    for z in (0.7, 0.95, 1.2, 1.45):
        b.cylinder((0, y, z), (0, y, z + 0.05), 0.1, S("neon_cyan"), segments=10)
    b.cylinder((0, y, 1.7), (0, y, 1.78), 0.08, S("neon_cyan"), segments=10)
    return Part("Gun_PulseLaser", "module", b, slot="Nose", module_ids=[103])


def rocket_pod():
    b = Builder()
    _mount(b, 0.7)
    y = -0.48
    b.cylinder((0, y, -0.75), (0, y, 0.75), 0.3, S("hull"), segments=12, cap_start=S("hull_dark"), cap_end=S("rubber"))
    b.cylinder((0, y, 0.42), (0, y, 0.52), 0.305, S("hazard"), segments=12)
    rims = [(0.0, 0.0)] + [(0.17 * math.cos(t), 0.17 * math.sin(t)) for t in (2 * math.pi * k / 6 for k in range(6))]
    for x, oy in rims:
        b.cylinder((x, y + oy, 0.74), (x, y + oy, 0.8), 0.065, S("gunmetal"), segments=8, cap_end=S("rubber"))
    return Part("Pod_Rockets", "module", b, slot="Pylon", module_ids=[111])


def swarm_pod():
    b = Builder()
    _mount(b, 0.7)
    y = -0.5
    b.box((0, y, 0), (0.5, 0.42, 1.3), S("hull"), swatches={"front": S("rubber")})
    for row in range(3):
        for col in range(4):
            x = -0.165 + col * 0.11
            yy = y - 0.12 + row * 0.12
            b.cylinder((x, yy, 0.64), (x, yy, 0.69), 0.04, S("gunmetal"), segments=6, cap_end=S("rubber"))
    for side in (1, -1):
        b.box((side * 0.255, y + 0.1, 0), (0.02, 0.03, 1.1), S("neon_pink"))
    return Part("Pod_Swarm", "module", b, slot="Pylon", module_ids=[117])


def _missile(b, centre, length, radius, band="warhead"):
    x, y, z = centre
    tail, nose = z - length / 2, z + length / 2
    b.cylinder((x, y, tail), (x, y, nose - radius * 3), radius, S("missile"), segments=10, cap_start=S("exhaust"))
    b.cylinder((x, y, nose - radius * 3), (x, y, nose), radius, S("missile"), segments=10, end_radius=0.004)
    b.cylinder((x, y, nose - radius * 4), (x, y, nose - radius * 3), radius * 1.01, S(band), segments=10)
    for k in range(4):
        turn = rot_z(45 + 90 * k)
        offset = turn @ Vector((0, radius + 0.08, 0))
        b.box((x + offset.x, y + offset.y, tail + 0.15), (0.02, 0.16, 0.25), S("hull_dark"), rotation=turn)


def missile_rack(name, ids, count, length, radius, band="warhead"):
    b = Builder()
    _mount(b, 0.6)
    b.box((0, -0.2, 0), (0.1, 0.08, length * 0.8), S("gunmetal"))
    xs = [0.0] if count == 1 else [-(radius + 0.04), radius + 0.04]
    for x in xs:
        _missile(b, (x, -0.3 - radius, 0.1), length, radius, band)
    return Part(name, "module", b, slot="Pylon", module_ids=ids)


def bomb_rack():
    b = Builder()
    _mount(b, 0.6)
    for x in (-0.16, 0.16):
        y = -0.42
        b.cylinder((x, y, -0.55), (x, y, 0.35), 0.14, S("hull_dark"), segments=10)
        b.cylinder((x, y, 0.35), (x, y, 0.6), 0.14, S("hull_dark"), segments=10, end_radius=0.05)
        b.cylinder((x, y, 0.2), (x, y, 0.28), 0.142, S("hazard"), segments=10)
        for k in range(4):
            turn = rot_z(45 + 90 * k)
            offset = turn @ Vector((0, 0.18, 0))
            b.box((x + offset.x, y + offset.y, -0.6), (0.015, 0.14, 0.2), S("gunmetal"), rotation=turn)
    b.box((0, -0.22, 0), (0.5, 0.06, 0.7), S("gunmetal"))
    return Part("Rack_Bombs", "module", b, slot="Pylon", module_ids=[113])


def dispenser(name, ids, accent, rows=2, cols=4):
    """Utility box (flares, smoke, mines): tubes on the back face, an accent light."""
    b = Builder()
    b.box((0, -0.12, 0), (0.36, 0.24, 0.5), S("hull_dark"))
    for row in range(rows):
        for col in range(cols):
            x = -0.12 + col * (0.24 / max(cols - 1, 1))
            y = -0.17 + row * 0.1
            b.box((x, y, -0.26), (0.05, 0.05, 0.03), S("rubber"))
    b.box((0, -0.01, 0.1), (0.3, 0.02, 0.06), S(accent))
    return Part(name, "module", b, slot="Utility", module_ids=ids)


def repair_drone():
    b = Builder()
    b.box((0, -0.06, 0), (0.5, 0.12, 0.5), S("hull_dark"))
    # A small quad drone parked on its dock.
    b.box((0, -0.2, 0), (0.18, 0.08, 0.18), S("hull_light"))
    for k in range(4):
        turn = rot_y(45 + 90 * k)
        arm = turn @ Vector((0.17, -0.2, 0))
        b.box(tuple(arm), (0.2, 0.03, 0.03), S("gunmetal"), rotation=turn)
        b.cylinder((arm.x * 1.5, -0.17, arm.z * 1.5), (arm.x * 1.5, -0.16, arm.z * 1.5), 0.08, S("carbon"), segments=8)
    b.box((0, -0.25, 0.09), (0.06, 0.03, 0.02), S("neon_green"))
    return Part("Utility_RepairDrone", "module", b, slot="Utility", module_ids=[133])


def emitter(name, ids, glow):
    """EMP coil or shield projector: a dish with glowing rings."""
    b = Builder()
    b.box((0, -0.08, 0), (0.3, 0.16, 0.3), S("hull_dark"))
    b.cylinder((0, -0.16, 0), (0, -0.3, 0), 0.22, S("gunmetal"), segments=12, end_radius=0.3)
    for y in (-0.34, -0.42):
        b.cylinder((0, y, 0), (0, y - 0.03, 0), 0.24, S(glow), segments=12)
    b.ico((0, -0.5, 0), 0.08, S(glow), subdivisions=1)
    return Part(name, "module", b, slot="Utility", module_ids=ids)


def flak_turret():
    b = Builder()
    b.cylinder((0, 0, 0), (0, -0.18, 0), 0.36, S("gunmetal"), segments=12)
    b.ico((0, -0.45, 0), 0.32, S("hull"), subdivisions=1)
    for x in (-0.1, 0.1):
        b.cylinder((x, -0.45, 0.2), (x, -0.45, 1.2), 0.04, S("steel"), segments=8)
        b.cylinder((x, -0.45, 1.05), (x, -0.45, 1.2), 0.06, S("gunmetal"), segments=8)
    b.box((0, -0.3, 0.29), (0.1, 0.06, 0.04), S("neon_cyan"))
    return Part("Turret_Flak", "module", b, slot="Turret", module_ids=[121])


def winch():
    b = Builder()
    b.box((0, -0.15, 0), (0.6, 0.3, 0.6), S("hull_dark"))
    b.cylinder((-0.25, -0.45, 0), (0.25, -0.45, 0), 0.18, S("gunmetal"), segments=12)
    b.cylinder((0, -0.6, 0), (0, -1.4, 0), 0.015, S("steel"), segments=5)
    b.box((0, -1.45, 0), (0.18, 0.12, 0.06), S("hazard"))
    return Part("Winch_Heavy", "module", b, slot="Winch", module_ids=[141])


def engine(name, ids, nozzles, glow, radius):
    """Exhaust stack that sits on the chassis' engine socket (the deck at the back of the engines)."""
    b = Builder()
    xs = [0.0] if nozzles == 1 else [(-0.28 + 0.56 * i / (nozzles - 1)) for i in range(nozzles)]
    for x in xs:
        b.cylinder((x, 0, 0.2), (x, 0, -0.45), radius, S("gunmetal"), segments=10, end_radius=radius * 0.8,
                   cap_end=S(glow))
        b.cylinder((x, 0, -0.1), (x, 0, -0.18), radius * 1.05, S("hull_dark"), segments=10)
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
        rotor("Rotor_HeavyLift", [153], blades=3, length=5.8, chord=0.48, tip="hazard"),
        engine("Engine_Standard", [161], 2, "exhaust", 0.13),
        engine("Engine_Boost", [162], 2, "neon_pink", 0.15),
        engine("Engine_Turbine", [163], 1, "exhaust", 0.22),
    ]
