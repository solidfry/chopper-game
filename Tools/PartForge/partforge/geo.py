"""
Geometry building blocks. Everything is built in **Unity space** (x = right, y = up, z = forward, metres) and mapped to
Blender space only when a mesh is finished (see `to_blender`). The FBX exporter maps it back, so the game sees the same
numbers that are written here and in the manifest.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector


def to_blender(p):
    """Unity (x right, y up, z forward) -> Blender (model faces -Y, right is -X, up is +Z).

    With the FBX exporter's default axes (forward -Z, up Y, bake space transform) and Unity's import, Blender -Y ends up
    as Unity +Z and Blender -X as Unity +X. It is a mirror, so normals are recalculated after the mapping."""
    return Vector((-p[0], -p[2], p[1]))


class Builder:
    """Collects faces in one bmesh, each face tagged with a palette swatch."""

    def __init__(self):
        self.bm = bmesh.new()
        self.swatch = self.bm.faces.layers.int.new("swatch")

    # -- low level -------------------------------------------------------------------------------------------------
    def _verts(self, points):
        return [self.bm.verts.new(Vector(p)) for p in points]

    def _face(self, verts, swatch):
        face = self.bm.faces.new(verts)
        face[self.swatch] = swatch
        return face

    # -- primitives ------------------------------------------------------------------------------------------------
    def loft(self, sections, swatch, cap_front=None, cap_back=None):
        """Connects rings of points (same count each) in order; caps both ends.

        sections: list of rings, each a list of (x, y, z). cap_front/cap_back: swatch for the first/last cap
        (default = swatch)."""
        rings = [self._verts(ring) for ring in sections]
        count = len(rings[0])
        for a, b in zip(rings, rings[1:]):
            for i in range(count):
                j = (i + 1) % count
                self._face([a[i], a[j], b[j], b[i]], swatch)
        self._face(list(reversed(rings[0])), swatch if cap_front is None else cap_front)
        self._face(rings[-1], swatch if cap_back is None else cap_back)

    def box(self, centre, size, swatch, rotation=None, swatches=None):
        """Axis box (optionally rotated about its centre). swatches: optional dict per side
        ('right','left','top','bottom','front','back')."""
        cx, cy, cz = centre
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        corners = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
                   (-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)]
        if rotation is not None:
            corners = [tuple(rotation @ Vector(c)) for c in corners]
        v = self._verts([(cx + x, cy + y, cz + z) for x, y, z in corners])
        sides = {
            "back": [v[0], v[3], v[2], v[1]], "front": [v[4], v[5], v[6], v[7]],
            "bottom": [v[0], v[1], v[5], v[4]], "top": [v[3], v[7], v[6], v[2]],
            "left": [v[0], v[4], v[7], v[3]], "right": [v[1], v[2], v[6], v[5]],
        }
        for name, verts in sides.items():
            self._face(verts, (swatches or {}).get(name, swatch))

    def cylinder(self, start, end, radius, swatch, segments=12, end_radius=None, cap_start=None, cap_end=None,
                 phase=0.0):
        """Cylinder or cone from `start` to `end` (Unity points)."""
        a, b = Vector(start), Vector(end)
        axis = (b - a).normalized()
        helper = Vector((0, 1, 0)) if abs(axis.y) < 0.9 else Vector((1, 0, 0))
        u = axis.cross(helper).normalized()
        w = axis.cross(u).normalized()
        r2 = radius if end_radius is None else end_radius

        def ring(centre, r):
            return [tuple(centre + (u * math.cos(t) + w * math.sin(t)) * r)
                    for t in (phase + 2 * math.pi * i / segments for i in range(segments))]

        self.loft([ring(a, radius), ring(b, r2)], swatch, cap_front=cap_start, cap_back=cap_end)

    def ico(self, centre, radius, swatch, subdivisions=1):
        """Low-poly ball (sensor, turret)."""
        result = bmesh.ops.create_icosphere(self.bm, subdivisions=subdivisions, radius=radius)
        for vert in result["verts"]:
            vert.co += Vector(centre)
        for face in {f for v in result["verts"] for f in v.link_faces}:
            face[self.swatch] = swatch

    def fin(self, outline_zy, x, thickness, swatch):
        """Flat plate from a 2D outline in the (z, y) plane, `thickness` across x, centred on `x`."""
        front = [(x - thickness / 2, y, z) for z, y in outline_zy]
        back = [(x + thickness / 2, y, z) for z, y in outline_zy]
        self.loft([front, back], swatch)

    # -- finish ----------------------------------------------------------------------------------------------------
    def to_object(self, name, collection, bevel_width=0.025):
        """Unity -> Blender mapping, normals out, a small bevel for hard-surface highlights, baked into the mesh."""
        for vert in self.bm.verts:
            vert.co = to_blender(vert.co)
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-5)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)

        mesh = bpy.data.meshes.new(name + "_raw")
        self.bm.to_mesh(mesh)
        self.bm.free()

        raw = bpy.data.objects.new(name + "_raw", mesh)
        collection.objects.link(raw)
        if bevel_width > 0:
            bevel = raw.modifiers.new("Bevel", "BEVEL")
            bevel.width = bevel_width
            bevel.segments = 1
            bevel.limit_method = "ANGLE"
            bevel.angle_limit = math.radians(35)
            bevel.harden_normals = False

        depsgraph = bpy.context.evaluated_depsgraph_get()
        baked = bpy.data.meshes.new_from_object(raw.evaluated_get(depsgraph))
        baked.name = name
        bpy.data.objects.remove(raw)
        bpy.data.meshes.remove(mesh)

        obj = bpy.data.objects.new(name, baked)
        collection.objects.link(obj)
        return obj


def chamfer_ring(z, width, height, chamfer=0.25, y=0.0, x=0.0, top_scale=1.0):
    """Eight-sided section (rectangle with cut corners) at forward position z. top_scale < 1 narrows the top."""
    hw, hh = width / 2, height / 2
    cw, ch = hw * chamfer, hh * chamfer
    tw = hw * top_scale
    tcw = cw * top_scale
    return [
        (x + hw, y - hh + ch, z), (x + hw - cw, y - hh, z), (x - hw + cw, y - hh, z), (x - hw, y - hh + ch, z),
        (x - tw, y + hh - ch, z), (x - tw + tcw, y + hh, z), (x + tw - tcw, y + hh, z), (x + tw, y + hh - ch, z),
    ]


def rot_y(degrees):
    return Matrix.Rotation(math.radians(degrees), 3, "Y")


def rot_x(degrees):
    return Matrix.Rotation(math.radians(degrees), 3, "X")


def rot_z(degrees):
    return Matrix.Rotation(math.radians(degrees), 3, "Z")
