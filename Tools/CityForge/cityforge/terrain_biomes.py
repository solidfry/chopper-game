"""
Terrain biomes: one procedural ground shader, many looks. The terrain bakes biome-free facts into two map-wide textures
(terrain.Terrain.surface): masks (R flow, G hollow, B made ground, A scree) and a world normal map with the small
gullies; a biome is only numbers and colours. The same rules
run in three places:
    Assets/Chopper/Art/Shaders/TerrainBiome.hlsl   the game (URP Shader Graph custom function, reads the biome table)
    make_material() below                          Blender previews (Cycles node tree)
    the biome table (write_table)                  one row of 16 pixels per biome, for the game

Rules (per pixel; slope = 1 - normal.y; world position p):
    cliff  = smoothstep(0.30, 0.55, slope)
    ground = mix(groundA, groundB, macro noise (180 m)) x detail noise (9 m)
    rock   = strata colour by height band (band m, warped by noise), x detail
    colour = mix(ground, rock, cliff); scree colour where scree (not on cliffs); sediment in hollows and flow lines
    cover  = snow / moss / ash on gentle slopes above a height, broken by noise
    wet    = darker and smoother (rain plus flow lines); glow = emission in strong flow lines (lava, toxic runs)
    made   = concrete where the city, roads and channel are (B mask)
"""
import math
import os

import numpy as np
import struct
import zlib

TABLE_WIDTH = 16
CC0 = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "Assets", "Chopper",
                                    "Art", "ThirdParty", "CC0"))


def detail_image(name, suffix="Colour"):
    """A CC0 detail map (Tools/CityForge/fetch_cc0.py), or None when it was not fetched."""
    import bpy

    path = os.path.join(CC0, name, f"{name}_{suffix}.jpg")
    if not os.path.exists(path):
        return None
    image = bpy.data.images.load(path, check_existing=True)
    image.colorspace_settings.name = "sRGB" if suffix == "Colour" else "Non-Color"
    return image


def detail_nodes(nodes, links, image, vector, box=True):
    """Image detail as a luminance factor around 1 (0.55..1.45) and a height for bump: (factor socket, height socket)."""
    node = nodes.new("ShaderNodeTexImage")
    node.image = image
    if box:
        node.projection = "BOX"
        node.projection_blend = 0.25
    links.new(vector, node.inputs["Vector"])
    grey = nodes.new("ShaderNodeRGBToBW")
    links.new(node.outputs["Color"], grey.inputs[0])
    scale = nodes.new("ShaderNodeMath")
    scale.operation = "MULTIPLY_ADD"
    links.new(grey.outputs[0], scale.inputs[0])
    scale.inputs[1].default_value = 2.0
    scale.inputs[2].default_value = 0.2
    clamp = nodes.new("ShaderNodeClamp")
    clamp.inputs["Min"].default_value = 0.55
    clamp.inputs["Max"].default_value = 1.45
    links.new(scale.outputs[0], clamp.inputs["Value"])
    return clamp.outputs[0], grey.outputs[0]

