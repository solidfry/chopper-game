"""
The four building types. Each takes a Building (kit.py) and fills its LOD meshes, colliders and markers from the spec
(specs.py) and the style (styles.py).
"""
import math

from . import kit
from .kit import ALL, L0, L01, L1, L2, Rect, faces


def build(b):
    {"tower": tower, "block": block, "slum": slum, "industrial": industrial}[b.type](b)
    return b


# -- tower -----------------------------------------------------------------------------------------------------------

def tower(b):
    """Podium with shop fronts, then tiers that step back; ribbon or grid facade; a crown on top."""
    spec, style, rng = b.spec, b.style, b.rng
    storey = style["storey"]
    plot = b.plot
    podium = spec["podium"]
    tiers = []
    for sw_, sd_, floors, ox, oz in spec["tiers"]:
        tiers.append((Rect.centred(plot.w * ox, plot.d * oz, plot.w * sw_, plot.d * sd_), floors))

    base = tiers[0][0]
    b.span(base.x0, base.x1, 0.0, podium, base.z0, base.z1, "wall", L01)
    sides = faces(base)
    for name in ("front", "left", "right"):
        kit.shopfront(b, sides[name], podium)
    kit.service_side(b, sides["back"], podium)

    y = podium
    facade = kit.ribbon if spec["facade"] == "ribbon" else kit.grid
    for index, (rect, floors) in enumerate(tiers):
        top = facade(b, rect, y, floors, storey)
        b.mass(rect, 0.0 if index == 0 else y, top, swatch="glass", top="tar")
        if index == 0:
            if rng.random() < style["neon"] + 0.3:
                kit.blade_sign(b, faces(rect)["front"], y + 1.0, min(14.0, top - y - 2.0))
            kit.pipes(b, faces(rect)["back"], y, top, count=2)
        upper = tiers[index + 1][0] if index + 1 < len(tiers) else None
        kit.parapet(b, rect, top, height=1.1)
        kit.obstruction_lights(b, rect, top + 1.1)
        blocked = [upper.inset(-1.0)] if upper else []
        if upper is None:
            blocked += _crown(b, rect, top, spec["crown"])
            zone = None if spec["crown"] == "pad" else kit.roof_zone(b, rect.inset(0.4), top, blocked)
            if zone:
                blocked.append(zone)
        kit.dress_roof(b, rect.inset(0.4), top, blocked, density=0.8)
        y = top


def _crown(b, rect, y, crown):
    """Top of a tower. Returns the rectangles it takes on the roof."""
    rng = b.rng
    if crown == "pad":
        size = min(rect.w, rect.d) - 4.0
        pad = kit.helipad(b, Rect.centred(rect.cx, rect.cz, size, size), y)
        return [pad.inset(-1.0)]
    if crown == "spire":
        base = Rect.centred(rect.cx, rect.cz, 4.0, 4.0)
        b.span(base.x0, base.x1, y, y + 3.0, base.z0, base.z1, "wall", ALL, swatches={"top": "trim"})
        height = rng.uniform(18.0, 26.0)
        b.cylinder((rect.cx, y + 3.0, rect.cz), (rect.cx, y + 3.0 + height, rect.cz), 1.2, "steel", segments=8,
                   at=L01, end_radius=0.12)
        b.collider(base.x0, base.x1, y, y + 3.0, base.z0, base.z1)
        for k in (0.35, 0.7):
            b.box((rect.cx, y + 3.0 + height * k, rect.cz + 1.2 * (1 - k) + 0.1), (0.3, 0.3, 0.2), "warning", L01)
        b.box((rect.cx, y + 3.1 + height, rect.cz), (0.35, 0.35, 0.35), "warning", L01)
        b.thin.append((rect.cx, rect.cz, y + 3.8 + height))
        return [base.inset(-1.5)]
    if crown == "antennas":
        taken = []
        for _ in range(rng.randint(3, 4)):
            x = rng.uniform(rect.x0 + 2.0, rect.x1 - 2.0)
            z = rng.uniform(rect.z0 + 2.0, rect.z1 - 2.0)
            kit.mast(b, x, z, y, rng.uniform(8.0, 18.0))
            taken.append(Rect.centred(x, z, 2.0, 2.0))
        return taken
    if crown == "sign":
        spot = Rect.centred(rect.cx, rect.cz - rect.d * 0.15, rect.w - 2.0, 2.0)
        kit.roof_sign(b, spot, y)
        return [spot.inset(-1.5)]
    return []


