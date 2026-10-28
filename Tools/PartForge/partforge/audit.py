"""
Alignment check (generate.py --audit): finds parts that do not sit on the model.

- Floating pieces: every primitive a builder adds is its own mesh island. Islands that touch or cut into each other
  (within TOUCH metres) are joined; every group that is not joined to the main hull floats in the air.
- Sockets: an attach point must be on the hull (or just under it, for hanging modules). Sockets further than
  SOCKET_GAP from the chassis surface are listed.

Works on the builder's mesh in Unity space (before the bevel and the surface detail), so positions match parts.py.
"""
import bmesh
from mathutils.bvhtree import BVHTree

from .palette import SWATCHES

TOUCH = 0.015
SOCKET_GAP = 0.12
# Sockets that hold a module in free air by design (rotor hubs sit on the mast top, armour is stats only).
FREE_SLOTS = {"Armour"}


def _islands(bm):
    """Face index lists, one per connected mesh island."""
    bm.verts.ensure_lookup_table()
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


def check(part):
    """Returns a list of problem lines for one part (empty = every piece sits on something)."""
    bm = part.builder.bm.copy()
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.normal_update()
    layer = bm.faces.layers.int.get("swatch")
    islands = _islands(bm)
    owner = {}
    for n, island in enumerate(islands):
        for index in island:
            owner[index] = n
    full = BVHTree.FromBMesh(bm)
    verts = [v.co.copy() for v in bm.verts]
    parent = list(range(len(islands)))

    for n, island in enumerate(islands):
        faces = [bm.faces[i] for i in island]
        local = BVHTree.FromPolygons(verts, [[v.index for v in f.verts] for f in faces])
        others = set()
        for _a, b in local.overlap(full):
            if owner[b] != n:
                others.add(owner[b])
        for vert in {v for f in faces for v in f.verts}:
            for _co, _normal, index, _dist in full.find_nearest_range(vert.co, TOUCH):
                if owner[index] != n:
                    others.add(owner[index])
        for other in others:
            parent[_root(parent, n)] = _root(parent, other)

    main = _root(parent, max(range(len(islands)), key=lambda n: len(islands[n])))
    groups = {}
    for n in range(len(islands)):
        root = _root(parent, n)
        if root != main:
            groups.setdefault(root, []).append(n)

    problems = []
    for members in groups.values():
        faces = [bm.faces[i] for n in members for i in islands[n]]
        points = [v.co for f in faces for v in f.verts]
        low = [min(p[k] for p in points) for k in range(3)]
        high = [max(p[k] for p in points) for k in range(3)]
        centre = [(a + b) / 2 for a, b in zip(low, high)]
        size = [b - a for a, b in zip(low, high)]
        gap = min((full.find_nearest(p)[3] for p in points), default=0.0)
        names = sorted({SWATCHES[f[layer]][0] for f in faces}) if layer is not None else []
        problems.append(f"floating  centre ({centre[0]:+.2f}, {centre[1]:+.2f}, {centre[2]:+.2f})  size "
                        f"({size[0]:.2f}, {size[1]:.2f}, {size[2]:.2f})  {len(members)} pieces  {'/'.join(names)}")

    if part.kind == "chassis":
        main_faces = [i for n in range(len(islands)) if _root(parent, n) == main for i in islands[n]]
        hull = BVHTree.FromPolygons(verts, [[v.index for v in bm.faces[i].verts] for i in main_faces])
        for slot, index, position, _scale in part.sockets:
            if slot in FREE_SLOTS:
                continue
            _co, _normal, _index, distance = hull.find_nearest(position)
            if distance is not None and distance > SOCKET_GAP:
                problems.append(f"socket    {slot}_{index} at ({position[0]:+.2f}, {position[1]:+.2f}, {position[2]:+.2f}) "
                                f"is {distance:.2f} m from the hull")
    bm.free()
    return problems


def run(parts_list):
    """Prints a report for every part; returns the number of problems."""
    total = 0
    for part in parts_list:
        problems = check(part)
        total += len(problems)
        print(f"{part.name:<24} {'ok' if not problems else f'{len(problems)} problems'}")
        for line in problems:
            print(f"    {line}")
    return total