BIOMES = {
    # The default: the city's desert. Dry sand, red sandstone strata, a little damp in the gullies.
    "desert": dict(
        ground=("#b8a07c", "#a58c69"), sediment="#8a765a", rock=("#8e6a52", "#a37d5e", "#7b5743", "#99704f"),
        band=11.0, cliff="#5b4536", scree="#9c8468", cover=None, cover_slope=0.0, cover_height=0.0, cover_amount=0.0,
        wet=0.05, flow_wet=0.35, glow=None, glow_strength=0.0, glow_flow=1.0, concrete="#74726b", roughness=0.9),
    # Rain on the desert (the plan's "rainy cyberpunk city in the desert"): darker, glossy, wet runs.
    "rain_desert": dict(
        ground=("#8f7c60", "#7d6a50"), sediment="#5d5040", rock=("#6e5141", "#80604a", "#5c4236", "#76563f"),
        band=11.0, cliff="#3f2f26", scree="#76634f", cover=None, cover_slope=0.0, cover_height=0.0, cover_amount=0.0,
        wet=0.45, flow_wet=0.5, glow=None, glow_strength=0.0, glow_flow=1.0, concrete="#5c5b56", roughness=0.7),
    # Snow on flat ground and ledges, blue-grey rock on the cliffs.
    "arctic": dict(
        ground=("#dfe6ea", "#c8d2d8"), sediment="#9aa7af", rock=("#5f6670", "#717985", "#4f555e", "#68707a"),
        band=14.0, cliff="#3a3f46", scree="#8d949c", cover="#f4f8fa", cover_slope=0.42, cover_height=-50.0,
        cover_amount=1.0, wet=0.1, flow_wet=0.2, glow=None, glow_strength=0.0, glow_flow=1.0, concrete="#7d7f82",
        roughness=0.75),
    # Green ground, mud in the hollows, moss over the rock ledges, wet everywhere.
    "jungle": dict(
        ground=("#4f6b2e", "#3c5623"), sediment="#4e3f2b", rock=("#5d6156", "#6b6e60", "#4c4f46", "#626657"),
        band=9.0, cliff="#343830", scree="#5a5848", cover="#3e6a2a", cover_slope=0.62, cover_height=-50.0,
        cover_amount=0.85, wet=0.4, flow_wet=0.5, glow=None, glow_strength=0.0, glow_flow=1.0, concrete="#6a6c64",
        roughness=0.8),
    # Black basalt and ash; lava glows in the strongest flow lines.
    "volcanic": dict(
        # Ash flats, then wall bands: dark basalt, rusty oxidised cinder, buff tuff, grey ash.
        ground=("#5c564d", "#2a2623"), sediment="#2c2724", rock=("#2a2725", "#5a3b2c", "#6e6151", "#403a35"),
        band=8.0, cliff="#221f1d", scree="#3b3430", cover="#5e5952", cover_slope=0.3, cover_height=120.0,
        cover_amount=0.35, wet=0.0, flow_wet=0.0, glow="#ff3a0a", glow_strength=3.0, glow_flow=0.55,
        concrete="#55524f", roughness=0.95,
        # Rusty scorched ground near the lava; old lava flows as sharp dark patches on the ash.
        scorch="#6e3a20", scorch_amount=0.85, patch=0.04),
    # Poisoned waste: sick yellow-green ground, crusted rock, glowing runs.
    "toxic": dict(
        ground=("#6b6a3a", "#59592f"), sediment="#3b4722", rock=("#4e4a3c", "#5d5845", "#433f33", "#57513f"),
        band=10.0, cliff="#2c2a22", scree="#6a6448", cover="#a7a35a", cover_slope=0.35, cover_height=-50.0,
        cover_amount=0.3, wet=0.3, flow_wet=0.6, glow="#7cff3a", glow_strength=3.0, glow_flow=0.5,
        concrete="#6a6a5c", roughness=0.8),
}
NAMES = tuple(BIOMES)


