"""
Geometry building blocks. Everything is built in **Unity space** (x = right, y = up, z = forward, metres) and mapped to
Blender space only when a mesh is finished (see `to_blender`). The FBX exporter maps it back, so the game sees the same
numbers that are written here and in the manifest.
"""
import math

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree


# Form (art direction, style key "form"): how soft the shapes are. Set by configure() before a build.
DEFAULT_FORM = dict(
    corner_segments=1,   # arc segments per section corner: 1 = a straight 45° cut, 3-4 = rounded corners
    loft_smooth=0,       # extra interpolated sections between two sections: curved silhouettes instead of kinks
    curve_detail=1.0,    # multiplier on cylinder and duct segments
    edge_radius=0.025,   # bevel width on hard edges (m)
    edge_segments=1,     # bevel segments: 1 = chamfer (soft-shaded with hardened normals), 2-3 = real fillets (costly)
    edge_profile=0.5,    # bevel profile: 0.5 = round, higher = fuller
    box_rounding=0.0,    # boxes: long edges rounded with this share of the smaller cross size (0 = square boxes)
    box_min=0.1,         # boxes thinner than this (m) stay square (slats, vents, bars: cheap)
    sharp_angle=35.0,    # soft forms: edges sharper than this shade hard (rounded corners step 30° or less)
)
FORM = dict(DEFAULT_FORM)


def configure(style):
    FORM.clear()
    FORM.update(DEFAULT_FORM)
    FORM.update(style.get("form") or {})


