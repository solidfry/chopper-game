"""
Building check (generate.py --audit, and after every build). Works on the builder meshes in Unity space.

- Floating: every piece must touch the ground or, through other pieces, something on the ground.
- Below ground: nothing under y = 0.
- Plot: colliders inside the plot; the mesh may hang over the street by at most OVERHANG (awnings, signs).
- Invisible walls: a collider must not reach outside the visible mesh.
- Uncovered: solid parts more than UNCOVERED metres above or beside every collider (a chopper would fly through
  them). Thin masts and spires (Building.thin) are allowed.
- LODs: LOD2 must have the collider height (no pop in the skyline); triangle budgets per LOD (style "budgets").
- Clear zones: nothing may stand inside the circle of a RoofPad or Roof marker (landing, objectives).
"""
import math

import bmesh
from mathutils.bvhtree import BVHTree

TOUCH = 0.02
OVERHANG = 2.2
UNCOVERED = 2.6
ZONE_CLEARANCE = 0.3


def _islands(bm):
    bm.faces.ensure_lookup_table()
    seen, islands = set(), []
    for face in bm.faces:
        if face.index in seen:
            continue
        stack, island = [face], []
        seen.add(face.index)
        while stack:
            current = stack.pop()
            island.append(current.index)
            for vert in current.verts:
                for other in vert.link_faces:
                    if other.index not in seen:
                        seen.add(other.index)
                        stack.append(other)
        islands.append(island)
    return islands


def _root(parent, i):
    while parent[i] != i:
        parent[i] = parent[parent[i]]
        i = parent[i]
    return i


def triangles(bm):
    return sum(len(face.verts) - 2 for face in bm.faces)


def _floating(bm):
    islands = _islands(bm)
    if not islands:
        return []
    owner = {}
    for n, island in enumerate(islands):
        for index in island:
            owner[index] = n
    verts = [v.co.copy() for v in bm.verts]
    full = BVHTree.FromBMesh(bm)
    ground = len(islands)
    parent = list(range(len(islands) + 1))
    for n, island in enumerate(islands):
        faces = [bm.faces[i] for i in island]
        points = {v for f in faces for v in f.verts}
        if min(v.co.y for v in points) <= TOUCH:
            parent[_root(parent, n)] = _root(parent, ground)
        local = BVHTree.FromPolygons(verts, [[v.index for v in f.verts] for f in faces])
        others = {owner[b] for _a, b in local.overlap(full) if owner[b] != n}
        for vert in points:
            for _co, _normal, index, _dist in full.find_nearest_range(vert.co, TOUCH):
                if owner[index] != n:
                    others.add(owner[index])
        for other in others:
            parent[_root(parent, n)] = _root(parent, other)
    grounded = _root(parent, ground)
    groups = {}
    for n in range(len(islands)):
        root = _root(parent, n)
        if root != grounded:
            groups.setdefault(root, []).append(n)
    problems = []
    for members in groups.values():
        points = [v.co for n in members for i in islands[n] for v in bm.faces[i].verts]
        low = [min(p[k] for p in points) for k in range(3)]
        high = [max(p[k] for p in points) for k in range(3)]
        centre = [(a + b) / 2 for a, b in zip(low, high)]
        size = [b - a for a, b in zip(low, high)]
        problems.append(f"floating   centre ({centre[0]:+.1f}, {centre[1]:+.1f}, {centre[2]:+.1f})  size "
                        f"({size[0]:.2f}, {size[1]:.2f}, {size[2]:.2f})  {len(members)} pieces")
    return problems


def _corners(centre, size, yaw):
    a = math.radians(yaw)
    c, s = math.cos(a), math.sin(a)
    out = []
    for dx in (-0.5, 0.5):
        for dz in (-0.5, 0.5):
            x, z = dx * size[0], dz * size[2]
            out.append((centre[0] + x * c + z * s, centre[2] - x * s + z * c))
    return out


class _Grid:
    """Items in xz cells. `place(item)` = (x, z, reach): the item is listed in every cell its reach touches."""

    CELL = 16.0

    def __init__(self, items, place):
        self.cells = {}
        for item in items:
            x, z, reach = place(item)
            for i in range(int(math.floor((x - reach) / self.CELL)), int(math.floor((x + reach) / self.CELL)) + 1):
                for k in range(int(math.floor((z - reach) / self.CELL)), int(math.floor((z + reach) / self.CELL)) + 1):
                    self.cells.setdefault((i, k), []).append(item)

    def near(self, x, z):
        return self.cells.get((int(math.floor(x / self.CELL)), int(math.floor(z / self.CELL))), ())


def _inside(collider, point, grow_side, grow_up):
    centre, size, yaw = collider
    a = math.radians(yaw)
    x, z = point[0] - centre[0], point[2] - centre[2]
    lx, lz = x * math.cos(a) - z * math.sin(a), x * math.sin(a) + z * math.cos(a)  # world -> local (Unity yaw)
    return (abs(lx) <= size[0] / 2 + grow_side and abs(lz) <= size[2] / 2 + grow_side
            and centre[1] - size[1] / 2 - 0.05 <= point[1] <= centre[1] + size[1] / 2 + grow_up)


