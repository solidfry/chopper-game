"""
Wear and grime for buildings: per-vertex masks baked from the finished mesh (headless, ray casts on its own BVH),
graded by a shader (Unity: Assets/Chopper/Art/Shaders/BuildingWear.hlsl; Blender previews: add_preview_nodes).

Vertex colour "Wear" (linear, point domain):
    R occlusion   ambient occlusion: share of short hemisphere rays (8, up to 6 m) that hit the building itself
    G splash      grime at the foot of walls and props: 1 at the surface below, fading out over 4 m
    B drip        rain-streak source: something stands above within 12 m (sills, ledges, awnings, upper tiers)
    A sky         sky exposure: share of upward rays (5, up to 60 m) that reach the sky; rain streaks and dust need it
                  (low-poly walls are all corners, so a per-vertex edge-wear mask would be 1 everywhere)
The shader makes the look from these and the world: streaks run down from drip spots, dust settles on ledges (the
biome's dust: sand, snow, ash), rain makes everything darker and glossier. Low-poly walls have few vertices, so the
masks are soft; the shader's noise adds the fine detail.
"""
import math

import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

AO_RAYS = [Vector(v).normalized() for v in ((0, 0, 1), (0.7, 0, 0.7), (-0.7, 0, 0.7), (0, 0.7, 0.7), (0, -0.7, 0.7),
                                            (0.5, 0.5, 0.3), (-0.5, -0.5, 0.3), (0.5, -0.5, 0.3))]
AO_DISTANCE = 6.0
SKY_RAYS = [Vector(v).normalized() for v in ((0, 0, 1), (0.7, 0, 0.7), (-0.7, 0, 0.7), (0, 0.7, 0.7), (0, -0.7, 0.7))]
SKY_DISTANCE = 60.0
SPLASH = 4.0
DRIP = 12.0


def _frame(normal):
    """Rotation taking +Z to the normal (for hemisphere rays round it)."""
    up = Vector((0, 0, 1))
    n = normal.normalized()
    if abs(n.dot(up)) > 0.999:
        return None if n.z > 0 else "flip"
    return up.rotation_difference(n).to_matrix()


def bake(obj):
    """Writes the "Wear" colour attribute on a mesh object (Blender space: +Z up)."""
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    bm.normal_update()
    tree = BVHTree.FromBMesh(bm)
    count = len(bm.verts)
    out = np.zeros((count, 4), dtype=np.float32)
    down, up = Vector((0, 0, -1)), Vector((0, 0, 1))
    for v in bm.verts:
        n = v.normal if v.normal.length > 1e-6 else up
        origin = v.co + n * 0.05
        # R: occlusion.
        frame = _frame(n)
        hits = 0
        for ray in AO_RAYS:
            if frame is None:
                d = ray
            elif frame == "flip":
                d = Vector((ray.x, ray.y, -ray.z))
            else:
                d = frame @ ray
            if tree.ray_cast(origin, d, AO_DISTANCE)[0] is not None:
                hits += 1
        occlusion = hits / len(AO_RAYS)
        # G: splash grime near the surface below (or the ground at z = 0).
        location, _normal, _index, distance = tree.ray_cast(v.co + Vector((0, 0, -0.02)), down, SPLASH)
        below = min(distance if location is not None else SPLASH, max(0.0, v.co.z))
        splash = 1.0 - min(1.0, below / SPLASH)
        # B: drip source above.
        location, _normal, _index, distance = tree.ray_cast(v.co + Vector((0, 0, 0.05)), up, DRIP)
        drip = 0.0 if location is None or n.z > 0.5 else 1.0 - min(1.0, distance / DRIP) * 0.6
        # A: sky exposure.
        sky = sum(1 for ray in SKY_RAYS if tree.ray_cast(origin, ray, SKY_DISTANCE)[0] is None) / len(SKY_RAYS)
        out[v.index] = (occlusion, splash, drip, sky)
    bm.free()
    if "Wear" in mesh.color_attributes:
        mesh.color_attributes.remove(mesh.color_attributes["Wear"])
    layer = mesh.color_attributes.new("Wear", "FLOAT_COLOR", "POINT")
    layer.data.foreach_set("color", out.ravel())
    return out


