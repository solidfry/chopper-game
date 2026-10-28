"""
One material per kit: every face samples one swatch of a small palette texture (4 x 4 cells). Three maps, same layout:
colour (sRGB), emission (sRGB, black = no glow) and URP metallic/smoothness (R = metallic, A = smoothness).
A skin is another set of the same three maps. Colours follow the game's style guide (UI/Theme/ChopperTheme.uss).
"""
import os

import bpy

GRID = 4
CELL = 8  # pixels per swatch (point filtered in Unity)

# name: (colour hex, emission hex or None, metallic, smoothness)
SWATCHES = [
    ("hull", "#4a514c", None, 0.35, 0.45),          # 0 main armour, dark olive grey
    ("hull_dark", "#2a2f33", None, 0.4, 0.35),      # 1 panels, belly
    ("hull_light", "#6b726c", None, 0.3, 0.5),      # 2 raised plates
    ("gunmetal", "#2b2d30", None, 0.9, 0.55),       # 3 barrels, mounts
    ("steel", "#8a8f94", None, 0.95, 0.65),         # 4 bare metal, rotor hub
    ("rubber", "#121314", None, 0.0, 0.2),          # 5 tyres, seals, tube mouths
    ("glass", "#0d2a2c", None, 0.1, 0.95),          # 6 canopy (dark cyan)
    ("hazard", "#ffdf6b", None, 0.2, 0.4),          # 7 Lightning Yellow stripes
    ("warhead", "#ff4246", None, 0.2, 0.45),        # 8 Screamer Red markings
    ("neon_green", "#00ff94", "#00ff94", 0.0, 0.6),  # 9 Console Green strips (glow)
    ("neon_cyan", "#00f0ff", "#00f0ff", 0.0, 0.6),   # 10 Dream Cyan lights (glow)
    ("neon_pink", "#ff4289", "#ff4289", 0.0, 0.6),   # 11 Cruise Pink exhaust (glow)
    ("exhaust", "#ff6a2a", "#ff6a2a", 0.0, 0.5),     # 12 hot exhaust (glow)
    ("missile", "#c9ccc8", None, 0.3, 0.5),          # 13 missile bodies
    ("carbon", "#18191b", None, 0.2, 0.3),           # 14 rotor blades
    ("marker", "#ff00ff", None, 0.0, 0.0),           # 15 never on exported meshes (debug)
]
INDEX = {name: i for i, (name, *_rest) in enumerate(SWATCHES)}


def swatch(name):
    return INDEX[name]


def uv_of(index):
    """Centre of a swatch cell in UV space (v = 0 at the bottom)."""
    col, row = index % GRID, index // GRID
    return ((col + 0.5) / GRID, 1.0 - (row + 0.5) / GRID)


def _hex(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def _write_png(path, pixel_fn, linear):
    size = GRID * CELL
    image = bpy.data.images.new(os.path.basename(path), size, size, alpha=True)
    # Byte images: `pixels` are the stored 8-bit values (sRGB-encoded for colour maps), so no conversion here.
    image.colorspace_settings.name = "Non-Color" if linear else "sRGB"
    pixels = []
    for y in range(size):
        row = (size - 1 - y) // CELL  # image rows go up from the bottom
        for x in range(size):
            col = x // CELL
            pixels.extend(pixel_fn(row * GRID + col))
    image.pixels = pixels
    image.filepath_raw = path
    image.file_format = "PNG"
    image.save()
    return image


def write_maps(folder, kit_name):
    """Writes <kit>_Colour.png, <kit>_Emission.png, <kit>_MetalSmooth.png. Returns the three image paths."""
    os.makedirs(folder, exist_ok=True)

    def colour(i):
        return (*_hex(SWATCHES[i][1]), 1.0)

    def emission(i):
        glow = SWATCHES[i][2]
        if glow is None:
            return (0.0, 0.0, 0.0, 1.0)
        return (*_hex(glow), 1.0)

    def metal_smooth(i):
        _name, _c, _e, metallic, smoothness = SWATCHES[i]
        return (metallic, 0.0, 0.0, smoothness)

    paths = [os.path.join(folder, f"{kit_name}_{suffix}.png") for suffix in ("Colour", "Emission", "MetalSmooth")]
    _write_png(paths[0], colour, linear=False)
    _write_png(paths[1], emission, linear=False)
    _write_png(paths[2], metal_smooth, linear=True)
    return paths


def make_material(kit_name, paths):
    """Blender material that matches the Unity one (for previews; the FBX only carries the material name)."""
    material = bpy.data.materials.new(f"{kit_name}_Kit")
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    bsdf = nodes.get("Principled BSDF")

    def texture(path, non_color):
        node = nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(path, check_existing=True)
        node.image.colorspace_settings.name = "Non-Color" if non_color else "sRGB"
        node.interpolation = "Closest"
        return node

    colour = texture(paths[0], False)
    emission = texture(paths[1], False)
    metal = texture(paths[2], True)
    links.new(colour.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(emission.outputs["Color"], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 4.0
    separate = nodes.new("ShaderNodeSeparateColor")
    links.new(metal.outputs["Color"], separate.inputs["Color"])
    links.new(separate.outputs["Red"], bsdf.inputs["Metallic"])
    invert = nodes.new("ShaderNodeMath")
    invert.operation = "SUBTRACT"
    invert.inputs[0].default_value = 1.0
    links.new(metal.outputs["Alpha"], invert.inputs[1])
    links.new(invert.outputs["Value"], bsdf.inputs["Roughness"])
    return material


def apply_uvs(obj):
    """Every face's UVs go to the centre of its swatch (read from the 'swatch' face attribute, kept through the bevel)."""
    mesh = obj.data
    layer = mesh.uv_layers.new(name="Palette")
    attribute = mesh.attributes.get("swatch")
    for poly in mesh.polygons:
        index = attribute.data[poly.index].value if attribute is not None else 0
        u, v = uv_of(index)
        for loop_index in poly.loop_indices:
            layer.data[loop_index].uv = (u, v)
    if attribute is not None:
        mesh.attributes.remove(attribute)
