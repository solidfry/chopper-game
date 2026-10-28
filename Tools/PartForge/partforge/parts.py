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
# Style colours
# ---------------------------------------------------------------------------------------------------------------------

_SWAPS = {}


def use_style(style):
    """Colour swaps for the whole build: glow accents and the hazard paint (see styles.py)."""
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
    _details(b, rng, spec, style, W, H, F, tail_z)
    if style["cage"] > 0 and not heavy:  # the heavy body is its own armour
        if attack or assault:
            _attack_cage(b, W, H)
        else:
            _cage(b, style, spec, W, H, boom)
    if style["conduits"] > 0:
        side_x = (0.33 * W + 0.04) if attack or assault else (W / 2 + 0.07)
        _conduits(b, W, H, F, tail_z, side_x=side_x,
                  y_low=0.10 * H if attack or assault else (0.30 * H if heavy else 0.26 * H))
    if style["asymmetry"] > 0:
        _retrofits(b, rng, style, W, H, F)
    if style["wear"] > 0:
        _wear(b, rng, style, W, H, F)

    sockets = _sockets(spec, W, H, F, rotors, pylon_points, body)
    return Part(f"Chassis_{spec['name']}", "chassis", b, sockets=sockets)


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
        for i in range(3):
            b.box((side * (W / 2 - 0.01), -0.02 * H - i * 0.08, -0.9), (0.04, 0.035, 0.5), C("hull_dark"))

    _tandem_cockpit(b, style, cw, H, F)

    if grille:
        # Beastly grille snout instead of the sensor turret; a small sensor ball moves under the chin, to the side.
        _grille_snout(b, style, cw, H, F + 0.45, scale=0.95)
        b.ico((0.3 * cw, -0.42 * H, F - 0.6), 0.16, C("gunmetal"), subdivisions=1)
        b.box((0.3 * cw, -0.42 * H, F - 0.44), (0.08, 0.06, 0.03), C(style["lights"]["sensor"]))
    else:
        _sensor_turret(b, style, cw, H, F)

    # Engine nacelles at the shoulders, with exhaust deflectors angled out.
    y = 0.36 * H
    nacelle_w = 0.26 * W
    for side in (1, -1):
        x = side * (cw / 2 + nacelle_w * 0.35)
        b.loft([
            ring(0.4, nacelle_w * 0.75, 0.26 * H, 0.35, y=y, x=x),
            ring(0.1, nacelle_w, 0.34 * H, 0.3, y=y, x=x),
            ring(-1.4, nacelle_w * 0.95, 0.32 * H, 0.3, y=y, x=x),
            ring(-1.8, nacelle_w * 0.8, 0.26 * H, 0.3, y=y, x=x),
        ], C("hull"), cap_front=C("rubber"), cap_back=C("exhaust"))
        b.box((x + side * 0.1, y, -2.05), (nacelle_w * 0.7, 0.24 * H, 0.45), C("gunmetal"), rotation=rot_y(side * 25))
        if style["wear"] > 0:
            b.box((x + side * 0.05, y - 0.05, -2.5), (nacelle_w * 0.5, 0.03, 0.6), C("soot"))

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
        b.box((x + side * 0.1, 0.36 * H, -2.05), (0.2 * W, 0.26 * H, 0.4), C("gunmetal"), rotation=rot_y(side * 25))
        if style["wear"] > 0:
            b.box((x + side * 0.05, 0.3 * H, -2.5), (0.15 * W, 0.03, 0.6), C("soot"))

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

    # Cockpit: faceted glazing with frames (or an armoured visor with slits), chin sensor, probe, floodlights.
    b.loft([chamfer_ring(F + 0.02, 0.50 * W, 0.18 * H, 0.45, y=0.06 * H, top_scale=0.7),
            chamfer_ring(F - 0.55, 0.80 * W, 0.34 * H, 0.40, y=0.20 * H, top_scale=0.75),
            chamfer_ring(F - 1.25, 0.86 * W, 0.30 * H, 0.40, y=0.30 * H, top_scale=0.75)],
           C("hull_dark") if visor else C("glass"))
    for x in (-0.2 * W, 0.0, 0.2 * W):
        b.box((x, 0.18 * H, F - 0.3), (0.05, 0.3 * H, 0.05), C("hull_dark"), rotation=rot_x(-40))
    b.box((0, 0.27 * H, F - 0.7), (0.84 * W, 0.05, 0.05), C("hull_dark"))
    if visor:
        for y, z, width in ((0.12 * H, F - 0.15, 0.4 * W), (0.3 * H, F - 0.75, 0.55 * W)):
            b.box((0, y, z), (width, 0.05, 0.07), C("glass"), rotation=rot_x(40))
    b.ico((0.22 * W, -0.36 * H, F - 0.45), 0.28, C("gunmetal"), subdivisions=1)
    b.box((0.22 * W, -0.36 * H, F - 0.17), (0.14, 0.1, 0.04), C(lights["sensor"]))
    b.cylinder((0.36 * W, -0.08 * H, F - 0.4), (0.36 * W, -0.08 * H, F + 1.4), 0.05, C("gunmetal"), segments=6)
    b.cylinder((0.36 * W, -0.08 * H, F + 1.4), (0.36 * W, -0.08 * H, F + 1.6), 0.08, C("hazard"), segments=8,
               end_radius=0.03)
    for i in range(4):
        b.box(((i - 1.5) * 0.12 * W, -0.38 * H, F - 0.05), (0.1, 0.06, 0.05), C(lights["nose"]))

    # Cabin windows (a few lit), door outline, ladder.
    z = F - 2.5
    while z > -2.3:
        for side in (1, -1):
            lit = rng.random() < 0.3
            b.box((side * (W / 2 + 0.01), 0.14 * H, z), (0.03, 0.12 * H, 0.32), C("sodium" if lit else "glass"))
        z -= 0.85
    b.box((W / 2 + 0.012, -0.05 * H, F - 2.0), (0.02, 0.6 * H, 0.04), C("hull_dark"))
    b.box((W / 2 + 0.012, -0.05 * H, F - 2.95), (0.02, 0.6 * H, 0.04), C("hull_dark"))
    for i in range(5):
        b.box((-(W / 2 + 0.05), -0.35 * H + i * 0.13 * H, F - 2.3), (0.04, 0.03, 0.4), C("gunmetal"))

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

    # Engine pods on the upper sides: glowing intake ring, dot-grid vents, chevrons, exhaust.
    ey = top + 0.32
    for side in (1, -1):
        x = side * (W / 2 - 0.05)
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
        if style["wear"] > 0:
            b.box((x, top + 0.02, -2.6), (0.6, 0.03, 0.8), C("soot"))
        # Pipes from the engines towards the tail.
        b.cylinder((side * 0.3 * W, top + 0.12, -1.8), (side * 0.12 * W, 0.40 * H, -3.6), 0.05, C("gunmetal"), segments=6)

    # Top: cage bars, antenna array, dish.
    for side in (1, -1):
        b.cylinder((side * 0.2 * W, top + 0.55, 1.3), (side * 0.2 * W, top + 0.55, -2.0), 0.03, C("steel"), segments=5)
        for z in (1.3, -0.4, -2.0):
            b.cylinder((side * 0.2 * W, top + 0.02, z), (side * 0.2 * W, top + 0.55, z), 0.03, C("steel"), segments=5)
    for z in (1.3, -2.0):
        b.cylinder((0.2 * W, top + 0.55, z), (-0.2 * W, top + 0.55, z), 0.03, C("steel"), segments=5)
    for _ in range(rng.randint(3, 5)):
        x, z = rng.uniform(-0.3, 0.3) * W, rng.uniform(F - 2.0, F - 1.2)
        b.cylinder((x, top - 0.02, z), (x, top + rng.uniform(0.5, 1.0), z - 0.1), 0.015, C("steel"), segments=5)
    b.cylinder((-0.25 * W, top + 0.02, -3.0), (-0.25 * W, top + 0.25, -3.0), 0.05, C("gunmetal"), segments=6)
    b.cylinder((-0.25 * W, top + 0.25, -3.0), (-0.25 * W, top + 0.35, -2.95), 0.32, C("hull_light"), segments=12,
               end_radius=0.08)

    # Belly lift frame with four hanging cables and hooks.
    fy = -top - 0.1
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
        for i in range(4):
            b.box((0, top + 0.9 + i * 0.18, rear_z - 0.68 + i * 0.07), (0.32 * W, 0.04, 0.03), C("hull_dark"))
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
        b.box((x - side * 0.15, y, -0.3), (0.4, 0.12, 0.6), C("hull_dark"))
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
    b.box((0, 0.44 * H, F - 1.88), (0.72 * cw, 0.34 * H, 0.06), C("hull_dark"))
    b.box((0, 0.73 * H, F - 2.45), (0.06, 0.04, 1.2), C("hull_dark"))
    b.box((0, 0.45 * H, F - 1.25), (0.06, 0.04, 1.0), C("hull_dark"))
    if visor:
        for z, y, width in ((F - 1.0, 0.36 * H, 0.42 * cw), (F - 2.2, 0.66 * H, 0.46 * cw)):
            b.box((0, y, z), (width, 0.05, 0.07), C("glass"), rotation=rot_x(30))
        for side in (1, -1):
            b.box((side * 0.37 * cw, 0.38 * H, F - 1.5), (0.06, 0.06, 0.6), C("glass"))
            b.box((side * 0.37 * cw, 0.58 * H, F - 2.6), (0.06, 0.06, 0.8), C("glass"))


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