# -- block -----------------------------------------------------------------------------------------------------------

def block(b):
    """Mid-rise block: rect, L or U plan; shop fronts on the street, punched windows above, fire escape, balconies,
    water tanks or a landing pad on the roof."""
    spec, style, rng = b.spec, b.style, b.rng
    storey = style["storey"] * 0.9
    plot = b.plot.inset(1.0)
    floors = spec["floors"]
    ground = 4.6
    top = ground + (floors - 1) * storey
    shape = spec["shape"]
    wing = min(14.0, plot.d * 0.45)
    if shape == "rect":
        masses = [Rect(plot.x0, plot.x1, plot.z0 + 2.0, plot.z1)]
    elif shape == "L":
        masses = [Rect(plot.x0, plot.x1, plot.z1 - wing, plot.z1), Rect(plot.x0, plot.x0 + wing, plot.z0, plot.z1 - wing)]
    else:
        masses = [Rect(plot.x0, plot.x1, plot.z0, plot.z0 + wing),
                  Rect(plot.x0, plot.x0 + wing, plot.z0 + wing, plot.z1),
                  Rect(plot.x1 - wing, plot.x1, plot.z0 + wing, plot.z1)]

    def exposed(face, rect):
        """True when the middle of the side is not against another mass."""
        mid = face.point(face.length / 2, 0.0, 0.5)
        return not any(other is not rect and other.contains(mid[0], mid[2], 0.01) for other in masses)

    roof_blocked = {id(rect): [] for rect in masses}
    escape_done = False
    for rect in masses:
        b.span(rect.x0, rect.x1, 0.0, top, rect.z0, rect.z1, "wall", L01, swatches={"top": "tar"})
        b.mass(rect, 0.0, top)
        for name, face in faces(rect).items():
            if not exposed(face, rect):
                continue
            street = name in ("front", "left", "right") and face.length > 6
            if street:
                kit.shopfront(b, face, ground)
            else:
                kit.service_side(b, face, ground)
            kit.punched(b, face, ground, floors - 1, storey, spacing=rng.uniform(3.0, 3.6),
                        boarded=0.04, balconies=spec["balconies"] if name in ("front", "back") else 0.0)
            if name == "back":
                kit.pipes(b, face, 0.0, top, count=2)
            if spec["fire_escape"] == name and not escape_done:
                kit.fire_escape(b, face, ground - storey, floors, storey, u=face.length * 0.3)
                escape_done = True
        kit.parapet(b, rect, top, height=1.0)

    big = max(masses, key=lambda r: r.w * r.d)
    blocked = roof_blocked[id(big)]
    if spec["roof"] == "pad":
        size = min(big.w, big.d) - 3.0
        pad = kit.helipad(b, Rect.centred(big.cx, big.cz, size, size), top)
        blocked.append(pad.inset(-1.0))
    else:
        zone = kit.roof_zone(b, big.inset(0.4), top)
        if zone:
            blocked.append(zone)
    if rng.random() < 0.6:
        x, z = big.x1 - 1.5, big.z0 + 1.5
        kit.mast(b, x, z, top, rng.uniform(5.0, 9.0), lights=top + 9 > style["obstruction_height"])
        blocked.append(Rect.centred(x, z, 2.0, 2.0))
    for rect in masses:
        kinds = ("tank", "ac", "vent", "tank", "hut", "dish") if spec["roof"] == "tanks" else ("ac", "vent", "hut")
        kit.dress_roof(b, rect.inset(0.4), top, roof_blocked[id(rect)], density=1.0, kinds=kinds)
    for rect in masses:
        kit.obstruction_lights(b, rect, top + 1.0)


# -- slum ------------------------------------------------------------------------------------------------------------

SHACK_SWATCHES = ("wall", "primer", "rust", "dark", "trim", "concrete", "rust", "primer")


