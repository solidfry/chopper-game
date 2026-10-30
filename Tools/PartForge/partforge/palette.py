"""
One material per kit: every face samples one swatch of a small palette texture (5 x 5 cells). Three maps, same layout:
colour (sRGB), emission (sRGB, black = no glow) and URP metallic/smoothness (R = metallic, A = smoothness).
A skin is another set of the same three maps. Colours follow the game's style guide (UI/Theme/ChopperTheme.uss);
a style (styles.py) can override any swatch colour. Swatch names and cells never change, so builders stay the same.
"""
import os

import bpy

GRID = 5
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
    ("primer", "#6e4636", None, 0.1, 0.25),          # 16 red-oxide replacement panels
    ("rust", "#4f3324", None, 0.3, 0.15),            # 17 rust, old metal
    ("soot", "#141312", None, 0.0, 0.1),             # 18 burnt metal behind exhausts
    ("hazard_faded", "#b39443", None, 0.1, 0.3),     # 19 sun-bleached warning paint
    ("sodium", "#ffae42", "#ffae42", 0.0, 0.5),      # 20 sodium-lamp amber (glow)
    ("warning", "#ff2e2e", "#ff2e2e", 0.0, 0.5),     # 21 red warning light (glow)
    ("concrete", "#6f706a", None, 0.0, 0.2),         # 22 matte cladding
    ("bare", "#9a9890", None, 0.85, 0.35),           # 23 scuffed bare metal edges
    ("spare", "#000000", None, 0.0, 0.0),            # 24 free cell
]
INDEX = {name: i for i, (name, *_rest) in enumerate(SWATCHES)}


def swatch(name):
    return INDEX[name]


# Live cockpit screens: one render texture in the game holds these pages side by side (square tiles, left to right).
# A display shows the page for its placeholder content; other contents keep their baked graphics.
SCREEN_PAGES = ("Flight", "Tactical", "Systems")
SCREEN_OF = {"horizon": 0, "radar": 1, "map": 1, "bars": 2, "text": 2}


def screen_uvs(page, w, h):
    """UVs (bottom-left, bottom-right, top-right, top-left) of a w x h display in its page tile: the middle of the
    square page, cut to the display's shape (a wide display shows the middle band, a tall one the middle column)."""
    du = 1.0 / len(SCREEN_PAGES)
    aspect = w / h
    sx, sy = (1.0, 1.0 / aspect) if aspect >= 1.0 else (aspect, 1.0)
    u0, u1 = du * (page + 0.5 - sx / 2), du * (page + 0.5 + sx / 2)
    v0, v1 = 0.5 - sy / 2, 0.5 + sy / 2
    return [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]


def screen_material_name(kit_name):
    return f"{kit_name}_Screen"


def make_screen_material(kit_name):
    """Live screens: the game draws the pages on them (Unity material <kit>_Screen). Here a dark glowing stand-in."""
    name = screen_material_name(kit_name)
    old = bpy.data.materials.get(name)
    if old is not None:
        bpy.data.materials.remove(old)
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.01, 0.03, 0.03, 1.0)
    bsdf.inputs["Emission Color"].default_value = (0.0, 0.35, 0.2, 1.0)
    bsdf.inputs["Emission Strength"].default_value = 1.0
    bsdf.inputs["Roughness"].default_value = 0.1
    material.diffuse_color = (0.0, 0.35, 0.2, 1.0)
    return material


def apply_screens(obj, material):
    """Faces tagged as live screens (geo.Builder.screen_quad) get the screen material (slot 1) and their atlas UVs
    in UV0. Removes the builder's screen layers from every part."""
    mesh = obj.data
    attribute = mesh.attributes.get("screen")
    layer = mesh.uv_layers.get("Screen")
    pages = [poly.index for poly in mesh.polygons if attribute is not None and attribute.data[poly.index].value > 0]
    if pages:
        base = mesh.uv_layers["UVMap"]
        mesh.materials.append(material)
        for index in pages:
            poly = mesh.polygons[index]
            poly.material_index = 1
            for loop_index in poly.loop_indices:
                base.data[loop_index].uv = layer.data[loop_index].uv
    if layer is not None:
        mesh.uv_layers.remove(layer)
    if attribute is not None:
        mesh.attributes.remove(mesh.attributes["screen"])
    return bool(pages)


def uv_of(index):
    """Centre of a swatch cell in UV space (v = 0 at the bottom)."""
    col, row = index % GRID, index // GRID
    return ((col + 0.5) / GRID, 1.0 - (row + 0.5) / GRID)