def _attack_cage(b, W, H):
    """Slat armour on the outer side of each engine nacelle."""
    y = 0.36 * H
    x_out = 0.31 * W + 0.26 * W * 0.35 + 0.26 * W / 2 + 0.12
    for side in (1, -1):
        x = side * x_out
        z = 0.2
        while z > -1.7:
            b.box((x, y, z), (0.03, 0.34 * H, 0.03), C("gunmetal"))
            z -= 0.26
        for dy in (-0.15 * H, 0.15 * H):
            b.box((x, y + dy, -0.75), (0.03, 0.03, 2.0), C("gunmetal"))
        for z in (0.1, -1.5):
            b.box((x - side * 0.06, y, z), (0.12, 0.03, 0.03), C("gunmetal"))


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
    b.box((0, back_top[1] + 0.03, back_top[0] + 0.05), (0.62 * W, 0.05, 0.3), C("hull"), rotation=rot_x(slope * 0.5))


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
            # Exhaust shroud and soot on the deck behind it.
            b.box((x, y + 0.02, -2.05), (width * 0.85, 0.30 * H, 0.25), C("gunmetal"))
            b.box((x, 0.40 * H, -2.45), (width * 0.7, 0.03, 0.7), C("soot"))


def _wings_or_stubs(b, spec, style, W, H):
    """Stub wings (pylons under the tips) or a small side mount per pylon. Pylon 0 is on the right (+x)."""
    pylons = spec["slots"].count("Pylon")
    points = []
    wing_y, wing_z = -0.05 * H, -0.2
    lights = style["lights"]
    if spec.get("wings", False):
        span = spec["wing_span"]
        thickness = 0.12 if style["facet"] <= 1.0 else 0.18
        for side in (1, -1):
            b.box((side * (W / 2 + span / 2 - 0.1), wing_y, wing_z), (span + 0.2, thickness, 1.0), C("hull"))
            b.box((side * (W / 2 + span), wing_y - 0.04, wing_z), (0.06, 0.36, 1.05), C("hazard"))
            b.box((side * (W / 2 + span + 0.04), wing_y + 0.1, wing_z + 0.45), (0.04, 0.05, 0.08),
                  C(lights["right"] if side > 0 else lights["left"]))
            # Pylon hardpoint under the tip.
            b.box((side * (W / 2 + span * 0.7), wing_y - 0.1, wing_z), (0.1, 0.1, 0.6), C("gunmetal"))
        for i in range(pylons):
            side = 1 if i % 2 == 0 else -1
            points.append((side * (W / 2 + spec["wing_span"] * 0.7), wing_y - 0.15, wing_z))
    else:
        for i in range(pylons):
            side = 1 if i % 2 == 0 else -1
            b.box((side * (W / 2 + 0.22), -0.15 * H, wing_z), (0.5, 0.12, 0.6), C("hull_dark"))
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
            b.box((x - side * 0.12, y + 0.12, 0.2), (0.3, 0.03, 0.4), C("bare"))
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
    # Stabiliser.
    b.box((0, base_y - 0.02, tail_z + 0.75), (1.9, 0.07, 0.5), C("hull"))
    for side in (1, -1):
        b.box((side * 0.95, base_y + 0.08, tail_z + 0.72), (0.05, 0.3, 0.45), C("hull_dark"))
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


