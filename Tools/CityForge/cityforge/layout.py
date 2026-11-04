"""
City layout: fills the basin floor with buildings. Zones by distance from the core (megatowers and landmarks in the
core, towers round it, blocks, slums at the edge, industrial on the canyon side), rows of plots with streets between,
a corridor for the storm-drain channel with bridges over it. Variants of the base buildings (new seeds and numbers)
give variety; each variant is one more FBX, used many times (instances).

Coordinates: Unity space, origin = the city centre on the ground (the same origin as the terrain).
"""
import json
import math
import random

from . import specs as spec_list
from .kit import Rect

STREET = 16.0
CITY = (1000.0, 800.0)        # city size x, z (m)
CHANNEL_SHARE = 0.22          # channel corridor at z = this share of the city depth (towards +Z, the front)
CORRIDOR = 84.0               # channel (56 m) + a road on each side
UNIQUE = ("Mega_Arcology", "Mega_Twins", "Landmark_Halo", "Mega_Needle", "Landmark_Orb", "Mega_Slab", "Landmark_Twist")
VARIANTS = {"tower": 6, "block": 6, "slum": 6, "industrial": 3}


def variant_specs(seed=11):
    """New specs from the base buildings: same type, new seed and new numbers (floors, shape, crown...)."""
    rng = random.Random(seed)
    bases = {}
    for spec in spec_list.BUILDINGS:
        bases.setdefault(spec["type"], []).append(spec)
    out = []
    for kind, count in VARIANTS.items():
        for k in range(count):
            base = bases[kind][k % len(bases[kind])]
            spec = json.loads(json.dumps(base))
            spec["name"] = f"{base['name']}_V{k + 1}"
            spec["seed"] = base["seed"] * 10 + k + 1
            spec["variantOf"] = base["name"]
            if kind == "tower":
                spec["tiers"] = [[sw, sd, max(3, int(round(floors * rng.uniform(0.7, 1.35)))), ox, oz]
                                 for sw, sd, floors, ox, oz in spec["tiers"]]
                spec["facade"] = rng.choice(("ribbon", "grid"))
                spec["crown"] = rng.choice(("pad", "antennas", "sign", "spire"))
            elif kind == "block":
                spec["floors"] = rng.randint(4, 9)
                if spec["shape"] != "U":
                    spec["shape"] = rng.choice(("rect", "L"))
                spec["roof"] = rng.choice(("tanks", "tanks", "pad"))
                spec["fire_escape"] = rng.choice(("left", "right", None))
                spec["balconies"] = round(rng.uniform(0.1, 0.4), 2)
            elif kind == "slum":
                spec["base_floors"] = rng.randint(2, 5)
                spec["stacks"] = rng.randint(6, 13)
            out.append(spec)
    return out