def check(b):
    """Problem lines for one Building (empty = fine)."""
    problems = []
    bm = b.lods[0].bm.copy()
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.normal_update()
    if not getattr(b, "skip_floating", False):
        problems += _floating(bm)

    points = [v.co.copy() for v in bm.verts]
    low = min(p.y for p in points)
    if low < b.ground_level - 0.01:
        problems.append(f"below      ground by {b.ground_level - low:.2f} m")
    plot = b.plot
    over = max(max(plot.x0 - p.x, p.x - plot.x1, plot.z0 - p.z, p.z - plot.z1) for p in points)
    if over > OVERHANG:
        problems.append(f"plot       mesh hangs {over:.1f} m over the plot edge (limit {OVERHANG})")
    bounds = [(min(p[k] for p in points), max(p[k] for p in points)) for k in range(3)]
    collider_top = 0.0
    for collider in b.colliders:
        centre, size, yaw = collider
        collider_top = max(collider_top, centre[1] + size[1] / 2)
        for x, z in _corners(centre, size, yaw):
            if not plot.contains(x, z, 0.05):
                problems.append(f"plot       collider at ({centre[0]:+.1f}, {centre[2]:+.1f}) leaves the plot")
                break
            if not (bounds[0][0] - 0.1 <= x <= bounds[0][1] + 0.1 and bounds[2][0] - 0.1 <= z <= bounds[2][1] + 0.1):
                problems.append(f"wall       collider at ({centre[0]:+.1f}, {centre[2]:+.1f}) reaches outside the mesh")
                break
        if centre[1] + size[1] / 2 > bounds[1][1] + 0.1:
            problems.append(f"wall       collider at ({centre[0]:+.1f}, {centre[2]:+.1f}) is higher than the mesh")

    # Grid of colliders (and thin parts) by xz cell: big road and shanty parts have thousands of each, and testing
    # every vertex against every collider took most of an export (5 Nov 2026).
    grid = _Grid(b.colliders, lambda c: (c[0][0], c[0][2], 0.5 * math.hypot(c[1][0], c[1][2]) + UNCOVERED))
    thin = _Grid(b.thin, lambda t: (t[0], t[1], t[3] if len(t) > 3 else 1.3))
    uncovered = []
    for p in points:
        if p.y < UNCOVERED:
            continue
        if any(math.hypot(p.x - t[0], p.z - t[1]) < (t[3] if len(t) > 3 else 1.3) and p.y <= t[2] for t in thin.near(p.x, p.z)):
            continue
        if not any(_inside(c, p, UNCOVERED, UNCOVERED) for c in grid.near(p.x, p.z)):
            uncovered.append(p)
    if uncovered:
        top = max(uncovered, key=lambda p: p.y)
        problems.append(f"uncovered  {len(uncovered)} points far from every collider, highest at "
                        f"({top.x:+.1f}, {top.y:+.1f}, {top.z:+.1f})")

    for kind, index, position, radius in b.markers:
        if radius <= 0:
            continue
        blockers = [p for p in points if position[1] + ZONE_CLEARANCE < p.y < position[1] + 40.0
                    and math.hypot(p.x - position[0], p.z - position[2]) < radius]
        if blockers:
            near = min(blockers, key=lambda p: math.hypot(p.x - position[0], p.z - position[2]))
            problems.append(f"zone       {kind}_{index} (r {radius:.1f} m) has {len(blockers)} points inside, nearest "
                            f"({near.x:+.1f}, {near.y:+.1f}, {near.z:+.1f})")
    bm.free()

    budgets = getattr(b, "budgets", None) or b.style["budgets"]
    counts = [triangles(level.bm) for level in b.lods]
    if counts[0] > budgets["lod0"]:
        problems.append(f"budget     LOD0 {counts[0]} triangles (limit {budgets['lod0']})")
    if counts[1] > counts[0] * budgets["lod1"]:
        problems.append(f"budget     LOD1 {counts[1]} triangles (limit {budgets['lod1']:.0%} of LOD0 = "
                        f"{int(counts[0] * budgets['lod1'])})")
    if counts[2] > budgets["lod2"]:
        problems.append(f"budget     LOD2 {counts[2]} triangles (limit {budgets['lod2']})")
    lod2 = [v.co.y for v in b.lods[2].bm.verts]
    if lod2 and b.type != "props" and abs(max(lod2) - collider_top) > 1.5:
        problems.append(f"lod        LOD2 top {max(lod2):.1f} m, colliders reach {collider_top:.1f} m (skyline pop)")
    return problems


def run(building_list):
    total = 0
    for b in building_list:
        problems = check(b)
        counts = [triangles(level.bm) for level in b.lods]
        status = "ok" if not problems else f"{len(problems)} problems"
        print(f"{b.name:<14} tris {counts[0]:>6} / {counts[1]:>5} / {counts[2]:>4}  colliders {len(b.colliders):>2}  "
              f"markers {len(b.markers)}  {status}")
        for line in problems:
            print(f"    {line}")
        total += len(problems)
    return total