def _details(b, rng, spec, style, W, H, F, tail_z):
    """Seeded details, mirrored left/right so they read as designed."""
    # Side vents.
    for side in (1, -1):
        for i in range(4):
            b.box((side * (W / 2 + 0.01), 0.05 * H + i * 0.09, -0.4), (0.05, 0.04, 0.6), C("hull_dark"))
    # Armour plates (two layers in heavy styles).
    plates = round(spec.get("armour", 0.5) * 6)
    for n in range(plates):
        y = rng.uniform(-0.3, 0.15) * H
        z = rng.uniform(-1.6, F - 2.2)
        size = (0.05, rng.uniform(0.25, 0.45), rng.uniform(0.6, 1.1))
        swatch = "hull_light" if style["wear"] <= 0 or n % 3 else "concrete"
        for side in (1, -1):
            b.box((side * (W / 2 + 0.02), y, z), size, C(swatch))
            if style["armour_layers"] > 1:
                b.box((side * (W / 2 + 0.07), y + 0.03, z - 0.05), (0.05, size[1] * 0.7, size[2] * 0.75), C("hull"))
    if style["neon_strips"]:
        strip_back, strip_front = -1.8, F - 1.8
        for side in (1, -1):
            b.box((side * (W / 2 + 0.025), -0.2 * H, (strip_back + strip_front) / 2),
                  (0.02, 0.04, strip_front - strip_back), C("neon_green"))
    else:
        # Belly beacon and small position lights instead of strips.
        b.box((0, -H / 2 - 0.07, -0.9), (0.12, 0.06, 0.12), C(style["lights"]["right"]))
    # Belly plate.
    b.box((0, -H / 2 - 0.02, -0.3), (0.6 * W, 0.06, 2.4), C("hull_dark"))
    # Antennas on the boom (and a small dish in cluttered styles).
    low, high = style["antennas"]
    for i in range(rng.randint(low, high)):
        z = rng.uniform(tail_z + 1.0, -2.8)
        height = rng.uniform(0.4, 0.8)
        x = rng.uniform(-0.08, 0.08) * (1 if style["asymmetry"] > 0 else 0)
        y = 0.26 * H + 0.12
        b.cylinder((x, y, z), (x, y + height, z - 0.15), 0.015, C("steel"), segments=5)
    if high >= 4:
        dish_z = tail_z + 1.6
        b.cylinder((0, 0.40 * H, dish_z), (0, 0.40 * H + 0.12, dish_z), 0.05, C("gunmetal"), segments=6)
        b.cylinder((0, 0.40 * H + 0.12, dish_z), (0, 0.40 * H + 0.2, dish_z + 0.05), 0.2, C("hull_light"),
                   segments=10, end_radius=0.06)
    # Sensor ball under the nose.
    if spec.get("sensor", False):
        b.ico((0, -0.32 * H, F - 0.75), 0.22, C("gunmetal"), subdivisions=1)
        b.box((0, -0.32 * H, F - 0.54), (0.12, 0.08, 0.04), C(style["lights"]["sensor"]))
    # Warning bands on the boom.
    for z in (-3.0, -3.3):
        if z > tail_z + 1.2:
            b.box((0, 0.22 * H, z), (0.27 * W + 0.02, 0.30 * H + 0.02, 0.08), C("hazard"))