class Layout:
    def __init__(self, building_specs, seed=5):
        self.rng = random.Random(seed)
        self.width, self.depth = CITY
        self.rect = Rect.centred(0.0, 0.0, self.width, self.depth)
        self.channel_z = self.depth * CHANNEL_SHARE
        self.specs = {spec["name"]: spec for spec in building_specs}
        self.pools = {}
        for spec in building_specs:
            if spec["name"] not in UNIQUE:
                self.pools.setdefault(spec["type"], []).append(spec)
        self.instances = []       # (name, (x, 0, z), yaw)
        self.plots = []           # (name, Rect)
        self.bridges = []         # x of each bridge over the channel
        self._fill()

    def _zone(self, x, z):
        """0 = core ... 1 = city edge (an ellipse over the city rectangle)."""
        return math.hypot(x / (self.width / 2), z / (self.depth / 2))

    def _pick(self, x, z, max_depth, uniques):
        r = self._zone(x, z)
        rng = self.rng
        if uniques and r < 0.42:
            spec = self.specs.get(uniques[0])
            if spec and spec["plot"][1] <= max_depth:
                return spec
        if r < 0.3:
            kinds = (("tower", 1.0),)
        elif r < 0.52:
            kinds = (("tower", 0.7), ("block", 0.3))
        elif r < 0.76:
            kinds = (("block", 0.7), ("tower", 0.1), ("slum", 0.2))
        elif x > self.width * 0.22:
            kinds = (("industrial", 0.5), ("slum", 0.3), ("block", 0.2))
        else:
            kinds = (("slum", 0.65), ("block", 0.2), ("industrial", 0.15))
        roll, total = rng.random(), 0.0
        kind = kinds[-1][0]
        for name, share in kinds:
            total += share
            if roll <= total:
                kind = name
                break
        choices = [s for s in self.pools.get(kind, []) if s["plot"][1] <= max_depth]
        return rng.choice(choices) if choices else None

    def _fill(self):
        half_w, half_d = self.width / 2, self.depth / 2
        corridor_top, corridor_bottom = self.channel_z + CORRIDOR / 2, self.channel_z - CORRIDOR / 2
        bands = [(half_d, corridor_top), (corridor_bottom, -half_d)]
        uniques = [name for name in UNIQUE if name in self.specs]
        for top, bottom in bands:
            front = top
            while front - bottom >= 18.0:
                x = -half_w
                row_depth = 0.0
                while True:
                    spec = self._pick(x + 30.0, front - 25.0, front - bottom, uniques)
                    if spec is None:
                        break
                    w, d = spec["plot"]
                    if x + w > half_w:
                        # Try a narrower building to close the row.
                        narrow = [s for s in self.pools.get("slum", []) + self.pools.get("block", [])
                                  if s["plot"][0] <= half_w - x and s["plot"][1] <= front - bottom]
                        if not narrow:
                            break
                        spec = self.rng.choice(narrow)
                        w, d = spec["plot"]
                    if spec["name"] in uniques:
                        uniques.remove(spec["name"])
                    centre = (x + w / 2, 0.0, front - d / 2)
                    yaw = 0.0 if spec["type"] in ("mega", "twins", "twist", "halo", "orb") or self.rng.random() < 0.6 \
                        else 180.0
                    self.instances.append((spec["name"], centre, yaw))
                    self.plots.append((spec["name"], Rect.centred(centre[0], centre[2], w, d)))
                    row_depth = max(row_depth, d)
                    x += w + STREET
                if row_depth == 0.0:
                    break
                front -= row_depth + STREET
        # Bridges over the channel where streets would cross it: every ~130 m.
        count = int(self.width // 130)
        self.bridges = [-half_w + (k + 0.5) * self.width / count for k in range(count)]
        self.missing = uniques

    def city_rect(self):
        plots = [rect for _name, rect in self.plots]
        return (min(r.x0 for r in plots), max(r.x1 for r in plots), min(r.z0 for r in plots), max(r.z1 for r in plots))

    def audit(self):
        problems = []
        for k, (name_a, a) in enumerate(self.plots):
            for name_b, b2 in self.plots[k + 1:]:
                if a.overlaps(b2, -0.01):
                    problems.append(f"layout     {name_a} and {name_b} overlap")
        corridor = Rect(-1e6, 1e6, self.channel_z - CORRIDOR / 2 + 1.0, self.channel_z + CORRIDOR / 2 - 1.0)
        for name, rect in self.plots:
            if rect.overlaps(corridor):
                problems.append(f"layout     {name} stands in the channel corridor")
        if self.missing:
            problems.append(f"layout     no room for {', '.join(self.missing)}")
        return problems


def bridges(layout, style):
    """Road bridges over the storm drain: deck, railings with lights, a pier in the channel. A Building, so it gets
    LODs, colliders and the audit like any other."""
    from .kit import ALL, L0, L01, Building

    b = Building(dict(name="City_Bridges", type="props", seed=77, plot=(layout.width + 40.0, layout.depth + 40.0)), style)
    b.ground_level = -8.0  # the piers stand on the channel floor
    z0, z1 = layout.channel_z - 31.0, layout.channel_z + 31.0
    for x in layout.bridges:
        w = 18.0
        b.span(x - w / 2, x + w / 2, -1.4, 0.15, z0, z1, "concrete", ALL, swatches={"top": "tar"})
        b.collider(x - w / 2, x + w / 2, -1.4, 0.15, z0, z1)
        for side in (-1, 1):
            rx = x + side * (w / 2 - 0.2)
            b.span(rx - 0.2, rx + 0.2, 0.15, 1.2, z0, z1, "trim", L01)
            for k in range(5):
                z = z0 + 3.0 + k * (z1 - z0 - 6.0) / 4
                b.span(rx - 0.15, rx + 0.15, 1.2, 1.35, z - 0.15, z + 0.15, "sodium", L0)
        b.span(x - 6.0, x + 6.0, -8.0, -1.4, layout.channel_z - 1.0, layout.channel_z + 1.0, "concrete", ALL)
        b.collider(x - 6.0, x + 6.0, -8.0, -1.4, layout.channel_z - 1.0, layout.channel_z + 1.0)
    return b