def _rgb(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def _linear(c):
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def row(biome):
    """16 RGBA pixels (0..1). Colours sRGB; parameters packed in 0..1 (decoded the same way in TerrainBiome.hlsl).
    0 groundA, 1 groundB, 2 sediment, 3-6 rock strata, 7 cliff, 8 scree, 9 cover (A = amount), 10 glow (A = strength
    / 10), 11 concrete, 12 (band / 50, cover slope, (cover height + 100) / 1000, glow flow), 13 (wet, flow wet,
    roughness, has cover), 14 scorch (A = amount: ground near lava, by the heat in the surface normal map's alpha),
    15 (patch: half width of the groundA/groundB edge, 0.15 soft .. 0.03 sharp; spare)."""
    b = BIOMES[biome]
    pixels = [(*_rgb(b["ground"][0]), 1.0), (*_rgb(b["ground"][1]), 1.0), (*_rgb(b["sediment"]), 1.0)]
    pixels += [(*_rgb(c), 1.0) for c in b["rock"]]
    pixels += [(*_rgb(b["cliff"]), 1.0), (*_rgb(b["scree"]), 1.0)]
    pixels.append((*_rgb(b["cover"] or "#000000"), b["cover_amount"] if b["cover"] else 0.0))
    pixels.append((*_rgb(b["glow"] or "#000000"), min(1.0, b["glow_strength"] / 10.0)))
    pixels.append((*_rgb(b["concrete"]), 1.0))
    pixels.append((b["band"] / 50.0, b["cover_slope"], (b["cover_height"] + 100.0) / 1000.0, b["glow_flow"]))
    pixels.append((b["wet"], b["flow_wet"], b["roughness"], 1.0 if b["cover"] else 0.0))
    pixels.append((*_rgb(b.get("scorch") or "#000000"), b.get("scorch_amount", 0.0) if b.get("scorch") else 0.0))
    pixels.append((b.get("patch", 0.15), 0.0, 0.0, 0.0))
    pixels += [(0.0, 0.0, 0.0, 0.0)] * (TABLE_WIDTH - len(pixels))
    return pixels


def write_table(path):
    """Biome table PNG (TABLE_WIDTH x biome count, row 0 = the first biome at the top). Import in Unity: sRGB off,
    point filter, no mipmaps, no compression (colours are decoded to linear in the shader)."""
    rows = [row(name) for name in NAMES]
    raw = b"".join(b"\x00" + bytes(int(round(max(0.0, min(1.0, v)) * 255)) for p in r for v in p) for r in rows)

    def chunk(kind, payload):
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))

    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as handle:
        handle.write(b"\x89PNG\r\n\x1a\n")
        handle.write(chunk(b"IHDR", struct.pack(">IIBBBBB", TABLE_WIDTH, len(rows), 8, 6, 0, 0, 0)))
        handle.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        handle.write(chunk(b"IEND", b""))
    return {"file": os.path.basename(path), "width": TABLE_WIDTH, "biomes": list(NAMES)}


# -- Blender preview material ------------------------------------------------------------------------------------------

def surface_images(t, kit):
    """Blender images of the terrain surface textures (terrain.Terrain.surface): (masks, normal, map size m)."""
    import bpy

    out = []
    for suffix, data in zip(("SurfaceMasks", "SurfaceNormal"), t.surface()):
        if suffix == "SurfaceNormal":
            data = np.concatenate([data[..., :3] * 0.5 + 0.5, data[..., 3:]], axis=-1)   # A = heat
        name = f"{kit}_{suffix}"
        image = bpy.data.images.get(name)
        if image is not None:
            bpy.data.images.remove(image)
        res = data.shape[0]
        # 8-bit (the data is 8-bit in the game too): packed into the .blend as PNG, not as a big float image.
        image = bpy.data.images.new(name, res, res, alpha=True, float_buffer=False, is_data=True)
        image.colorspace_settings.name = "Non-Color"
        image.alpha_mode = "CHANNEL_PACKED"
        image.pixels.foreach_set(np.ascontiguousarray(data, np.float32).ravel())   # row 0 = -Z = bottom
        image.pack()
        out.append(image)
    return out[0], out[1], float(t.values["size"])