def _cage(b, style, spec, W, H, boom):
    """Slat armour: bars on stand-offs round the engines and the front of the boom (stops rockets early)."""
    count = spec.get("engines", 2)
    width = 0.55 * W if count == 1 else 0.36 * W
    y = 0.5 * H + 0.12 * H
    outer = (0.30 * W if count > 1 else 0.0) + width / 2 + 0.14
    for side in (1, -1):
        x = side * outer
        z = 0.3
        while z > -1.8:
            b.box((x, y, z), (0.03, 0.34 * H, 0.03), C("gunmetal"))
            z -= 0.26
        for dy in (-0.17 * H, 0.17 * H):
            b.box((x, y + dy, -0.75), (0.03, 0.03, 2.2), C("gunmetal"))
        for z in (0.2, -1.6):
            b.box((x - side * 0.07, y, z), (0.14, 0.03, 0.03), C("gunmetal"))
    # Boom cage.
    for side in (1, -1):
        x = side * (0.17 * W + 0.12)
        for i in range(6):
            b.box((x, 0.22 * H, -2.8 - i * 0.28), (0.03, 0.36 * H, 0.03), C("gunmetal"))
        for dy in (0.05 * H, 0.38 * H):
            b.box((x, dy, -2.8 - 0.7), (0.03, 0.03, 1.5), C("gunmetal"))


def _conduits(b, W, H, F, tail_z, side_x, y_low):
    """External pipes and a cable bundle: retrofitted, function over form."""
    for side in (1, -1):
        x = side * side_x
        for y, radius, swatch in ((y_low, 0.035, "gunmetal"), (y_low + 0.07 * H, 0.025, "rubber")):
            b.cylinder((x, y, F - 2.0), (x, y, -1.5), radius, C(swatch), segments=6)
            for z in (F - 2.0, -1.5):
                b.cylinder((x, y, z), (x - side * 0.12, y, z), radius, C(swatch), segments=6)
        z = F - 2.2
        while z > -1.5:
            b.box((x, y_low + 0.035 * H, z), (0.1, 0.16, 0.06), C("hull_dark"))
            z -= 0.7
    b.cylinder((0.09, 0.40 * H, -2.4), (0.06, 0.38 * H, tail_z + 1.1), 0.03, C("rubber"), segments=6)