def _smooth_sections(sections, extra, tension=0.35):
    """Cardinal spline through the sections (point by point): `extra` new sections between each pair."""
    result = []
    count = len(sections)
    for i in range(count - 1):
        p0, p1, p2, p3 = sections[max(i - 1, 0)], sections[i], sections[i + 1], sections[min(i + 2, count - 1)]
        result.append(p1)
        for step in range(1, extra + 1):
            t = step / (extra + 1)
            t2, t3 = t * t, t * t * t
            h00, h10, h01, h11 = 2 * t3 - 3 * t2 + 1, t3 - 2 * t2 + t, -2 * t3 + 3 * t2, t3 - t2
            k = (1 - tension) / 2
            ring = []
            for a, b, c, d in zip(p0, p1, p2, p3):
                ring.append(tuple(h00 * b[n] + h10 * k * (c[n] - a[n]) + h01 * c[n] + h11 * k * (d[n] - b[n])
                                  for n in range(3)))
            result.append(ring)
    result.append(sections[-1])
    return result


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
        self._surface = None
        self._covered = []
        self._everything = None

    # -- low level -------------------------------------------------------------------------------------------------
    def _verts(self, points):
        return [self.bm.verts.new(Vector(p)) for p in points]

    def _face(self, verts, swatch):
        face = self.bm.faces.new(verts)
        face[self.swatch] = swatch
        return face

    # -- surface placement ----------------------------------------------------------------------------------------
    def snapshot_surface(self):
        """Remembers the hull built so far; on_surface() lays parts flat on it. Call right after the main hull."""
        self.bm.normal_update()
        self._surface = BVHTree.FromBMesh(self.bm)
        self._covered = []

    def _tree(self, everything):
        """The hull snapshot, or (everything=True) a tree of every face built so far (rebuilt when faces were added)."""
        if not everything:
            if self._surface is None:
                raise RuntimeError("snapshot_surface() first")
            return self._surface
        count = len(self.bm.faces)
        if self._everything is None or self._everything[0] != count:
            self._everything = (count, BVHTree.FromBMesh(self.bm))
        return self._everything[1]

    def hit(self, origin, direction, everything=False, distance=100.0):
        """First point where a ray meets the hull (or everything): (point, normal pointing back at the ray) or
        (None, None)."""
        direction = Vector(direction).normalized()
        point, normal, _index, _distance = self._tree(everything).ray_cast(Vector(origin), direction, distance)
        if point is None:
            return None, None
        normal = normal.normalized()
        if normal.dot(direction) > 0:
            normal = -normal
        return point, normal

    def hull_x(self, side, y, z, default=None, everything=False):
        """|x| of the outer surface on the right (side 1) or left (-1) at height y, forward position z."""
        point, _normal = self.hit((side * 50.0, y, z), (-side, 0.0, 0.0), everything)
        return abs(point.x) if point is not None else default

    def hull_y(self, x, z, top=True, default=None, everything=False):
        """y of the top (or bottom) surface at (x, z)."""
        point, _normal = self.hit((x, 50.0 if top else -50.0, z), (0.0, -1.0 if top else 1.0, 0.0), everything)
        return point.y if point is not None else default

    def lay(self, origin, direction, size, swatch, forward=(0.0, 0.0, 1.0), lift=0.0, embed=0.006, everything=False):
        """A thin part laid flat where a ray from `origin` along `direction` meets the surface, turned to its slope.
        size = (thickness along the surface normal, width across, length along `forward` projected on the surface).
        Use forward (0, 1, 0) on front and back faces. Returns the hit point, or None (nothing placed)."""
        point, n = self.hit(origin, direction, everything)
        if point is None:
            return None
        hint = Vector(forward)
        along = hint - n * hint.dot(n)
        if along.length < 1e-4:
            along = Vector((0.0, 1.0, 0.0)) - n * n.y
        along.normalize()
        across = along.cross(n)
        rotation = Matrix((n, across, along)).transposed()  # columns: thickness along the normal, width, length
        self.box(tuple(point + n * (size[0] / 2 - embed + lift)), size, swatch, rotation=rotation)
        return point

    def on_surface(self, side, y, z, size, swatch, avoid=False, lift=0.0, embed=0.006, register=True):
        """A thin part (size = thickness, height, length) laid flat on the hull's right (side 1) or left (-1) side at
        height y, forward position z: a ray from outside finds the surface and its slope, and the part follows it.
        avoid: skip it when it overlaps an earlier registered part. lift: extra distance off the surface (a second
        layer, ladder rungs). Returns True when placed."""
        rect = (side, y - size[1] / 2, y + size[1] / 2, z - size[2] / 2, z + size[2] / 2)
        if avoid and any(other[0] == side and rect[1] < other[2] + 0.02 and other[1] < rect[2] + 0.02
                         and rect[3] < other[4] + 0.02 and other[3] < rect[4] + 0.02 for other in self._covered):
            return False
        placed = self.lay((side * 50.0, y, z), (-side, 0.0, 0.0), size, swatch, lift=lift, embed=embed)
        if placed is not None and register:
            self._covered.append(rect)
        return placed is not None

    # -- primitives ------------------------------------------------------------------------------------------------
    def loft(self, sections, swatch, cap_front=None, cap_back=None):
        """Connects rings of points (same count each) in order; caps both ends.

        sections: list of rings, each a list of (x, y, z). cap_front/cap_back: swatch for the first/last cap
        (default = swatch). With form "loft_smooth", three or more sections get curved in between."""
        if FORM["loft_smooth"] > 0 and len(sections) >= 3:
            sections = _smooth_sections(sections, int(FORM["loft_smooth"]))
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
        ('right','left','top','bottom','front','back'). With form box_rounding, boxes that are not thin get all edges
        and corners rounded (a moulded part, not a brick)."""
        cross = sorted(size)[:2]
        if swatches is None and FORM["box_rounding"] > 0 and cross[0] >= FORM["box_min"]:
            self._rounded_box(centre, size, swatch, rotation)
            return
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

    def _rounded_box(self, centre, size, swatch, rotation):
        """Box with every edge and corner rounded (a bmesh bevel on its own 12 edges): reads as a moulded part,
        not a brick. The radius is a share of the smallest side."""
        cx, cy, cz = centre
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        corners = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
                   (-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)]
        if rotation is not None:
            corners = [tuple(rotation @ Vector(c)) for c in corners]
        v = self._verts([(cx + x, cy + y, cz + z) for x, y, z in corners])
        faces = [self._face(quad, swatch) for quad in (
            [v[0], v[3], v[2], v[1]], [v[4], v[5], v[6], v[7]], [v[0], v[1], v[5], v[4]],
            [v[3], v[7], v[6], v[2]], [v[0], v[4], v[7], v[3]], [v[1], v[2], v[6], v[5]])]
        edges = list({edge for face in faces for edge in face.edges})
        radius = min(size) * FORM["box_rounding"]
        bmesh.ops.bevel(self.bm, geom=edges + v, offset=radius, offset_type="OFFSET",
                        segments=max(2, int(FORM["corner_segments"]) - 1), profile=0.5, affect="EDGES",
                        clamp_overlap=True)

    def block(self, centre, size, swatch, taper=0.85, taper_depth=0.95, rotation=None, top=None, front=None, back=None):
        """Box whose top face is smaller than its base (taper across x, taper_depth along z): the blocky,
        slightly pyramidal element of the assault body. Optional swatches for the top, front and back faces."""
        cx, cy, cz = centre
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        tx, tz = hx * taper, hz * taper_depth
        corners = [(-hx, -hy, -hz), (hx, -hy, -hz), (tx, hy, -tz), (-tx, hy, -tz),
                   (-hx, -hy, hz), (hx, -hy, hz), (tx, hy, tz), (-tx, hy, tz)]
        if rotation is not None:
            corners = [tuple(rotation @ Vector(c)) for c in corners]
        v = self._verts([(cx + x, cy + y, cz + z) for x, y, z in corners])
        faces = [
            self._face([v[0], v[3], v[2], v[1]], swatch if back is None else back),
            self._face([v[4], v[5], v[6], v[7]], swatch if front is None else front),
            self._face([v[0], v[1], v[5], v[4]], swatch),
            self._face([v[3], v[7], v[6], v[2]], swatch if top is None else top),
            self._face([v[0], v[4], v[7], v[3]], swatch),
            self._face([v[1], v[2], v[6], v[5]], swatch),
        ]
        if FORM["box_rounding"] > 0 and min(size) >= FORM["box_min"]:
            edges = list({edge for face in faces for edge in face.edges})
            bmesh.ops.bevel(self.bm, geom=edges + v, offset=min(size) * FORM["box_rounding"] * 0.6,
                            offset_type="OFFSET", segments=max(2, int(FORM["corner_segments"]) - 1), profile=0.5,
                            affect="EDGES", clamp_overlap=True)

    def duct(self, centre, outer, inner, height, swatch, segments=24, inner_swatch=None):
        """Upright ring (a ducted-fan shroud): annulus of radii outer/inner, `height` tall, centred on `centre`."""
        segments = max(segments, round(segments * FORM["curve_detail"]))
        cx, cy, cz = centre
        rings = []
        for y in (cy - height / 2, cy + height / 2):
            for radius in (outer, inner):
                rings.append([self.bm.verts.new(Vector((cx + radius * math.cos(t), y, cz + radius * math.sin(t))))
                              for t in (2 * math.pi * i / segments for i in range(segments))])
        bottom_out, bottom_in, top_out, top_in = rings
        for i in range(segments):
            j = (i + 1) % segments
            self._face([bottom_out[i], bottom_out[j], top_out[j], top_out[i]], swatch)
            self._face([bottom_in[i], top_in[i], top_in[j], bottom_in[j]], swatch if inner_swatch is None else inner_swatch)
            self._face([top_out[i], top_out[j], top_in[j], top_in[i]], swatch)
            self._face([bottom_out[i], bottom_in[i], bottom_in[j], bottom_out[j]], swatch)

    def cylinder(self, start, end, radius, swatch, segments=12, end_radius=None, cap_start=None, cap_end=None,
                 phase=0.0):
        """Cylinder or cone from `start` to `end` (Unity points)."""
        segments = max(segments, round(segments * FORM["curve_detail"]))
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
        if FORM["curve_detail"] >= 1.4:
            subdivisions += 1
        result = bmesh.ops.create_icosphere(self.bm, subdivisions=subdivisions, radius=radius)
        for vert in result["verts"]:
            vert.co += Vector(centre)
        for face in {f for v in result["verts"] for f in v.link_faces}:
            face[self.swatch] = swatch

    def fin(self, outline_zy, x, thickness, swatch, cant=0.0):
        """Flat plate from a 2D outline in the (z, y) plane, `thickness` across x, centred on `x`. cant: sideways
        lean, metres of x per metre of height above the outline's lowest point (an end plate leaning out)."""
        y0 = min(y for _z, y in outline_zy)
        front = [(x + cant * (y - y0) - thickness / 2, y, z) for z, y in outline_zy]
        back = [(x + cant * (y - y0) + thickness / 2, y, z) for z, y in outline_zy]
        self.loft([front, back], swatch)

    # -- finish ----------------------------------------------------------------------------------------------------
    def to_object(self, name, collection, bevel_width=None, detail=None):
        """Unity -> Blender mapping, normals out, a bevel on hard edges with hardened normals (form: radius, segments,
        profile; flat faces stay flat, the bevel shades as a soft rounded edge), then the surface detail node group
        (detail = (seed, values): panels, nuts, greebles); all baked into the mesh."""
        if bevel_width is None:
            bevel_width = FORM["edge_radius"]
        for vert in self.bm.verts:
            vert.co = to_blender(vert.co)
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=1e-5)
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)

        mesh = bpy.data.meshes.new(name + "_raw")
        self.bm.to_mesh(mesh)
        self.bm.free()

        raw = bpy.data.objects.new(name + "_raw", mesh)
        collection.objects.link(raw)
        segments = max(1, int(FORM["edge_segments"]))
        soft = FORM["corner_segments"] > 1 or FORM["loft_smooth"] > 0
        if soft:
            # Smooth shading everywhere; the bevel's hardened normals keep flat faces flat.
            mesh.shade_smooth()
        if bevel_width > 0:
            bevel = raw.modifiers.new("Bevel", "BEVEL")
            bevel.width = bevel_width
            bevel.segments = segments
            bevel.profile = FORM["edge_profile"]
            bevel.limit_method = "ANGLE"
            # Soft forms: rounded corners already come in steps of 45° or less; bevel only what is still sharp.
            bevel.angle_limit = math.radians(50 if soft else 35)
            bevel.harden_normals = soft
        if detail is not None:
            from . import detail as surface_detail
            surface_detail.add_modifier(raw, *detail)

        depsgraph = bpy.context.evaluated_depsgraph_get()
        baked = bpy.data.meshes.new_from_object(raw.evaluated_get(depsgraph))
        baked.name = name
        bpy.data.objects.remove(raw)
        bpy.data.meshes.remove(mesh)

        if soft:
            baked = _weighted_normals(baked, FORM["sharp_angle"], collection)
        else:
            # Hard look (form defaults): smooth panels, crisp 45° chamfers.
            baked.shade_smooth()
            baked.set_sharp_from_angle(angle=math.radians(35))

        obj = bpy.data.objects.new(name, baked)
        collection.objects.link(obj)
        return obj