def make_material(biome, name="CF Terrain", surface=None):
    """Cycles node tree with the same rules (approximately: Blender noise, not the shader's hash noise). surface:
    surface_images(); without it the ground has no masks and only the mesh normals."""
    import bpy

    b = BIOMES[biome]
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    x = [0]

    def new(kind, **inputs):
        node = nodes.new(kind)
        node.location = (x[0], 0)
        x[0] += 40
        for key, value in inputs.items():
            node.inputs[key].default_value = value
        return node

    def link(a, b_):
        links.new(a, b_)

    def colour(value):
        return (*_linear(_rgb(value)), 1.0)

    def mix(a, b_, fac, kind="MIX"):
        node = new("ShaderNodeMix")
        node.data_type = "RGBA"
        node.blend_type = kind
        for slot, value in ((6, a), (7, b_)):
            if isinstance(value, tuple):
                node.inputs[slot].default_value = value
            else:
                link(value, node.inputs[slot])
        if isinstance(fac, float):
            node.inputs[0].default_value = fac
        else:
            link(fac, node.inputs[0])
        return node.outputs[2]

    def math_node(op, a, b_=None, clamp=False):
        node = new("ShaderNodeMath")
        node.operation = op
        node.use_clamp = clamp
        for slot, value in ((0, a), (1, b_)):
            if value is None:
                continue
            if isinstance(value, (int, float)):
                node.inputs[slot].default_value = float(value)
            else:
                link(value, node.inputs[slot])
        return node.outputs[0]

    def ramp(value, lo, hi):
        node = new("ShaderNodeMapRange")
        node.interpolation_type = "SMOOTHSTEP"
        link(value, node.inputs["Value"])
        node.inputs["From Min"].default_value = lo
        node.inputs["From Max"].default_value = hi
        return node.outputs["Result"]

    geometry = new("ShaderNodeNewGeometry")
    position, normal = geometry.outputs["Position"], geometry.outputs["Normal"]
    if surface is not None:
        # Map UV from the object position (relative to the city centre): Blender (-x, -z, y) = Unity (x, y, z), so
        # u = x / size + 0.5 = -bx / size + 0.5, v = -by / size + 0.5. Outside the map: clip (alpha 0).
        mask_image, normal_image, size = surface
        coords = new("ShaderNodeTexCoord")
        mapping = new("ShaderNodeMapping")
        mapping.inputs["Scale"].default_value = (-1.0 / size, -1.0 / size, 1.0)
        mapping.inputs["Location"].default_value = (0.5, 0.5, 0.0)
        link(coords.outputs["Object"], mapping.inputs["Vector"])

        def image_node(image):
            node = new("ShaderNodeTexImage")
            node.image = image
            node.interpolation = "Cubic"
            node.extension = "CLIP"
            link(mapping.outputs["Vector"], node.inputs["Vector"])
            return node

        mask_node, normal_node = image_node(mask_image), image_node(normal_image)
        masks = new("ShaderNodeSeparateColor")
        link(mask_node.outputs["Color"], masks.inputs[0])
        flow, hollow, made = masks.outputs[0], masks.outputs[1], masks.outputs[2]
        scree = mask_node.outputs["Alpha"]
        unpack = new("ShaderNodeVectorMath")
        unpack.operation = "MULTIPLY_ADD"
        link(normal_node.outputs["Color"], unpack.inputs[0])
        unpack.inputs[1].default_value = (2.0, 2.0, 2.0)
        unpack.inputs[2].default_value = (-1.0, -1.0, -1.0)
        parts = new("ShaderNodeSeparateXYZ")
        link(unpack.outputs[0], parts.inputs[0])
        blender_n = new("ShaderNodeCombineXYZ")
        link(math_node("MULTIPLY", parts.outputs["X"], -1.0), blender_n.inputs["X"])
        link(math_node("MULTIPLY", parts.outputs["Z"], -1.0), blender_n.inputs["Y"])
        link(parts.outputs["Y"], blender_n.inputs["Z"])
        # On the map: 1, fading to 0 over the last 6 m (as TerrainBiome.hlsl); outside, the mesh normal.
        uv = new("ShaderNodeSeparateXYZ")
        link(mapping.outputs["Vector"], uv.inputs[0])
        edge = None
        for axis in ("X", "Y"):
            for value in (uv.outputs[axis], math_node("SUBTRACT", 1.0, uv.outputs[axis])):
                edge = value if edge is None else math_node("MINIMUM", edge, value)
        inside = math_node("MULTIPLY", edge, size / 6.0, clamp=True)
        heat = math_node("MULTIPLY", normal_node.outputs["Alpha"], inside)
        pick = new("ShaderNodeMix")
        pick.data_type = "VECTOR"
        link(inside, pick.inputs[0])
        link(normal, pick.inputs[4])
        link(blender_n.outputs[0], pick.inputs[5])
        tidy = new("ShaderNodeVectorMath")
        tidy.operation = "NORMALIZE"
        link(pick.outputs[1], tidy.inputs[0])
        normal = tidy.outputs[0]
    else:
        flow = hollow = made = scree = heat = new("ShaderNodeValue").outputs[0]
    split_n = new("ShaderNodeSeparateXYZ")
    link(normal, split_n.inputs[0])
    split_p = new("ShaderNodeSeparateXYZ")
    link(position, split_p.inputs[0])
    height = split_p.outputs["Z"]
    slope = math_node("SUBTRACT", 1.0, split_n.outputs["Z"])

    def noise(scale_m, detail=2.0):
        node = new("ShaderNodeTexNoise")
        node.noise_dimensions = "3D"
        link(position, node.inputs["Vector"])
        node.inputs["Scale"].default_value = 1.0 / scale_m
        node.inputs["Detail"].default_value = detail
        return node.outputs["Fac"]

    macro, detail = noise(180.0), noise(9.0, 4.0)
    macro.node.inputs["Distortion"].default_value = 0.6     # warped, like the shader (flow-lobe shapes)
    shade = math_node("ADD", math_node("MULTIPLY", detail, 0.3), 0.85)       # 0.85..1.15
    # CC0 detail maps (if fetched): rock on three axes (8 m), sand and mud from above (4 m).
    def scaled(metres):
        mapping = new("ShaderNodeMapping")
        mapping.inputs["Scale"].default_value = (1.0 / metres,) * 3
        link(position, mapping.inputs["Vector"])
        return mapping.outputs["Vector"]

    rock_image, sand_image, mud_image = detail_image("RockFace"), detail_image("GravellySand"), detail_image("DryMud")
    rock_detail = detail_nodes(nodes, links, rock_image, scaled(8.0)) if rock_image else None
    sand_detail = detail_nodes(nodes, links, sand_image, scaled(4.0), box=False) if sand_image else None
    mud_detail = detail_nodes(nodes, links, mud_image, scaled(5.0), box=False) if mud_image else None
    cover_image = detail_image("WindSnow")
    cover_detail = detail_nodes(nodes, links, cover_image, scaled(6.0), box=False) if cover_image else None

    def slab_grey(value):
        grey = new("ShaderNodeCombineColor")
        for k in range(3):
            link(value, grey.inputs[k])
        return grey.outputs[0]

    def detailed(colour_socket, pair):
        if pair is None:
            return colour_socket
        grey = new("ShaderNodeCombineColor")
        for k in range(3):
            link(pair[0], grey.inputs[k])
        return mix(colour_socket, grey.outputs[0], 1.0, "MULTIPLY")

    patch = b.get("patch", 0.15)
    ground = mix(colour(b["ground"][0]), colour(b["ground"][1]), ramp(macro, 0.5 - patch, 0.5 + patch))
    ground = mix(ground, shade, 1.0, "MULTIPLY")
    middle = math_node("ADD", math_node("MULTIPLY", noise(45.0, 2.0), 0.3), 0.85)            # 45 m light and dark
    ground = mix(ground, slab_grey(middle), 1.0, "MULTIPLY")
    if b.get("scorch"):
        burn = math_node("MULTIPLY", math_node("MULTIPLY", heat, heat), b.get("scorch_amount", 0.0))
        ground = mix(ground, colour(b["scorch"]), burn)
    ground = detailed(ground, sand_detail)
    # Strata: height bands (warped), 4 colours, constant steps.
    warp = math_node("MULTIPLY", noise(90.0), 6.0)
    band = math_node("DIVIDE", math_node("ADD", height, warp), b["band"] * 4.0)
    strata = new("ShaderNodeValToRGB")
    strata.color_ramp.interpolation = "CONSTANT"
    elements = strata.color_ramp.elements
    while len(elements) < 4:
        elements.new(0.5)
    for k, element in enumerate(elements):
        element.position = k * 0.25
        element.color = colour(b["rock"][k])
    link(math_node("FRACT", band), strata.inputs["Fac"])
    rock = mix(strata.outputs["Color"], shade, 1.0, "MULTIPLY")
    rock = detailed(rock, rock_detail)
    cliff = ramp(slope, 0.30, 0.55)
    rock = mix(rock, colour(b["cliff"]), ramp(slope, 0.7, 0.95))
    # Rock varnish: dark streaks down the walls (3D noise stretched 9x in height), and darker gully floors.
    def streaks(across, down):
        mapping = new("ShaderNodeMapping")
        mapping.inputs["Scale"].default_value = (1.0 / across, 1.0 / across, 1.0 / down)
        link(position, mapping.inputs["Vector"])
        node = new("ShaderNodeTexNoise")
        node.noise_dimensions = "3D"
        node.inputs["Detail"].default_value = 1.0
        link(mapping.outputs["Vector"], node.inputs["Vector"])
        return node.outputs["Fac"]

    streak = math_node("ADD", math_node("MULTIPLY", streaks(5.0, 45.0), 0.6), math_node("MULTIPLY", streaks(2.0, 18.0), 0.4))
    rock = mix(rock, (0.0, 0.0, 0.0, 1.0), math_node("MULTIPLY", ramp(streak, 0.45, 0.7), 0.45))
    rock = mix(rock, (0.0, 0.0, 0.0, 1.0), math_node("MULTIPLY", hollow, 0.4))
    result = mix(ground, rock, cliff)
    open_ground = math_node("SUBTRACT", 1.0, cliff)
    result = mix(result, colour(b["scree"]), math_node("MULTIPLY", scree, open_ground))
    deposit = math_node("ADD", math_node("MULTIPLY", hollow, 0.8), math_node("MULTIPLY", flow, 0.6), clamp=True)
    sediment = detailed(colour(b["sediment"]), mud_detail)
    result = mix(result, sediment, math_node("MULTIPLY", deposit, open_ground))
    if b["cover"]:
        flat = math_node("SUBTRACT", 1.0, ramp(slope, b["cover_slope"] - 0.08, b["cover_slope"] + 0.02))
        high = ramp(height, b["cover_height"] - 10.0, b["cover_height"] + 10.0)
        breakup = ramp(noise(25.0, 3.0), 0.35, 0.6)
        cover = math_node("MULTIPLY", math_node("MULTIPLY", flat, high), math_node("MULTIPLY", breakup, b["cover_amount"]))
        result = mix(result, detailed(colour(b["cover"]), cover_detail), cover)
    wet = math_node("ADD", b["wet"], math_node("MULTIPLY", flow, b["flow_wet"]), clamp=True)
    darker = math_node("SUBTRACT", 1.0, math_node("MULTIPLY", wet, 0.45))
    shaded = new("ShaderNodeMix")
    shaded.data_type = "RGBA"
    shaded.blend_type = "MULTIPLY"
    shaded.inputs[0].default_value = 1.0
    link(result, shaded.inputs[6])
    grey = new("ShaderNodeCombineColor")
    for k in range(3):
        link(darker, grey.inputs[k])
    link(grey.outputs[0], shaded.inputs[7])
    # Made ground: concrete slabs (6 m joints, each slab a little lighter or darker), oil stains, cracks, and dirt
    # blown onto the edges. Same rules as TerrainBiome.hlsl.
    slab = math_node("ADD", math_node("MULTIPLY", noise(7.0), 0.3), 0.85, clamp=False)
    concrete = mix(colour(b["concrete"]), (1.0, 1.0, 1.0, 1.0), 0.0)
    concrete = mix(concrete, slab_grey(slab), 1.0, "MULTIPLY")
    joint = None
    for axis in ("X", "Y"):
        across = math_node("ABSOLUTE", math_node("SUBTRACT", math_node("MULTIPLY", math_node(
            "FRACT", math_node("DIVIDE", split_p.outputs[axis], 6.0, clamp=False)), 2.0, clamp=False), 1.0, clamp=False))
        line = ramp(across, 0.93, 0.98)
        joint = line if joint is None else math_node("MAXIMUM", joint, line)
    concrete = mix(concrete, (0.0, 0.0, 0.0, 1.0), math_node("MULTIPLY", joint, 0.25))
    oil = ramp(noise(9.0, 3.0), 0.62, 0.7)
    concrete = mix(concrete, (0.05, 0.05, 0.05, 1.0), math_node("MULTIPLY", oil, 0.55))
    crack = ramp(math_node("SUBTRACT", 1.0, math_node("ABSOLUTE", math_node("SUBTRACT", math_node(
        "MULTIPLY", noise(4.0, 4.0), 2.0, clamp=False), 1.0, clamp=False))), 0.94, 0.985)
    concrete = mix(concrete, (0.02, 0.02, 0.02, 1.0), math_node("MULTIPLY", crack, 0.6))
    edge = math_node("MULTIPLY", math_node("SUBTRACT", 1.0, ramp(made, 0.75, 0.98)), ramp(noise(5.0), 0.4, 0.6))
    concrete = mix(concrete, ground, math_node("MULTIPLY", edge, 0.8))
    result = mix(shaded.outputs[2], concrete, ramp(made, 0.3, 0.7))
    puddle = None
    if b["wet"] > 0.02:
        # Puddles on flat made ground and in hollows (as TerrainBiome.hlsl).
        wet_noise = noise(6.0, 3.0)
        puddle = ramp(wet_noise, 1.0 - b["wet"] * 0.9, 1.05 - b["wet"] * 0.9)
        flat_ground = math_node("SUBTRACT", 1.0, ramp(slope, 0.02, 0.08))
        puddle = math_node("MULTIPLY", math_node("MULTIPLY", puddle, math_node("ADD", made, hollow, clamp=True)),
                           flat_ground)
        result = mix(result, (0.0, 0.0, 0.0, 1.0), math_node("MULTIPLY", puddle, 0.65))
    bsdf = new("ShaderNodeBsdfPrincipled")
    link(result, bsdf.inputs["Base Color"])
    if rock_detail and sand_detail:
        height = new("ShaderNodeMix")
        height.data_type = "FLOAT"
        link(cliff, height.inputs[0])
        link(sand_detail[1], height.inputs[2])
        link(rock_detail[1], height.inputs[3])
        bump = new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.35
        bump.inputs["Distance"].default_value = 0.5
        link(height.outputs[0], bump.inputs["Height"])
        link(normal, bump.inputs["Normal"])
        link(bump.outputs["Normal"], bsdf.inputs["Normal"])
    else:
        link(normal, bsdf.inputs["Normal"])
    roughness = math_node("SUBTRACT", b["roughness"], math_node("MULTIPLY", wet, 0.55), clamp=True)
    if puddle is not None:
        roughness = math_node("SUBTRACT", roughness, math_node("MULTIPLY", puddle, roughness), clamp=True)
    link(roughness, bsdf.inputs["Roughness"])
    if b["glow"]:
        glow = ramp(flow, b["glow_flow"], b["glow_flow"] + 0.15)
        glow = math_node("MULTIPLY", glow, math_node("SUBTRACT", 1.0, made, clamp=True))
        # Crust: dark cooled plates over the glowing melt (25 % .. 100 % glow), as in TerrainBiome.hlsl.
        crust = ramp(noise(14.0, 3.0), 0.35, 0.65)
        glow = math_node("MULTIPLY", glow, math_node("ADD", math_node("MULTIPLY", crust, 0.75), 0.25))
        bsdf.inputs["Emission Color"].default_value = colour(b["glow"])
        link(math_node("MULTIPLY", glow, b["glow_strength"]), bsdf.inputs["Emission Strength"])
    output = new("ShaderNodeOutputMaterial")
    link(bsdf.outputs[0], output.inputs["Surface"])
    material.diffuse_color = colour(b["ground"][0])
    return material