def slum(b):
    """Old concrete base with shacks piled on top: overhangs on steel stilts, ladders, cables, small neon."""
    spec, style, rng = b.spec, b.style, b.rng
    storey = 3.0
    plot = b.plot.inset(0.5)
    base = Rect.centred(plot.cx + rng.uniform(-0.5, 0.5), plot.cz, plot.w - spec["spread"] * 2,
                        plot.d - spec["spread"] * 2)
    top = 3.8 + (spec["base_floors"] - 1) * storey
    b.span(base.x0, base.x1, 0.0, top, base.z0, base.z1, "concrete", ALL, swatches={"top": "tar"})
    b.collider(base.x0, base.x1, 0.0, top, base.z0, base.z1)
    sides = faces(base)
    kit.shopfront(b, sides["front"], 3.8, lit=style["shop_lit"] * 0.7)
    kit.service_side(b, sides["back"], 3.8)
    for name, face in sides.items():
        kit.punched(b, face, 3.8, spec["base_floors"] - 1, storey, spacing=2.8, win=(1.1, 1.3), sill=1.0,
                    boarded=0.25, balconies=0.15, units=0.2)
    kit.pipes(b, sides["left"], 0.0, top, count=1)
    kit.parapet(b, base, top, height=0.8, thick=0.25)

    decks = [(base, top)]     # surfaces shacks can stand on: (rect, height)
    shacks = [(base, 0.0, top)]
    for _ in range(spec["stacks"]):
        for _try in range(30):
            deck, deck_y = rng.choice(decks[: max(1, len(decks))])
            w, d = rng.uniform(3.0, 6.0), rng.uniform(3.0, 5.0)
            h = rng.uniform(2.6, 3.2)
            reach = spec["spread"]
            cx = rng.uniform(deck.x0 - reach + w / 2, deck.x1 + reach - w / 2)
            cz = rng.uniform(deck.z0 - reach + d / 2, deck.z1 + reach - d / 2)
            rect = Rect.centred(cx, cz, w, d)
            if not (plot.contains(rect.x0, rect.z0) and plot.contains(rect.x1, rect.z1)):
                continue
            # Must stand mostly on its deck, and not cut into another shack.
            ox = max(0.0, min(rect.x1, deck.x1) - max(rect.x0, deck.x0))
            oz = max(0.0, min(rect.z1, deck.z1) - max(rect.z0, deck.z0))
            if ox * oz < 0.55 * w * d:
                continue
            if any(rect.overlaps(other, 0.1) and deck_y < oy1 - 0.05 and oy0 < deck_y + h - 0.05
                   for other, oy0, oy1 in shacks):
                continue
            _shack(b, rect, deck_y, h)
            _stilts(b, rect, deck, deck_y)
            shacks.append((rect, deck_y, deck_y + h + 0.12))
            decks.append((rect, deck_y + h + 0.12))
            break

    # Ladders from each deck to a shack roof above it, cables between shack corners, masts on the highest roofs.
    for rect, y0, y1 in shacks[1:]:
        if rng.random() < 0.35:
            kit.ladder(b, rect.x0 + 0.8, rect.z1, y0, y1, (0.0, 1.0))
    tops = sorted(shacks[1:], key=lambda s: -s[2])
    for (ra, _a0, a1), (rb, _b0, b1) in zip(tops, tops[1:]):
        if rng.random() < 0.6:
            b.beam((ra.x1, a1 - 0.3, ra.z1), (rb.x0, b1 - 0.3, rb.z1), 0.05, "rubber")
    for rect, _y0, y1 in tops[:2]:
        if rng.random() < 0.7:
            kit.mast(b, rect.x0 + 0.5, rect.z0 + 0.5, y1, rng.uniform(3.0, 6.0), lights=False)
        else:
            kit.dish(b, rect.cx, rect.cz, y1)
    for rect, y0, y1 in rng.sample(shacks[1:], min(2, len(shacks) - 1)):
        if rng.random() < style["neon"]:
            face = faces(rect)[rng.choice(("front", "left", "right"))]
            colour = rng.choice(("neon_pink", "neon_cyan", "neon_green"))
            kit.wall_sign(b, face, face.length / 2, y1 - 0.9, min(2.6, face.length - 0.6), colour)
    # Barrels and crates on the base roof.
    placer = kit.Placer(rng, base.inset(0.5), 0.0)
    for rect, y0, _y1 in shacks[1:]:
        if abs(y0 - top) < 0.01:
            placer.block(rect)
    for _ in range(4):
        spot = placer.place(0.9, 0.9, gap=0.3)
        if spot:
            b.cylinder((spot.cx, top, spot.cz), (spot.cx, top + 0.95, spot.cz), 0.4, rng.choice(("rust", "dark",
                                                                                                 "primer")), segments=8)