def _weighted_normals(mesh, sharp_angle, collection):
    """Game-ready shading for the final mesh (Blender's old "auto smooth" + weighted normals): edges sharper than
    sharp_angle shade hard, and every face's normal leans to the largest face round it, so flat panels shade flat
    (no melted look at insets and hard edges) while rounded corners and curves stay smooth."""
    name = mesh.name
    if "custom_normal" in mesh.attributes:
        mesh.attributes.remove(mesh.attributes["custom_normal"])
    mesh.set_sharp_from_angle(angle=math.radians(sharp_angle))
    holder = bpy.data.objects.new(name + "_normals", mesh)
    collection.objects.link(holder)
    weighted = holder.modifiers.new("Weighted Normal", "WEIGHTED_NORMAL")
    weighted.keep_sharp = True
    weighted.mode = "FACE_AREA"
    weighted.weight = 50
    depsgraph = bpy.context.evaluated_depsgraph_get()
    result = bpy.data.meshes.new_from_object(holder.evaluated_get(depsgraph))
    bpy.data.objects.remove(holder)
    bpy.data.meshes.remove(mesh)
    result.name = name
    return result


def _rounded_rect(hw, hh, rx, ry, n):
    """Rounded rectangle (half sizes hw, hh; corner radii rx, ry; n arc segments per corner) as (a, b) points."""
    rx, ry = min(rx, hw * 0.95), min(ry, hh * 0.95)
    corners = ((hw - rx, -hh + ry, 0.0, -90.0), (-hw + rx, -hh + ry, -90.0, -180.0),
               (-hw + rx, hh - ry, 180.0, 90.0), (hw - rx, hh - ry, 90.0, 0.0))
    points = []
    for cx, cy, start, end in corners:
        for step in range(n + 1):
            angle = math.radians(start + (end - start) * step / n)
            points.append((cx + rx * math.cos(angle), cy + ry * math.sin(angle)))
    return points