def _hex(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def _fill(image, pixel_fn):
    size = GRID * CELL
    pixels = []
    for y in range(size):
        row = (size - 1 - y) // CELL  # image rows go up from the bottom
        for x in range(size):
            col = x // CELL
            pixels.extend(pixel_fn(row * GRID + col))
    # Byte images: `pixels` are the stored 8-bit values (sRGB-encoded for colour maps), so no conversion here.
    image.pixels = pixels
    image.update()


def _image(name, non_color):
    size = GRID * CELL
    image = bpy.data.images.get(name)
    if image is None or tuple(image.size) != (size, size):
        if image is not None:
            bpy.data.images.remove(image)
        image = bpy.data.images.new(name, size, size, alpha=True)
    image.colorspace_settings.name = "Non-Color" if non_color else "sRGB"
    return image


MAP_SUFFIXES = ("Colour", "Emission", "MetalSmooth")


def make_images(kit_name, overrides=None):
    """The three palette maps as Blender images in memory (made or refreshed). Saved only on export (save_images).

    overrides: {swatch name: (colour hex, emission hex or None[, metallic, smoothness])} from the style."""
    overrides = overrides or {}

    def entry(i):
        name, base, glow, metal, smooth = SWATCHES[i]
        override = overrides.get(name, ())
        return tuple(override) + (base, glow, metal, smooth)[len(override):]

    def colour(i):
        return (*_hex(entry(i)[0]), 1.0)

    def emission(i):
        glow = entry(i)[1]
        if glow is None:
            return (0.0, 0.0, 0.0, 1.0)
        return (*_hex(glow), 1.0)

    def metal_smooth(i):
        _colour, _glow, metallic, smoothness = entry(i)
        return (metallic, 0.0, 0.0, smoothness)

    images = []
    for suffix, fn, non_color in zip(MAP_SUFFIXES, (colour, emission, metal_smooth), (False, False, True)):
        image = _image(f"{kit_name}_{suffix}.png", non_color)
        _fill(image, fn)
        images.append(image)
    return images


def save_images(images, folder):
    """Writes the maps as PNG files into folder. Returns the paths."""
    os.makedirs(folder, exist_ok=True)
    paths = []
    for image in images:
        path = os.path.join(folder, image.name)
        if image.packed_file is not None:
            image.unpack(method="REMOVE")
        image.filepath_raw = path
        image.file_format = "PNG"
        image.save()
        paths.append(path)
    return paths


def material_name(kit_name):
    return f"{kit_name}_Kit"


def make_material(kit_name, images):
    """The palette material: the quick look in the panel before export (export bakes the real texture sets), and the
    exported material of parts in no texture set (cockpits)."""
    name = material_name(kit_name)
    old = bpy.data.materials.get(name)
    if old is not None:
        bpy.data.materials.remove(old)
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    bsdf = nodes.get("Principled BSDF")

    uv = nodes.new("ShaderNodeUVMap")
    uv.uv_map = "Palette"

    def texture(image):
        node = nodes.new("ShaderNodeTexImage")
        node.image = image
        node.interpolation = "Closest"
        links.new(uv.outputs["UV"], node.inputs["Vector"])
        return node

    colour, emission, metal = (texture(image) for image in images)
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
    # Solid view colour, so the parts are not white in Solid mode.
    material.diffuse_color = (*_hex(SWATCHES[INDEX["hull"]][1]), 1.0)
    return material


def apply_uvs(obj, palette_only=False):
    """UV layers: "UVMap" first (UV0, unwrapped by texture.py on export) and "Palette" (UV1): every face's palette UVs
    go to the centre of its swatch (read from the 'swatch' face attribute, kept through the bevel). palette_only: the
    part is drawn with the palette maps, so UV0 gets the palette UVs too (no unwrap, no bake)."""
    mesh = obj.data
    base = mesh.uv_layers.new(name="UVMap")
    layer = mesh.uv_layers.new(name="Palette")
    attribute = mesh.attributes.get("swatch")
    for poly in mesh.polygons:
        index = attribute.data[poly.index].value if attribute is not None else 0
        u, v = uv_of(index)
        for loop_index in poly.loop_indices:
            layer.data[loop_index].uv = (u, v)
            if palette_only:
                base.data[loop_index].uv = (u, v)
    if attribute is not None:
        mesh.attributes.remove(attribute)