def _shack(b, rect, y, h):
    rng = b.rng
    swatch = rng.choice(SHACK_SWATCHES)
    b.span(rect.x0, rect.x1, y, y + h, rect.z0, rect.z1, swatch, ALL)
    b.collider(rect.x0, rect.x1, y, y + h + 0.12, rect.z0, rect.z1)
    roof = rect.inset(-0.25)
    tilt = rng.uniform(-0.12, 0.12)
    b.span(roof.x0, roof.x1, y + h, y + h + 0.12, roof.z0, roof.z1, rng.choice(("rust", "metal", "tar", "steel")), L01)
    if tilt > 0.06:
        b.span(roof.x0, roof.x1, y + h + 0.12, y + h + 0.16, roof.z0, roof.z0 + 0.5, "rust")
    sides = list(faces(rect).items())
    rng.shuffle(sides)
    door = True
    for name, face in sides[:3]:
        if door and face.length > 1.6:
            b.on_face(face, face.length * rng.uniform(0.3, 0.7), y + 1.0, 0.03, 0.9, 2.0, 0.06, "dark")
            door = False
            continue
        for k in range(rng.randint(1, 2)):
            u = face.length * (k + 1) / 3
            lit = rng.random() < b.style["lit"] * 2.2
            b.on_face(face, u, y + 1.6, 0.03, 0.9, 0.8, 0.06, b.lit_swatch() if lit else "glass")


def _stilts(b, rect, deck, deck_y):
    """Steel legs under each corner of a shack that hangs off its deck, down to whatever is below (or the ground)."""
    builder = b.lods[0]
    for x in (rect.x0 + 0.15, rect.x1 - 0.15):
        for z in (rect.z0 + 0.15, rect.z1 - 0.15):
            if deck.contains(x, z, -0.05):
                continue
            point, _normal = builder.hit((x, deck_y - 0.05, z), (0.0, -1.0, 0.0), everything=True, distance=200.0)
            ground = point.y if point is not None else 0.0
            if deck_y - ground < 0.1:
                continue
            b.span(x - 0.08, x + 0.08, ground, deck_y, z - 0.08, z + 0.08, "metal")
            if deck_y - ground > 2.0:
                b.beam((x, deck_y - 1.4, z), (x + (0.9 if x < rect.cx else -0.9), deck_y, z), 0.07, "metal")


# -- industrial ------------------------------------------------------------------------------------------------------

def industrial(b):
    """Sawtooth hall, office, chimneys, tanks, pipe runs, floodlights."""
    spec, style, rng = b.spec, b.style, b.rng
    hall_rect = None
    if spec.get("hall"):
        w, d, h, x, z = spec["hall"]
        hall_rect = Rect.centred(x, z, w, d)
        _hall(b, hall_rect, h)
    if spec.get("office"):
        w, d, floors, x, z = spec["office"]
        rect = Rect.centred(x, z, w, d)
        storey = 3.4
        top = 4.2 + (floors - 1) * storey
        b.span(rect.x0, rect.x1, 0.0, top, rect.z0, rect.z1, "wall", L01, swatches={"top": "tar"})
        b.mass(rect, 0.0, top)
        sides = faces(rect)
        kit.shopfront(b, sides["front"], 4.2, lit=0.4, signs=False)
        for name, face in sides.items():
            kit.punched(b, face, 4.2, floors - 1, storey, spacing=3.0, win=(2.0, 1.5), sill=1.0)
        kit.parapet(b, rect, top, 0.9)
        kit.dress_roof(b, rect.inset(0.3), top, density=0.8, kinds=("ac", "vent"))
    for x, z, radius, height in spec.get("chimneys", []):
        _chimney(b, x, z, radius, height)
    tanks = spec.get("tanks", [])
    for x, z, radius, height in tanks:
        _tank(b, x, z, radius, height)
    # Pipe runs: tank to tank, and the first tank to the hall.
    for (xa, za, ra, ha), (xb, zb, rb, hb) in zip(tanks, tanks[1:]):
        _pipe_run(b, (xa, za), ra, (xb, zb), rb, min(ha, hb) * 0.45)
    if tanks and hall_rect is not None:
        xa, za, ra, ha = tanks[-1]
        hx = min(max(xa, hall_rect.x0), hall_rect.x1)
        hz = min(max(za, hall_rect.z0), hall_rect.z1)
        _pipe_run(b, (xa, za), ra, (hx, hz), 0.0, min(ha * 0.45, spec["hall"][2] - 1.0))
    plot = b.plot
    for x, z in ((plot.x0 + 1.0, plot.z1 - 1.0), (plot.x1 - 1.0, plot.z0 + 1.0)):
        if not _occupied(b, x, z):
            kit.floodlight(b, x, z, 11.0, facing=(1.0 if x < 0 else -1.0, 0.0))