def chamfer_ring(z, width, height, chamfer=0.25, y=0.0, x=0.0, top_scale=1.0):
    """Section at forward position z: a rectangle whose corners are cut (form corner_segments 1: eight points, a 45°
    chamfer) or rounded (2+: elliptical arcs, the chamfer is the corner radius). top_scale < 1 narrows the top.
    Every ring has 4 x (corner_segments + 1) points, so rings of one build always loft together."""
    hw, hh = width / 2, height / 2
    cw, ch = hw * chamfer, hh * chamfer
    tw = hw * top_scale
    tcw = cw * top_scale
    n = max(1, int(FORM["corner_segments"]))
    corners = (
        (x + hw - cw, y - hh + ch, cw, 0.0, -90.0),            # bottom right: right side -> bottom
        (x - hw + cw, y - hh + ch, cw, -90.0, -180.0),         # bottom left: bottom -> left side
        (x - tw + tcw, y + hh - ch, tcw, 180.0, 90.0),         # top left: left side -> top
        (x + tw - tcw, y + hh - ch, tcw, 90.0, 0.0),           # top right: top -> right side
    )
    points = []
    for cx, cy, rx, start, end in corners:
        for step in range(n + 1):
            angle = math.radians(start + (end - start) * step / n)
            points.append((cx + rx * math.cos(angle), cy + ch * math.sin(angle), z))
    return points


def rot_y(degrees):
    return Matrix.Rotation(math.radians(degrees), 3, "Y")


def rot_x(degrees):
    return Matrix.Rotation(math.radians(degrees), 3, "X")


def rot_z(degrees):
    return Matrix.Rotation(math.radians(degrees), 3, "Z")