def _retrofits(b, rng, style, W, H, F):
    """One-sided add-ons, picked by the seed: the machine was fixed and extended in the field."""
    options = ["searchlight", "reel", "ammo", "sensor_mast"]
    rng.shuffle(options)
    for name in options[:2 + round(style["asymmetry"])]:
        if name == "searchlight":
            b.box((W / 2 + 0.2, -0.18 * H, F - 1.9), (0.4, 0.08, 0.1), C("gunmetal"))
            b.cylinder((W / 2 + 0.42, -0.24 * H, F - 2.1), (W / 2 + 0.42, -0.24 * H, F - 1.75), 0.15, C("gunmetal"),
                       segments=10, cap_end=C(style["lights"]["left"]))
        elif name == "reel":
            b.box((-(W / 2 + 0.18), 0.02 * H, -1.1), (0.3, 0.34, 0.5), C("hull_dark"))
            b.cylinder((-(W / 2 + 0.36), 0.02 * H, -1.3), (-(W / 2 + 0.36), 0.02 * H, -0.9), 0.14, C("rust"), segments=10)
        elif name == "ammo":
            for i in range(2):
                b.box((-(W / 2 + 0.16), -0.3 * H, 0.2 - i * 0.42), (0.26, 0.3, 0.38), C("primer"))
        elif name == "sensor_mast":
            x = 0.3 * W
            b.cylinder((x, 0.62 * H + 0.16 * H, 0.35), (x, 0.62 * H + 0.75, 0.35), 0.03, C("steel"), segments=5)
            b.box((x, 0.62 * H + 0.8, 0.35), (0.22, 0.12, 0.16), C("hull_light"))
            b.box((x, 0.62 * H + 0.8, 0.44), (0.1, 0.05, 0.02), C(style["lights"]["sensor"]))


def _wear(b, rng, style, W, H, F):
    """Replacement panels in primer, rust patches and bare edges; not mirrored, so the machine looks used."""
    for _ in range(round(style["wear"] * 6)):
        side = rng.choice((1, -1))
        y = rng.uniform(-0.25, 0.25) * H
        z = rng.uniform(-1.5, F - 2.2)
        b.box((side * (W / 2 + 0.035), y, z), (0.03, rng.uniform(0.2, 0.4), rng.uniform(0.3, 0.7)),
              C(rng.choice(("primer", "primer", "rust", "concrete"))))
    # Scuffed bare metal on the nose and wing leading edges.
    b.box((0, -0.24 * H if style["nose"] == "chisel" else -0.12 * H, F - 0.03), (0.5 * W, 0.03, 0.05), C("bare"))


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
            "Utility": [(W / 2 - 0.05, -0.05 * H, -1.35), (-(W / 2 - 0.05), -0.05 * H, -1.35)],
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
    """Short hardpoint block under the socket that every hanging module shares."""
    b.box((0, -0.08, 0), (0.12, 0.16, length), C("gunmetal"))


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
    y = -0.5
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
    return Part(name, "module", b, slot="Pylon", module_ids=ids)


def bomb_rack():
    b = Builder()
    _mount(b, 0.6)
    for x in (-0.16, 0.16):
        y = -0.42
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
    b.box((0, -0.2, 0), (0.18, 0.08, 0.18), C("hull_light"))
    for k in range(4):
        turn = rot_y(45 + 90 * k)
        arm = turn @ Vector((0.17, -0.2, 0))
        b.box(tuple(arm), (0.2, 0.03, 0.03), C("gunmetal"), rotation=turn)
        b.cylinder((arm.x * 1.5, -0.17, arm.z * 1.5), (arm.x * 1.5, -0.16, arm.z * 1.5), 0.08, C("carbon"), segments=8)
    b.box((0, -0.25, 0.09), (0.06, 0.03, 0.02), C("neon_green"))
    return Part("Utility_RepairDrone", "module", b, slot="Utility", module_ids=[133])


def emitter(name, ids, glow):
    """EMP coil or shield projector: a dish with glowing rings."""
    b = Builder()
    b.box((0, -0.08, 0), (0.3, 0.16, 0.3), C("hull_dark"))
    b.cylinder((0, -0.16, 0), (0, -0.3, 0), 0.22, C("gunmetal"), segments=12, end_radius=0.3)
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