def clear(obj):
    """Zero wear masks (roads and other props: no grime, no dust)."""
    mesh = obj.data
    if "Wear" in mesh.color_attributes:
        mesh.color_attributes.remove(mesh.color_attributes["Wear"])
    layer = mesh.color_attributes.new("Wear", "FLOAT_COLOR", "POINT")
    layer.data.foreach_set("color", np.zeros(len(mesh.vertices) * 4, dtype=np.float32))


# -- Blender preview -----------------------------------------------------------------------------------------------------

def add_preview_nodes(material, dust, grime="#3b342b", wear=1.0, rain=0.0):
    """Grades the palette material's base colour with the Wear masks (the rules of BuildingWear.hlsl, roughly).
    dust: hex colour of the biome's dust. Objects without the attribute read zeros (no wear)."""
    from .terrain_biomes import _linear, _rgb

    nodes, links = material.node_tree.nodes, material.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    if bsdf is None or nodes.get("CF Wear") is not None:
        return
    source = bsdf.inputs["Base Color"].links[0].from_socket if bsdf.inputs["Base Color"].links else None
    if source is None:
        return

    def colour(value):
        return (*_linear(_rgb(value)), 1.0)

    def new(kind, **inputs):
        node = nodes.new(kind)
        for key, value in inputs.items():
            node.inputs[key].default_value = value
        return node

    def math_node(op, a, b=None, clamp=True):
        node = new("ShaderNodeMath")
        node.operation = op
        node.use_clamp = clamp
        for slot, value in ((0, a), (1, b)):
            if value is None:
                continue
            if isinstance(value, (int, float)):
                node.inputs[slot].default_value = float(value)
            else:
                links.new(value, node.inputs[slot])
        return node.outputs[0]

    def mix(a, b, fac, kind="MIX"):
        node = new("ShaderNodeMix")
        node.data_type = "RGBA"
        node.blend_type = kind
        for slot, value in ((6, a), (7, b)):
            if isinstance(value, tuple):
                node.inputs[slot].default_value = value
            else:
                links.new(value, node.inputs[slot])
        if isinstance(fac, float):
            node.inputs[0].default_value = fac
        else:
            links.new(fac, node.inputs[0])
        return node.outputs[2]

    attribute = new("ShaderNodeAttribute")
    attribute.name = "CF Wear"
    attribute.attribute_name = "Wear"
    split = new("ShaderNodeSeparateColor")
    links.new(attribute.outputs["Color"], split.inputs[0])
    occlusion, splash, drip = split.outputs[0], split.outputs[1], split.outputs[2]
    # Blender reads a missing attribute as alpha 1: only count the sky where the masks exist (R + G + B > 0).
    present = math_node("GREATER_THAN", math_node("ADD", math_node("ADD", occlusion, splash), drip), 0.0001)
    sky = math_node("MULTIPLY", attribute.outputs["Alpha"], present)
    # No grime on smooth surfaces (glass, lit windows): the palette's smoothness (metal-smooth map alpha).
    metal = next((n for n in nodes if n.type == "TEX_IMAGE" and n.image and "MetalSmooth" in n.image.name), None)
    rough = math_node("SUBTRACT", 1.0, metal.outputs["Alpha"]) if metal else None
    dirty = math_node("MULTIPLY", math_node("SUBTRACT", rough, 0.25), 1.6) if rough else 1.0
    geometry = new("ShaderNodeNewGeometry")
    normal = new("ShaderNodeSeparateXYZ")
    links.new(geometry.outputs["Normal"], normal.inputs[0])
    vertical = math_node("SUBTRACT", 1.0, math_node("ABSOLUTE", normal.outputs["Z"]))
    upward = math_node("MULTIPLY", math_node("SUBTRACT", normal.outputs["Z"], 0.6), 2.5)
    # Streak noise: stretched along the world vertical.
    mapping = new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (1.2, 1.2, 0.04)
    links.new(geometry.outputs["Position"], mapping.inputs["Vector"])
    streak_noise = new("ShaderNodeTexNoise", Scale=1.0, Detail=2.0)
    links.new(mapping.outputs["Vector"], streak_noise.inputs["Vector"])
    streaks = math_node("MULTIPLY", math_node("MULTIPLY", vertical, math_node("ADD", math_node("MULTIPLY", drip, 0.8), 0.2)),
                        math_node("MULTIPLY", math_node("SUBTRACT", streak_noise.outputs["Fac"], 0.45), 3.0))
    streaks = math_node("MULTIPLY", streaks, sky)
    fine = new("ShaderNodeTexNoise", Scale=0.6, Detail=3.0)
    links.new(geometry.outputs["Position"], fine.inputs["Vector"])
    def masked(value, strength):
        value = math_node("MULTIPLY", value, strength * wear)
        return math_node("MULTIPLY", value, dirty) if rough else value

    base = source
    # CC0 concrete detail (if fetched): three-axis projection in world space (so copies of a building differ), two
    # scales (3 m, and 7.7 m turned) blended by noise so the tiling does not repeat, and a slow light/dark drift.
    # Rough surfaces only. Same rules as BuildingWear.hlsl.
    from .terrain_biomes import detail_image, detail_nodes
    concrete = detail_image("ConcreteWall")
    if concrete is not None:
        def projected(metres, turn):
            mapping = new("ShaderNodeMapping")
            mapping.inputs["Scale"].default_value = (1.0 / metres,) * 3
            mapping.inputs["Rotation"].default_value = (0.0, 0.0, turn)
            links.new(geometry.outputs["Position"], mapping.inputs["Vector"])
            return detail_nodes(nodes, links, concrete, mapping.outputs["Vector"])

        fine_factor, height = projected(3.0, 0.0)
        coarse_factor, _height2 = projected(7.7, 0.6)
        pick = new("ShaderNodeTexNoise", Scale=1.0 / 18.0, Detail=2.0)
        links.new(geometry.outputs["Position"], pick.inputs["Vector"])
        blend = new("ShaderNodeMapRange")
        blend.interpolation_type = "SMOOTHSTEP"
        blend.inputs["From Min"].default_value, blend.inputs["From Max"].default_value = 0.35, 0.65
        links.new(pick.outputs["Fac"], blend.inputs["Value"])
        both = new("ShaderNodeMix")
        both.data_type = "FLOAT"
        links.new(blend.outputs["Result"], both.inputs[0])
        links.new(fine_factor, both.inputs[2])
        links.new(coarse_factor, both.inputs[3])
        drift = new("ShaderNodeTexNoise", Scale=1.0 / 30.0, Detail=2.0)
        links.new(geometry.outputs["Position"], drift.inputs["Vector"])
        factor = math_node("MULTIPLY", both.outputs[0],
                           math_node("ADD", math_node("MULTIPLY", drift.outputs["Fac"], 0.3), 0.85, clamp=False), clamp=False)
        strength = math_node("MULTIPLY", dirty, 0.6) if rough else 0.6
        soft = new("ShaderNodeMix")
        soft.data_type = "FLOAT"
        links.new(strength, soft.inputs[0]) if not isinstance(strength, float) else None
        soft.inputs[2].default_value = 1.0
        links.new(factor, soft.inputs[3])
        grey = new("ShaderNodeCombineColor")
        for k in range(3):
            links.new(soft.outputs[0], grey.inputs[k])
        base = mix(base, grey.outputs[0], 1.0, "MULTIPLY")
        bump = new("ShaderNodeBump")
        bump.inputs["Distance"].default_value = 0.2
        links.new(strength, bump.inputs["Strength"]) if not isinstance(strength, float) else None
        links.new(height, bump.inputs["Height"])
        links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])
    base = mix(base, (0.6, 0.6, 0.6, 1.0), masked(math_node("SUBTRACT", occlusion, 0.25), 0.6), "MULTIPLY")
    base = mix(base, colour(grime), masked(splash, 0.6))
    base = mix(base, colour(grime), masked(streaks, 0.75))
    base = mix(base, colour(dust), masked(math_node("MULTIPLY", math_node("MULTIPLY", upward, sky), fine.outputs["Fac"]), 0.8))
    if rain > 0:
        base = mix(base, (1.0 - 0.4 * rain,) * 3 + (1.0,), 1.0, "MULTIPLY")
    base = _surfaces(nodes, links, bsdf, metal, base, source, new, math_node, mix, wear, rain, colour(grime))
    links.new(base, bsdf.inputs["Base Color"])


