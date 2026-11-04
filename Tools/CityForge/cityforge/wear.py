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
    # CC0 concrete detail (if fetched): three-axis projection in object space (3 m), rough surfaces only.
    from .terrain_biomes import detail_image, detail_nodes
    concrete = detail_image("ConcreteWall")
    if concrete is not None:
        coords = new("ShaderNodeTexCoord")
        mapping = new("ShaderNodeMapping")
        mapping.inputs["Scale"].default_value = (1.0 / 3.0,) * 3
        links.new(coords.outputs["Object"], mapping.inputs["Vector"])
        factor, height = detail_nodes(nodes, links, concrete, mapping.outputs["Vector"])
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
    links.new(base, bsdf.inputs["Base Color"])
