"""
The four building types. Each takes a Building (kit.py) and fills its LOD meshes, colliders and markers from the spec
(specs.py) and the style (styles.py).
"""
import math

from . import kit
from .kit import ALL, L0, L01, L1, L2, Rect, faces


def build(b):
    {"tower": tower, "mega": mega, "twins": twins, "twist": twist, "halo": halo, "orb": orb, "block": block,
     "slum": slum, "industrial": industrial}[b.type](b)
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
        b.add_thin(rect.cx, rect.cz, y + 3.8 + height)
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


# -- mega ------------------------------------------------------------------------------------------------------------

def mega(b):
    """Megatower (300 to 550 m): podium, many setback tiers with alternating facades, sky-lobby rings every N floors,
    neon corner strips, giant billboards, landing pads that stand out from the walls, a tall crown."""
    spec = b.spec
    plot = b.plot
    tiers = [(Rect.centred(plot.w * ox, plot.d * oz, plot.w * sw_, plot.d * sd_), floors, facade)
             for sw_, sd_, floors, ox, oz, facade in spec["tiers"]]
    info = _megatower(b, tiers, spec["podium"], spec.get("lobby_every", 20), spec.get("neon", ()), spec["crown"])
    _billboards_and_pads(b, info, spec.get("billboards", ()), spec.get("pads", ()), spec.get("lobby_every", 20))


def _megatower(b, tiers, podium, every, neon, crown, street=("front", "left", "right")):
    """Podium + setback tiers + crown. tiers: [(rect, floors, ribbon | grid)]. Returns [(rect, y0, top, floors)]."""
    rng, storey = b.rng, b.style["storey"]
    base = tiers[0][0]
    b.span(base.x0, base.x1, 0.0, podium, base.z0, base.z1, "wall", L01)
    sides = faces(base)
    for name, face in sides.items():
        if name in street:
            kit.shopfront(b, face, podium)
        else:
            kit.service_side(b, face, podium)
    kit.sky_lobby(b, base, podium - 3.2, "neon_cyan")
    info = []
    y = podium
    for index, (rect, floors, facade) in enumerate(tiers):
        build_facade = kit.ribbon if facade == "ribbon" else kit.grid
        top = build_facade(b, rect, y, floors, storey)
        b.mass(rect, 0.0 if index == 0 else y, top, swatch="glass", top="tar")
        for f in range(every, floors, every):
            kit.sky_lobby(b, rect, y + f * storey - 1.3)
        if index in neon:
            kit.neon_edges(b, rect, y, top, rng.choice(("neon_cyan", "neon_pink")))
        kit.tier_lights(b, rect, y, top)
        upper = tiers[index + 1][0] if index + 1 < len(tiers) else None
        kit.parapet(b, rect, top, height=1.2)
        kit.obstruction_lights(b, rect, top + 1.2)
        blocked = [upper.inset(-1.0)] if upper else []
        if upper is None:
            blocked += _mega_crown(b, rect, top, crown)
        kit.dress_roof(b, rect.inset(0.4), top, blocked, density=0.6)
        info.append((rect, y, top, floors))
        y = top
    return info


def _billboards_and_pads(b, info, billboards, pads, every):
    storey, plot = b.style["storey"], b.plot
    for index, side in billboards:
        rect, y0, top, _floors = info[index]
        face = faces(rect)[side]
        width = min(face.length * 0.7, 40.0)
        height = min((top - y0) * 0.45, width * 1.3)
        kit.billboard(b, face, face.length / 2, y0 + (top - y0) * 0.55, width, height)
    for index, side in pads:
        rect, y0, _top, floors = info[index]
        face = faces(rect)[side]
        clearance = {"front": plot.z1 - rect.z1, "back": rect.z0 - plot.z0,
                     "right": plot.x1 - rect.x1, "left": rect.x0 - plot.x0}[side]
        depth = min(14.0, clearance - 0.3)
        if depth < 9.0:
            print(f"{b.name}: no room for a sky pad on tier {index} {side} ({clearance:.1f} m to the plot edge)")
            continue
        floor = floors // 2
        if floor % every in (0, every - 1, 1):
            floor += 3
        kit.sky_pad(b, face, face.length / 2, y0 + floor * storey, 14.0, depth)


# -- twin towers and sky bridges -------------------------------------------------------------------------------------