def _surfaces(nodes, links, bsdf, metal, base, source, new, math_node, mix, wear, rain, grime):
    """Building surfaces in the preview (the rules of BuildingWear.hlsl, roughly): per-copy tint (Object Info random),
    panel seams on rough walls, puddles in wet biomes, fake rooms behind windows (metal-smooth G = 1). Returns the new
    base colour socket; sets the emission to the room-lit glow."""
    if metal is None:
        return base
    split = new("ShaderNodeSeparateColor")
    links.new(metal.outputs["Color"], split.inputs[0])
    window = split.outputs["Green"]
    solid = math_node("SUBTRACT", 1.0, window)
    rough = math_node("MULTIPLY", math_node("SUBTRACT", math_node("SUBTRACT", 1.0, metal.outputs["Alpha"]), 0.25), 1.6)
    info = new("ShaderNodeObjectInfo")
    seed = info.outputs["Random"]
    coords = new("ShaderNodeTexCoord")
    local = new("ShaderNodeSeparateXYZ")
    links.new(coords.outputs["Object"], local.inputs[0])
    geometry = new("ShaderNodeNewGeometry")
    n = new("ShaderNodeSeparateXYZ")
    links.new(geometry.outputs["Normal"], n.inputs[0])
    vertical = math_node("SUBTRACT", 1.0, math_node("ABSOLUTE", n.outputs["Z"]))

    def grey(value):
        node = new("ShaderNodeCombineColor")
        for k in range(3):
            links.new(value, node.inputs[k])
        return node.outputs[0]

    # Per copy: up to 6 % lighter or darker (not on windows).
    tint = math_node("ADD", math_node("MULTIPLY", math_node("SUBTRACT", seed, 0.5), 0.12, clamp=False), 1.0, clamp=False)
    base = mix(base, grey(math_node("ADD", math_node("MULTIPLY", math_node("SUBTRACT", tint, 1.0, clamp=False), solid,
                                                     clamp=False), 1.0, clamp=False)), 1.0, "MULTIPLY")
    # Panel seams: 1.5 m across, 3 m up, in object space.
    across = new("ShaderNodeMix")
    across.data_type = "FLOAT"
    links.new(math_node("GREATER_THAN", math_node("ABSOLUTE", n.outputs["X"]), math_node("ABSOLUTE", n.outputs["Y"])),
              across.inputs[0])
    links.new(local.outputs["X"], across.inputs[2])
    links.new(local.outputs["Y"], across.inputs[3])

    def joint(value, every, lo, hi):
        f = math_node("ABSOLUTE", math_node("SUBTRACT", math_node("FRACT", math_node("DIVIDE", value, every, clamp=False),
                                                                    clamp=False), 0.5, clamp=False))
        ramp = new("ShaderNodeMapRange")
        ramp.interpolation_type = "SMOOTHSTEP"
        ramp.inputs["From Min"].default_value, ramp.inputs["From Max"].default_value = lo, hi
        links.new(f, ramp.inputs["Value"])
        return ramp.outputs["Result"]

    seam = math_node("ADD", joint(across.outputs[0], 1.5, 0.47, 0.495), joint(local.outputs["Z"], 3.0, 0.475, 0.497))
    seam = math_node("MULTIPLY", math_node("MULTIPLY", math_node("MULTIPLY", seam, vertical), rough), solid)
    base = mix(base, (0.0, 0.0, 0.0, 1.0), math_node("MULTIPLY", seam, 0.22))
    # Puddles on flat ground in wet biomes.
    if rain > 0.02:
        noise = new("ShaderNodeTexNoise", Scale=1.0 / 6.0, Detail=3.0)
        links.new(geometry.outputs["Position"], noise.inputs["Vector"])
        ramp = new("ShaderNodeMapRange")
        ramp.interpolation_type = "SMOOTHSTEP"
        ramp.inputs["From Min"].default_value, ramp.inputs["From Max"].default_value = 1.0 - rain * 0.9, 1.05 - rain * 0.9
        links.new(noise.outputs["Fac"], ramp.inputs["Value"])
        flat = math_node("MULTIPLY", math_node("SUBTRACT", n.outputs["Z"], 0.6), 2.5)
        puddle = math_node("MULTIPLY", ramp.outputs["Result"], flat)
        base = mix(base, (0.0, 0.0, 0.0, 1.0), math_node("MULTIPLY", puddle, 0.65))
        rough_in = bsdf.inputs["Roughness"].links[0].from_socket if bsdf.inputs["Roughness"].links else None
        if rough_in is not None:
            wet = new("ShaderNodeMix")
            wet.data_type = "FLOAT"
            links.new(puddle, wet.inputs[0])
            links.new(rough_in, wet.inputs[2])
            wet.inputs[3].default_value = 0.04
            links.new(wet.outputs[0], bsdf.inputs["Roughness"])
    # Fake rooms: a ray from the camera into a grid of rooms (4 x 4 x 3.4 m, object space).
    incoming = new("ShaderNodeVectorTransform")
    incoming.vector_type = "VECTOR"
    incoming.convert_from, incoming.convert_to = "WORLD", "OBJECT"
    links.new(geometry.outputs["Incoming"], incoming.inputs[0])
    d = new("ShaderNodeSeparateXYZ")
    links.new(incoming.outputs[0], d.inputs[0])
    size = {"X": 4.0, "Y": 4.0, "Z": 3.4}
    t = {}
    hit = {}
    for axis in ("X", "Y", "Z"):
        q = math_node("DIVIDE", local.outputs[axis], size[axis], clamp=False)
        frac = math_node("FRACT", q, clamp=False)
        dd = math_node("DIVIDE", math_node("MULTIPLY", d.outputs[axis], -1.0, clamp=False), size[axis], clamp=False)
        dd = math_node("ADD", dd, 1e-5, clamp=False)
        far = math_node("GREATER_THAN", dd, 0.0)
        t[axis] = math_node("DIVIDE", math_node("SUBTRACT", far, frac, clamp=False), dd, clamp=False)
        hit[axis] = (frac, dd)
    t_min = math_node("MINIMUM", t["X"], math_node("MINIMUM", t["Y"], t["Z"]))
    on_floor = math_node("LESS_THAN", math_node("SUBTRACT", t["Z"], t_min, clamp=False), 1e-4)
    up = math_node("GREATER_THAN", hit["Z"][1], 0.0)
    ceiling = math_node("MULTIPLY", on_floor, up)
    floor = math_node("MULTIPLY", on_floor, math_node("SUBTRACT", 1.0, up))
    cell = new("ShaderNodeVectorMath")
    cell.operation = "FLOOR"
    scale = new("ShaderNodeVectorMath")
    scale.operation = "DIVIDE"
    links.new(coords.outputs["Object"], scale.inputs[0])
    scale.inputs[1].default_value = (4.0, 4.0, 3.4)
    links.new(scale.outputs[0], cell.inputs[0])
    room_noise = new("ShaderNodeTexWhiteNoise")
    room_noise.noise_dimensions = "4D"
    links.new(cell.outputs[0], room_noise.inputs["Vector"])
    links.new(seed, room_noise.inputs["W"])
    room = room_noise.outputs["Value"]
    wall = math_node("ADD", 0.55, math_node("MULTIPLY", room, 0.3), clamp=False)
    shade = math_node("ADD", math_node("ADD", math_node("MULTIPLY", ceiling, 1.2, clamp=False),
                                       math_node("MULTIPLY", floor, 0.35, clamp=False), clamp=False),
                      math_node("MULTIPLY", math_node("SUBTRACT", 1.0, on_floor), wall, clamp=False), clamp=False)
    shade = math_node("MULTIPLY", shade, math_node("ADD", 0.55, math_node("MULTIPLY", room, 0.45), clamp=False), clamp=False)
    shade = math_node("DIVIDE", shade, math_node("ADD", 1.0, math_node("MULTIPLY", t_min, 0.35, clamp=False), clamp=False),
                      clamp=False)
    # Glow: the window's emission times the room shade; glass: a dim room under the reflection.
    glow_in = bsdf.inputs["Emission Color"].links[0].from_socket if bsdf.inputs["Emission Color"].links else None
    if glow_in is not None:
        lit = new("ShaderNodeMix")
        lit.data_type = "FLOAT"
        links.new(window, lit.inputs[0])
        lit.inputs[2].default_value = 1.0
        links.new(shade, lit.inputs[3])
        links.new(mix(glow_in, grey(lit.outputs[0]), 1.0, "MULTIPLY"), bsdf.inputs["Emission Color"])
    roomy = mix(base, mix(base, grey(math_node("MULTIPLY", shade, 0.5, clamp=False)), 1.0, "MULTIPLY"), 0.6)
    return mix(base, roomy, window)