def _occupied(b, x, z):
    return any(abs(x - c[0]) < s[0] / 2 + 1.0 and abs(z - c[2]) < s[2] / 2 + 1.0 for c, s, _yaw in b.colliders)


def _hall(b, rect, h):
    rng = b.rng
    b.span(rect.x0, rect.x1, 0.0, h, rect.z0, rect.z1, "concrete", L01, swatches={"top": "tar"})
    b.span(rect.x0, rect.x1, 0.0, h, rect.z0, rect.z1, "concrete", L2)
    b.collider(rect.x0, rect.x1, 0.0, h, rect.z0, rect.z1)
    sides = faces(rect)
    # High window band on the long sides, roller doors on the front, a plinth band.
    for name in ("left", "right", "back", "front"):
        face = sides[name]
        b.on_face(face, face.length / 2, h - 2.4, 0.02, face.length - 1.6, 1.6, 0.06, "glass", L01)
        count = max(1, int(face.length // 4))
        for i in range(1, count):
            b.on_face(face, i * face.length / count, h - 2.4, 0.06, 0.2, 1.8, 0.12, "metal")
        b.on_face(face, face.length / 2, 0.6, 0.05, face.length, 1.2, 0.1, "dark")
        b.on_face(face, face.length / 2, h - 0.2, 0.1, face.length, 0.4, 0.2, "trim", L01)
    front = sides["front"]
    doors = max(1, int(front.length // 12))
    for i in range(doors):
        u = (i + 0.5) * front.length / doors
        b.on_face(front, u, 2.9, 0.05, 5.0, 5.8, 0.1, "dark", L01)
        for side in (-1, 1):
            b.on_face(front, u + side * 2.75, 2.9, 0.08, 0.5, 5.8, 0.16, "hazard")
        b.on_face(front, u, 6.1, 0.35, 1.2, 0.2, 0.7, "sodium", L01)
    # Sawtooth roof: teeth across x, glass on the steep side.
    teeth = max(2, round(rect.w / 7.0))
    step = rect.w / teeth
    rise = 2.6
    for i in range(teeth):
        xa, xb = rect.x0 + i * step, rect.x0 + (i + 1) * step
        b.prism([(xa, h), (xb, h), (xb, h + rise)], "z", rect.z0 + 0.3, rect.z1 - 0.3, ["tar", "glass", "metal"],
                "concrete", L01)
    b.collider(rect.x0, rect.x1, h, h + rise * 0.5, rect.z0, rect.z1)
    kit.obstruction_lights(b, rect, h + rise)


def _chimney(b, x, z, radius, height):
    b.span(x - radius - 0.6, x + radius + 0.6, 0.0, 1.5, z - radius - 0.6, z + radius + 0.6, "concrete", L01)
    b.cylinder((x, 1.5, z), (x, height, z), radius, "concrete", segments=16, at=L01, end_radius=radius * 0.82)
    b.cylinder((x, 1.5, z), (x, height, z), radius, "concrete", segments=6, at=L2, end_radius=radius * 0.82)
    top_r = radius * 0.82
    b.cylinder((x, height - 3.0, z), (x, height, z), top_r * 1.04 + 0.03, "soot", segments=16, at=L01)
    for k in (0.33, 0.66):
        y = height * k
        r = radius + (top_r - radius) * k + 0.05
        b.cylinder((x, y, z), (x, y + 0.6, z), r, "trim", segments=16)
    for angle in (0, 120, 240):
        a = math.radians(angle)
        for y in (height - 0.5, height * 0.5):
            r = (top_r if y > height * 0.9 else radius * 0.91) + 0.1
            b.box((x + math.cos(a) * r, y, z + math.sin(a) * r), (0.3, 0.3, 0.3), "warning", L01)
    kit.ladder(b, x, z + radius * 0.9 - 0.05, 1.5, height - 1.0, (0.0, 1.0), rung=1.2)
    b.round_collider(x, z, radius, 0.0, height)


def _tank(b, x, z, radius, height):
    rng = b.rng
    swatch = rng.choice(("steel", "trim", "wall"))
    b.span(x - radius - 0.4, x + radius + 0.4, 0.0, 0.5, z - radius - 0.4, z + radius + 0.4, "concrete", L01)
    b.cylinder((x, 0.5, z), (x, height, z), radius, swatch, segments=24, at=L01)
    b.cylinder((x, 0.5, z), (x, height, z), radius, swatch, segments=8, at=L2)
    b.cylinder((x, height, z), (x, height + radius * 0.18, z), radius, swatch, segments=24, at=L01,
               end_radius=radius * 0.25)
    for k in (0.3, 0.62):
        y = 0.5 + (height - 0.5) * k
        b.cylinder((x, y, z), (x, y + 0.35, z), radius + 0.08, "metal", segments=24)
    b.cylinder((x, 0.5, z), (x, 2.0, z), radius + 0.02, "rust", segments=24)
    kit.ladder(b, x, z + radius - 0.05, 0.5, height, (0.0, 1.0))
    for angle in range(0, 360, 30):
        a = math.radians(angle)
        b.span(x + math.cos(a) * (radius - 0.2) - 0.04, x + math.cos(a) * (radius - 0.2) + 0.04, height,
               height + 1.0, z + math.sin(a) * (radius - 0.2) - 0.04, z + math.sin(a) * (radius - 0.2) + 0.04, "metal")
    b.round_collider(x, z, radius, 0.0, height + radius * 0.18)


def _pipe_run(b, a, ra, c, rc, y):
    """Two pipes between two points (stopping at the tank walls), on frames every 6 m."""
    (xa, za), (xc, zc) = a, c
    dx, dz = xc - xa, zc - za
    length = math.hypot(dx, dz)
    if length < ra + rc + 1.0:
        return
    ux, uz = dx / length, dz / length
    start = (xa + ux * (ra - 0.2), za + uz * (ra - 0.2))
    end = (xc - ux * (rc - 0.2), zc - uz * (rc - 0.2)) if rc > 0 else (xc, zc)
    run = math.hypot(end[0] - start[0], end[1] - start[1])
    px, pz = -uz, ux
    for offset, radius, lift in ((-0.45, 0.32, 0.0), (0.45, 0.22, 0.25)):
        p0 = (start[0] + px * offset, y + lift, start[1] + pz * offset)
        p1 = (end[0] + px * offset, y + lift, end[1] + pz * offset)
        b.cylinder(p0, p1, radius, "metal", segments=8)
    yaw = math.degrees(math.atan2(-uz, ux))
    mx, mz = (start[0] + end[0]) / 2, (start[1] + end[1]) / 2
    b.colliders.append(((mx, y + 0.1, mz), (run, 1.0, 1.8), yaw))
    frames = int(run // 6)
    for k in range(1, frames + 1):
        t = k / (frames + 1)
        fx, fz = start[0] + (end[0] - start[0]) * t, start[1] + (end[1] - start[1]) * t
        for side in (-1, 1):
            lx, lz = fx + px * side * 0.9, fz + pz * side * 0.9
            b.span(lx - 0.1, lx + 0.1, 0.0, y - 0.35, lz - 0.1, lz + 0.1, "metal")
        b.beam((fx - px * 0.9, y - 0.35, fz - pz * 0.9), (fx + px * 0.9, y - 0.35, fz + pz * 0.9), 0.18, "metal")