def twins(b):
    """Two megatowers on one plot, joined by sky bridges: enclosed glass bridges and an open sky deck with a landing
    pad. Choppers can fly under and between them."""
    spec, plot = b.spec, b.plot
    gap = spec["gap"]
    width = (plot.w - gap) / 2
    infos = []
    for side, (floors_list, crown) in zip((-1, 1), spec["towers"]):
        cx = side * (gap + width) / 2
        tiers = []
        share = 1.0
        for floors, facade in floors_list:
            tiers.append((Rect.centred(cx, 0.0, width * share, plot.d * share), floors, facade))
            share -= 0.14
        street = ("front", "back", "left" if side < 0 else "right")
        infos.append(_megatower(b, tiers, spec["podium"], spec.get("lobby_every", 20), spec.get("neon", ()), crown,
                                street=street))
    for kind, height, wide in spec["bridges"]:
        x0, x1 = _inner_walls(infos, height)
        if x0 is None:
            print(f"{b.name}: no walls on both sides at {height:.0f} m for a bridge")
            continue
        if kind == "deck":
            _sky_deck(b, x0, x1, height, wide)
        else:
            _glass_bridge(b, x0, x1, height, wide)


def _inner_walls(infos, height):
    """x of the left tower's inner wall and the right tower's inner wall at a height (None if a tower is too low)."""
    walls = []
    for info, pick in zip(infos, (max, min)):
        hits = [rect for rect, y0, top, _f in info if y0 - 0.1 <= height <= top - 9.0]
        if not hits:
            return None, None
        walls.append(pick(rect.x1 for rect in hits) if pick is max else pick(rect.x0 for rect in hits))
    return walls[0], walls[1]


def _glass_bridge(b, x0, x1, y, wide):
    """Enclosed sky bridge: concrete floor beam, glass walls, ribs, roof, neon lines along the bottom edges."""
    hw = wide / 2
    height = 6.5
    b.span(x0 - 0.5, x1 + 0.5, y, y + 1.2, -hw, hw, "concrete", ALL)
    b.span(x0 - 0.5, x1 + 0.5, y + 1.2, y + height - 0.8, -hw + 0.3, hw - 0.3, "glass", L01)
    b.span(x0 - 0.5, x1 + 0.5, y + height - 0.8, y + height, -hw, hw, "dark", L01, swatches={"top": "tar"})
    count = max(2, int((x1 - x0) // 4.0))
    for i in range(1, count):
        x = x0 + i * (x1 - x0) / count
        b.span(x - 0.2, x + 0.2, y + 1.2, y + height - 0.8, -hw, hw, "trim", L01)
    for z in (-hw - 0.05, hw + 0.05):
        b.span(x0 - 0.5, x1 + 0.5, y + 0.2, y + 0.45, z - 0.08, z + 0.08, b.rng.choice(("neon_cyan", "neon_pink")), L01)
    for x in (x0 + (x1 - x0) * 0.3, x0 + (x1 - x0) * 0.7):
        b.span(x - 1.5, x + 1.5, y + 2.0, y + 4.2, hw - 0.32, hw - 0.28, b.lit_swatch(), L01)
    b.collider(x0, x1, y, y + height, -hw, hw)


def _sky_deck(b, x0, x1, y, wide):
    """Open bridge deck with a landing pad in the middle, railings and lights; truss under it."""
    hw = wide / 2
    b.span(x0 - 0.5, x1 + 0.5, y - 2.5, y, -hw, hw, "dark", ALL, swatches={"top": "concrete"})
    for z in (-hw + 0.3, hw - 0.3):
        b.span(x0, x1, y, y + 1.1, z - 0.06, z + 0.06, "metal")
        for k in range(int((x1 - x0) // 6) + 1):
            x = x0 + 1.0 + k * 6.0
            if x < x1 - 0.5:
                b.box((x, y + 1.18, z), (0.22, 0.2, 0.22), "sodium", L01)
    span = x1 - x0
    for k in range(int(span // 8)):
        xa = x0 + k * 8.0
        b.beam((xa, y - 2.5, -hw + 0.5), (min(x1, xa + 8.0), y - 7.5, -hw + 0.5), 0.5, "metal")
        b.beam((xa, y - 2.5, hw - 0.5), (min(x1, xa + 8.0), y - 7.5, hw - 0.5), 0.5, "metal")
    b.span(x0, x1, y - 8.0, y - 7.0, -hw + 0.2, -hw + 0.8, "metal")
    b.span(x0, x1, y - 8.0, y - 7.0, hw - 0.8, hw - 0.2, "metal")
    b.collider(x0, x1, y - 8.0, y, -hw, hw)
    size = min(span - 3.0, wide - 2.0)
    kit.helipad(b, Rect.centred((x0 + x1) / 2, 0.0, size, size), y)


# -- landmarks: high tech and unusual --------------------------------------------------------------------------------

def twist(b):
    """A tower that turns as it rises: stacks of floors, each turned a little more (Unity yaw), ribbon facades,
    neon edges on every few stacks, antennas on top."""
    spec, rng, storey = b.spec, b.rng, b.style["storey"]
    size = spec["size"]
    podium = spec["podium"]
    base = Rect.centred(0.0, 0.0, size * 1.25, size * 1.25)
    b.span(base.x0, base.x1, 0.0, podium, base.z0, base.z1, "wall", ALL)
    b.collider(base.x0, base.x1, 0.0, podium, base.z0, base.z1)
    for face in faces(base).values():
        kit.shopfront(b, face, podium)
    kit.sky_lobby(b, base, podium - 3.0, "neon_pink")
    kit.parapet(b, base, podium, height=1.0)
    y = podium
    stacks = spec["stacks"]
    turn = spec["turn"]
    rect = Rect.centred(0.0, 0.0, size, size)
    colour = rng.choice(("neon_cyan", "neon_pink"))
    for i in range(stacks):
        angle = turn * i / max(1, stacks - 1)
        taper = 1.0 - spec.get("taper", 0.0) * i / stacks
        r = Rect.centred(0.0, 0.0, size * taper, size * taper)
        with b.turned(0.0, 0.0, angle):
            top = kit.ribbon(b, r, y, spec["floors"], storey, inset=0.6, band=1.1, columns_at=L0)
            b.mass(r, y, top, swatch="glass", top="tar")
            if i % 3 == 1:
                kit.neon_edges(b, r, y, top, colour)
            if i == stacks - 1:
                kit.parapet(b, r, top, 1.2)
                kit.obstruction_lights(b, r, top + 1.2)
                for _ in range(3):
                    x = rng.uniform(r.x0 + 2.0, r.x1 - 2.0)
                    z = rng.uniform(r.z0 + 2.0, r.z1 - 2.0)
                    kit.mast(b, x, z, top, rng.uniform(15.0, 30.0))
        if top - podium > 0 and int((y - podium) // 90) != int((top - podium) // 90):
            with b.turned(0.0, 0.0, angle):
                for x in (r.x0 - 0.1, r.x1 + 0.1):
                    for z in (r.z0 - 0.1, r.z1 + 0.1):
                        b.box((x, top - 1.5, z), (0.5, 0.5, 0.5), "warning", L01)
        y = top


def halo(b):
    """Round glass tower with two giant rings round it, held by spokes; a landing pad on the round roof."""
    spec, rng, storey = b.spec, b.rng, b.style["storey"]
    radius = spec["radius"]
    podium = spec["podium"]
    base = Rect.centred(0.0, 0.0, radius * 2 + 10.0, radius * 2 + 10.0)
    b.span(base.x0, base.x1, 0.0, podium, base.z0, base.z1, "wall", ALL)
    b.collider(base.x0, base.x1, 0.0, podium, base.z0, base.z1)
    for face in faces(base).values():
        kit.shopfront(b, face, podium)
    kit.parapet(b, base, podium, height=1.0)
    top = podium + spec["floors"] * storey
    b.cylinder((0, podium, 0), (0, top, 0), radius, "glass", segments=16, at=L01)
    b.cylinder((0, 0.0, 0), (0, top, 0), radius, "glass", segments=8, at=L2)
    b.round_collider(0.0, 0.0, radius, podium, top)
    for f in range(0, spec["floors"] + 1, 2):
        y = podium + f * storey
        b.cylinder((0, y, 0), (0, y + 0.8, 0), radius + 0.25, "wall", segments=16, at=L0 if f % 4 else L01)
    # Lit windows: small panels on the round wall.
    for f in range(spec["floors"]):
        for _ in range(3):
            if rng.random() < b.style["lit"] * 2.0:
                with b.turned(0.0, 0.0, rng.uniform(0.0, 360.0)):
                    b.box((0.0, podium + f * storey + 2.0, radius * 0.985), (rng.uniform(2.0, 5.0), 2.0, 0.6),
                          b.lit_swatch(), L01)
    # Vertical neon lines.
    for k in range(4):
        with b.turned(0.0, 0.0, 45.0 + 90.0 * k):
            b.span(-0.2, 0.2, podium, top, radius + 0.1, radius + 0.45, "neon_cyan", L01)
    for ring_y, ring_r in spec["rings"]:
        _ring(b, ring_y, radius, ring_r)
    kit.tier_lights(b, Rect.centred(0, 0, radius * 1.42, radius * 1.42), podium, top)
    # Round roof: parapet ring, obstruction lights, landing pad.
    b.cylinder((0, top, 0), (0, top + 1.2, 0), radius + 0.3, "wall", segments=16, at=ALL)
    b.cylinder((0, top + 1.2, 0), (0, top + 1.25, 0), radius - 0.6, "tar", segments=16, at=L01)
    for k in range(4):
        with b.turned(0.0, 0.0, 90.0 * k):
            b.box((0.0, top + 1.4, radius), (0.4, 0.4, 0.4), "warning", L01)
    pad = radius * 1.2
    kit.helipad(b, Rect.centred(0.0, 0.0, pad, pad), top + 1.2)


def _ring(b, y, core, radius):
    """A ring walkway round the tower: box segments turned round the axis, neon on the outer edge, 4 spokes."""
    count = 24
    width = 7.0
    length = 2 * radius * math.tan(math.pi / count) + 0.6
    for i in range(count):
        with b.turned(0.0, 0.0, 360.0 * i / count):
            b.span(-length / 2, length / 2, y, y + 4.0, radius - width / 2, radius + width / 2, "dark", L01,
                   swatches={"top": "concrete"})
            b.span(-length / 2, length / 2, y + 1.6, y + 2.0, radius + width / 2, radius + width / 2 + 0.15,
                   "neon_pink", L01)
            b.span(-length / 2 + 0.3, length / 2 - 0.3, y + 4.0, y + 5.0, radius + width / 2 - 0.25,
                   radius + width / 2 - 0.1, "metal")
            b.collider(-length / 2, length / 2, y, y + 4.0, radius - width / 2, radius + width / 2)
            if i % 6 == 0:
                b.span(-1.5, 1.5, y + 0.5, y + 3.5, core - 0.5, radius - width / 2 + 0.2, "dark", L01)
                b.span(-1.0, 1.0, y + 3.5, y + 3.7, core - 0.5, radius - width / 2 + 0.2, "sodium", L01)
                b.collider(-1.5, 1.5, y + 0.5, y + 3.5, core - 0.5, radius - width / 2 + 0.2)
            if i % 3 == 0:
                b.box((0.0, y + 4.2, radius + width / 2 - 0.4), (0.3, 0.3, 0.3), "warning", L01)
    b.cylinder((0, y, 0), (0, y + 4.0, 0), radius + width / 2, "dark", segments=8, at=L2)


def orb(b):
    """Grid tower holding a wide round deck at the top with a faceted orb set into it (half above, half below);
    landing pads on the deck round the orb."""
    spec, storey = b.spec, b.style["storey"]
    size, podium = spec["size"], spec["podium"]
    rect = Rect.centred(0.0, 0.0, size, size)
    b.span(rect.x0 - 3, rect.x1 + 3, 0.0, podium, rect.z0 - 3, rect.z1 + 3, "wall", ALL)
    b.collider(rect.x0 - 3, rect.x1 + 3, 0.0, podium, rect.z0 - 3, rect.z1 + 3)
    for face in faces(rect.inset(-3.0)).values():
        kit.shopfront(b, face, podium)
    top = kit.grid(b, rect, podium, spec["floors"], storey)
    b.mass(rect, podium, top, swatch="glass", top="tar")
    kit.neon_edges(b, rect, podium, top, "neon_cyan")
    for f in range(20, spec["floors"], 20):
        kit.sky_lobby(b, rect, podium + f * storey - 1.3)
    kit.tier_lights(b, rect, podium, top)
    # Flared neck under the deck.
    neck = 10.0
    b.cylinder((0, top - 1.0, 0), (0, top + neck, 0), size * 0.75, "dark", segments=8, at=ALL,
               end_radius=spec["deck"] * 0.55)
    b.round_collider(0.0, 0.0, size * 0.6, top, top + neck)
    deck_y = top + neck
    deck_r = spec["deck"]
    b.cylinder((0, deck_y, 0), (0, deck_y + 1.4, 0), deck_r, "dark", segments=16, at=L01, cap_end="concrete")
    b.cylinder((0, deck_y, 0), (0, deck_y + 1.4, 0), deck_r, "dark", segments=8, at=L2)
    b.round_collider(0.0, 0.0, deck_r, deck_y, deck_y + 1.4)
    b.cylinder((0, deck_y + 0.5, 0), (0, deck_y + 0.9, 0), deck_r + 0.1, "neon_pink", segments=16, at=L01)
    for k in range(16):
        with b.turned(0.0, 0.0, 22.5 * k):
            b.box((0.0, deck_y + 1.55, deck_r - 0.4), (0.25, 0.3, 0.25), "sodium", L01)
    # The orb: centre on the deck top, so the deck is its widest ring and nothing hangs over the pads.
    r = spec["orb"]
    centre_y = deck_y + 1.4
    b.ico((0, centre_y, 0), r, "glass", at=ALL, subdivisions=2)
    for lat, colour in ((0.0, "neon_cyan"), (0.45, "trim"), (-0.45, "trim")):
        ring_r = r * math.cos(lat) * 0.985
        yy = centre_y + r * math.sin(lat)
        b.cylinder((0, yy - 0.3, 0), (0, yy + 0.3, 0), ring_r + 0.15, colour, segments=20,
                   at=L01 if colour != "trim" else L0)
    slices = 5
    for k in range(slices):
        a0, a1 = -math.pi / 2 + math.pi * k / slices, -math.pi / 2 + math.pi * (k + 1) / slices
        # 0.9: the ends of the faceted orb sit a little inside the true sphere.
        y0, y1 = centre_y + r * 0.9 * math.sin(a0), centre_y + r * 0.9 * math.sin(a1)
        widest = r if a0 < 0 < a1 else r * max(math.cos(a0), math.cos(a1))
        b.round_collider(0.0, 0.0, widest * 0.97, y0, y1)
    kit.mast(b, 0.0, 0.0, centre_y + r - 0.3, 18.0)
    b.box((0.0, centre_y + r + 0.2, 0.0), (1.4, 1.0, 1.4), "metal", L01)
    # Landing pads on the deck, round the orb.
    pad = deck_r - r - 2.0
    for angle in spec.get("pads", (90.0, 270.0)):
        with b.turned(0.0, 0.0, angle):
            kit.helipad(b, Rect.centred(0.0, r + 1.0 + pad / 2, pad, pad), deck_y + 1.4)


def _mega_crown(b, rect, y, crown):
    rng = b.rng
    if crown == "spire":
        base = Rect.centred(rect.cx, rect.cz, 9.0, 9.0)
        b.span(base.x0, base.x1, y, y + 6.0, base.z0, base.z1, "wall", ALL, swatches={"top": "trim"})
        b.collider(base.x0, base.x1, y, y + 6.0, base.z0, base.z1)
        height = rng.uniform(60.0, 80.0)
        start = y + 6.0
        b.cylinder((rect.cx, start, rect.cz), (rect.cx, start + height, rect.cz), 2.2, "steel", segments=10, at=L01,
                   end_radius=0.2)
        for k in (0.3, 0.55):
            r = 2.2 - 2.0 * k + 0.05
            b.cylinder((rect.cx, start + height * k, rect.cz), (rect.cx, start + height * k + 0.5, rect.cz), r,
                       rng.choice(("neon_cyan", "neon_pink")), segments=10, at=L01)
        for k in (0.2, 0.45, 0.7):
            r = 2.2 - 2.0 * k
            b.box((rect.cx, start + height * k, rect.cz + r), (0.4, 0.4, 0.3), "warning", L01)
        b.box((rect.cx, start + height + 0.1, rect.cz), (0.5, 0.5, 0.5), "warning", L01)
        b.add_thin(rect.cx, rect.cz, start + height + 1.0, 2.7)
        return [base.inset(-2.0)]
    if crown == "antennas":
        taken = []
        for _ in range(rng.randint(4, 6)):
            x = rng.uniform(rect.x0 + 2.5, rect.x1 - 2.5)
            z = rng.uniform(rect.z0 + 2.5, rect.z1 - 2.5)
            kit.mast(b, x, z, y, rng.uniform(20.0, 45.0))
            taken.append(Rect.centred(x, z, 2.0, 2.0))
        return taken
    if crown == "pad":
        size = min(rect.w, rect.d) - 4.0
        pad = kit.helipad(b, Rect.centred(rect.cx, rect.cz, size, size), y)
        return [pad.inset(-1.0)]
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
    # Refinery parts (City04 and later kits).
    for x, z, radius, height in spec.get("columns", []):
        _column(b, x, z, radius, height)
    for x, z, radius, height in spec.get("cooling", []):
        _cooling_tower(b, x, z, radius, height)
    for x, z, radius in spec.get("spheres", []):
        _sphere_tank(b, x, z, radius)
    for x, z, height in spec.get("flares", []):
        _flare(b, x, z, height)
    for x, z, cols, rows, stacks in spec.get("containers", []):
        _containers(b, x, z, cols, rows, stacks)
    for a, c, y in spec.get("racks", []):
        _pipe_run(b, tuple(a), 0.0, tuple(c), 0.0, y, rest=True)
    # Catwalks between neighbouring vessels of about the same height (tank tops, column decks).
    def top_deck(height):       # the highest platform ring _column builds (8 m, then every 9 m, below the top)
        y = 8.0
        while y + 9.0 < height - 2.0:
            y += 9.0
        return y

    vessels = [(x, z, r, h) for x, z, r, h in tanks] + [(x, z, r + 1.2, top_deck(h))
                                                        for x, z, r, h in spec.get("columns", []) if h > 12.0]
    for (xa, za, ra, ha), (xb, zb, rb, hb) in zip(vessels, vessels[1:]):
        gap = math.hypot(xb - xa, zb - za) - ra - rb
        if 1.0 < gap < 24.0 and abs(ha - hb) < 2.5:
            _catwalk(b, (xa, za), ra, (xb, zb), rb, min(ha, hb))
    plot = b.plot
    for x, z in ((plot.x0 + 1.0, plot.z1 - 1.0), (plot.x1 - 1.0, plot.z0 + 1.0)):
        if not _occupied(b, x, z):
            kit.floodlight(b, x, z, 11.0, facing=(1.0 if x < 0 else -1.0, 0.0))
    # Yard vehicles: a few trucks, tankers and vans in free spots of the yard.
    parked = 0
    for _ in range(24):
        if parked >= spec.get("vehicles", 3):
            break
        x, z = rng.uniform(plot.x0 + 4.0, plot.x1 - 4.0), rng.uniform(plot.z0 + 7.0, plot.z1 - 7.0)
        yaw = rng.choice((0.0, 90.0, 180.0, 270.0))
        long = 6.5 if yaw in (0.0, 180.0) else 2.0
        wide = 2.0 if yaw in (0.0, 180.0) else 6.5
        if not plot.inset(max(long, wide) + 1.0).contains(x, z):
            continue
        if any(_occupied(b, x + dx, z + dz) for dx in (-wide, 0.0, wide) for dz in (-long, 0.0, long)):
            continue
        kit.vehicle(b, rng.choice(("truck", "tanker", "van")), x, z, yaw)
        parked += 1


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
    """Concrete stack. Varies per stack (the building's seed): shaft colour, ring bands, soot length; stacks of 40 m
    and more get red and white aviation bands at the top (as real tall stacks do)."""
    rng = b.rng
    shaft = rng.choice(("concrete", "wall", "concrete", "trim"))
    b.span(x - radius - 0.6, x + radius + 0.6, 0.0, 1.5, z - radius - 0.6, z + radius + 0.6, "concrete", L01)
    b.cylinder((x, 1.5, z), (x, height, z), radius, shaft, segments=16, at=L01, end_radius=radius * 0.82)
    b.cylinder((x, 1.5, z), (x, height, z), radius, shaft, segments=6, at=L2, end_radius=radius * 0.82)
    top_r = radius * 0.82

    def r_at(y):
        return radius + (top_r - radius) * (y - 1.5) / (height - 1.5) + 0.05

    soot = rng.uniform(2.5, 6.0)
    b.cylinder((x, height - soot, z), (x, height, z), r_at(height - soot) + 0.03, "soot", segments=16, at=L01,
               end_radius=top_r + 0.08)
    if height >= 40.0:
        bands, band = 6, (height * 0.22) / 6
        for k in range(bands):
            y = height - soot - (k + 1) * band
            b.cylinder((x, y, z), (x, y + band, z), r_at(y) + 0.02, "primer" if k % 2 == 0 else "trim", segments=16,
                       at=L01, end_radius=r_at(y + band) + 0.02)
    for k in sorted(rng.sample((0.2, 0.33, 0.45, 0.55, 0.66), rng.randint(1, 3))):
        y = height * k
        r = r_at(y)
        b.cylinder((x, y, z), (x, y + rng.choice((0.4, 0.6, 1.0)), z), r, rng.choice(("trim", "metal", "rust")),
                   segments=16)
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


def _pipe_run(b, a, ra, c, rc, y, rest=False):
    """Two pipes between two points (stopping at the tank walls), on frames every 6 m. rest: both pipes lie on the
    frames (free ends: a pipe rack)."""
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
    for offset, radius, lift in ((-0.45, 0.32, 0.0), (0.45, 0.22, -0.1 if rest else 0.25)):
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


def _catwalk(b, a, ra, c, rc, y):
    """A grated walkway from vessel wall to vessel wall at height y: deck, posts and two hand rails each side."""
    (xa, za), (xc, zc) = a, c
    dx, dz = xc - xa, zc - za
    length = math.hypot(dx, dz)
    ux, uz = dx / length, dz / length
    start = (xa + ux * (ra - 0.2), za + uz * (ra - 0.2))
    end = (xc - ux * (rc - 0.2), zc - uz * (rc - 0.2))
    run = math.hypot(end[0] - start[0], end[1] - start[1])
    mx, mz = (start[0] + end[0]) / 2, (start[1] + end[1]) / 2
    yaw = math.degrees(math.atan2(ux, uz))
    with b.turned(mx, mz, yaw):
        b.span(mx - 0.6, mx + 0.6, y - 0.15, y, mz - run / 2, mz + run / 2, "metal", L01)
        for side in (-1, 1):
            rx = mx + side * 0.6
            for rail in (0.55, 1.05):
                b.span(rx - 0.03, rx + 0.03, y + rail - 0.03, y + rail + 0.03, mz - run / 2, mz + run / 2, "hazard", L01)
            k = 0.0
            while k <= run:
                b.span(rx - 0.03, rx + 0.03, y, y + 1.05, mz - run / 2 + k - 0.03, mz - run / 2 + k + 0.03, "metal")
                k += 2.0
        b.collider(mx - 0.7, mx + 0.7, y - 0.15, y + 1.1, mz - run / 2, mz + run / 2)
        if run > 12.0:
            # A support frame in the middle of long spans.
            b.span(mx - 0.12, mx + 0.12, 0.0, y - 0.15, mz - 0.12, mz + 0.12, "steel", L01)


# -- refinery parts ----------------------------------------------------------------------------------------------------

def _column(b, x, z, radius, height):
    """Distillation column: a tall thin vessel on a plinth, platform rings every 9 m, a ladder, a domed top, a pipe
    down its side, red light on top when tall."""
    rng = b.rng
    swatch = rng.choice(("steel", "trim", "steel"))
    deck = radius + 1.2
    cap = height + radius * 0.5
    b.span(x - radius - 0.8, x + radius + 0.8, 0.0, 1.2, z - radius - 0.8, z + radius + 0.8, "concrete", L01)
    b.cylinder((x, 1.2, z), (x, height, z), radius, swatch, segments=16, at=L01)
    b.cylinder((x, height, z), (x, cap, z), radius, swatch, segments=16, at=L01, end_radius=radius * 0.3)
    b.cylinder((x, 0.0, z), (x, height, z), radius, swatch, segments=6, at=L2)
    b.cylinder((x, height, z), (x, cap, z), radius, swatch, segments=6, at=L2, end_radius=radius * 0.3)
    y = 8.0
    while y < height - 2.0:
        b.cylinder((x, y - 0.25, z), (x, y, z), deck, "metal", segments=16)
        if rng.random() < 0.5:
            b.box((x + deck - 0.3, y + 0.3, z), (0.3, 0.3, 0.3), "sodium")
        y += 9.0
    for k in (0.25, 0.55, 0.85):
        yy = 1.2 + (height - 1.2) * k
        b.cylinder((x, yy, z), (x, yy + 0.4, z), radius + 0.06, "metal", segments=16)
    kit.ladder(b, x, z + radius - 0.05, 1.2, height, (0.0, 1.0))
    px = x + radius + 0.6
    b.cylinder((px, 1.2, z), (px, height - 1.0, z), 0.25, "metal", segments=8)
    b.cylinder((px, height - 1.0, z), (x + radius * 0.5, height - 1.0, z), 0.25, "metal", segments=8)
    if cap >= b.style["obstruction_height"]:
        b.box((x, cap + 0.15, z), (0.4, 0.4, 0.4), "warning", L01)
    b.round_collider(x, z, deck, 0.0, cap)


def _cooling_tower(b, x, z, radius, height):
    """Hyperbolic cooling tower: a ring of crossed legs, the shell narrowing to a waist and flaring to the lip, dark
    inside the rim, red lights on the lip."""
    leg_h = 4.0
    mid_y, waist_y = height * 0.4, height * 0.72
    mid_r, waist_r, top_r = radius * 0.8, radius * 0.66, radius * 0.74
    b.cylinder((x, 0.0, z), (x, 0.6, z), radius * 1.03, "concrete", segments=28, at=L01)
    for k in range(16):
        a, a2 = math.tau * k / 16, math.tau * (k + 0.5) / 16
        b.beam((x + math.cos(a) * radius * 0.96, 0.3, z + math.sin(a) * radius * 0.96),
               (x + math.cos(a2) * radius * 0.96, leg_h + 0.4, z + math.sin(a2) * radius * 0.96), 0.5, "concrete", L01)
    b.cylinder((x, leg_h, z), (x, mid_y, z), radius, "concrete", segments=28, at=L01, end_radius=mid_r)
    b.cylinder((x, mid_y, z), (x, waist_y, z), mid_r, "concrete", segments=28, at=L01, end_radius=waist_r)
    b.cylinder((x, waist_y, z), (x, height, z), waist_r, "concrete", segments=28, at=L01, end_radius=top_r,
               cap_end="soot")
    b.cylinder((x, height - 7.0, z), (x, height, z), top_r - (top_r - waist_r) * 7.0 / (height - waist_y) + 0.06,
               "soot", segments=28, at=L01, end_radius=top_r + 0.06)
    b.cylinder((x, 0.0, z), (x, mid_y, z), radius, "concrete", segments=8, at=L2, end_radius=mid_r)
    b.cylinder((x, mid_y, z), (x, height, z), mid_r, "concrete", segments=8, at=L2, end_radius=top_r, cap_end="soot")
    for angle in (45, 135, 225, 315):
        a = math.radians(angle)
        b.box((x + math.cos(a) * (top_r + 0.1), height + 0.2, z + math.sin(a) * (top_r + 0.1)), (0.4, 0.4, 0.4),
              "warning", L01)
    b.round_collider(x, z, radius, 0.0, mid_y)
    b.round_collider(x, z, mid_r, mid_y, height)


def _sphere_tank(b, x, z, radius):
    """Gas sphere on legs, with an equator band and a stair tower beside it."""
    y = radius + 2.0
    b.ico((x, y, z), radius, "trim", at=L01, subdivisions=2)
    b.cylinder((x, 0.0, z), (x, y + radius, z), radius * 0.85, "trim", segments=6, at=L2, end_radius=radius * 0.4)
    b.cylinder((x, y - 0.3, z), (x, y + 0.3, z), radius * 1.01, "metal", segments=24, at=L01)
    for k in range(8):
        a = math.tau * (k + 0.5) / 8
        lx, lz = x + math.cos(a) * radius * 0.9, z + math.sin(a) * radius * 0.9
        b.span(lx - 0.3, lx + 0.3, 0.0, y, lz - 0.3, lz + 0.3, "metal", L01)
        b.span(lx - 0.5, lx + 0.5, 0.0, 0.4, lz - 0.5, lz + 0.5, "concrete", L01)
    b.span(x - radius - 0.8, x + radius + 0.8, 0.0, 0.3, z - radius - 0.8, z + radius + 0.8, "concrete", L01)
    b.round_collider(x, z, radius, 0.0, y + radius)


def _flare(b, x, z, height):
    """Flare stack: a thin steel stack held by three guy braces, a platform near the top and a burning tip."""
    b.span(x - 1.5, x + 1.5, 0.0, 1.0, z - 1.5, z + 1.5, "concrete", L01)
    b.cylinder((x, 1.0, z), (x, height, z), 0.7, "steel", segments=8, at=L01, end_radius=0.55)
    b.cylinder((x, 0.0, z), (x, height + 1.2, z), 0.7, "steel", segments=6, at=L2, end_radius=0.55)
    b.cylinder((x, height, z), (x, height + 1.2, z), 0.85, "soot", segments=8, at=L01)
    b.cylinder((x, height - 3.2, z), (x, height - 3.0, z), 1.8, "metal", segments=12, at=L01)
    for angle in (90, 210, 330):
        a = math.radians(angle)
        b.beam((x + math.cos(a) * 5.0, 0.15, z + math.sin(a) * 5.0), (x, height * 0.55, z), 0.2, "metal", L01)
    # The flame: an amber cone over the tip (glows), thin (no collider above the tip).
    b.cylinder((x, height + 1.2, z), (x, height + 5.0, z), 0.8, "flame", segments=8, at=L01, end_radius=0.08)
    b.light("flame", (x, height + 4.0, z), "#ff8a2a", 45.0, 3.0)
    b.box((x, height - 2.8, z + 1.6), (0.4, 0.4, 0.4), "warning", L01)
    b.add_thin(x, z, height + 5.2)
    b.round_collider(x, z, 1.0, 0.0, height + 1.2)


def _containers(b, x, z, cols, rows, stacks):
    """Shipping containers (12 x 2.5 x 2.6 m, long side along x) in a block: cols along x, rows along z, stacked up
    to `stacks` high (fewer at random), mixed colours. One collider per column of the block."""
    rng = b.rng
    length, width, height, gap = 12.2, 2.45, 2.6, 0.3
    colours = ("rust", "primer", "steel", "metal", "hazard", "trim")
    x0 = x - (cols * (length + gap) - gap) / 2
    z0 = z - (rows * (width + gap) - gap) / 2
    for i in range(cols):
        cx = x0 + i * (length + gap) + length / 2
        top = 0.0
        for j in range(rows):
            cz = z0 + j * (width + gap) + width / 2
            count = rng.randint(1, stacks)
            for k in range(count):
                b.box((cx, k * height + height / 2, cz), (length - 0.1 * (k % 2), height, width), rng.choice(colours), L01)
            top = max(top, count * height)
        b.span(cx - length / 2, cx + length / 2, 0.0, top, z0, z0 + rows * (width + gap) - gap, "rust", L2)
        b.collider(cx - length / 2, cx + length / 2, 0.0, top, z0, z0 + rows * (width + gap) - gap)
