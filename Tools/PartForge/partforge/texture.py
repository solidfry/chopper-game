"""
Textures: real UVs, baked surface data, and one texture set per chassis (plus one shared set for all modules).

1. Unwrap: every part gets a UV map ("UVMap", UV0 in Unity) next to its palette UVs ("Palette", UV1). A chassis is
   unwrapped on its own; all modules are packed into one atlas. Texture size follows the surface area (style
   "textures": density in pixels per metre, min and max size).
2. Bake (Cycles, once per set, skin-independent): model-space position and normal, swatch id, ambient occlusion, an
   edge mask (Bevel shader against the true normal), a short-range cavity mask and a tangent-space normal map (fine
   cast-metal noise; painted and metal swatches only, glass and lights stay smooth).
3. Compose (numpy, per skin, skins.py; the first skin's lights-only emission is shared): colour, metallic/smoothness and emission maps; the normal and occlusion maps
   are shared by every skin.

Files (<kit>/Textures): <Kit>_<Set>_Normal.png, _Occlusion.png, and per skin <Kit>_<Set>_<skin>_Colour.png,
_MetalSmooth.png (R metallic, A smoothness: URP's layout) and _Emission.png (only skins with glow lines have their own;
the others share <Kit>_<Set>_Emission.png, the lights).
"""
import math
import os
import struct
import time
import zlib

import bpy
import numpy as np

from . import palette, skins

# density: normal-map pixels per metre; skin_scale: size of the skin maps (colour, metal/smoothness, emission) and
# the occlusion map against the normal map (0.5 = half: a quarter of the file size, the paint is soft-edged anyway).
DEFAULTS = dict(density=110.0, min_size=512, max_size=2048, module_size=1024, skin_scale=0.5, samples=16, margin=8)
SPAN = 40.0  # positions are baked as p / SPAN + 0.5 (every part fits in +-20 m)
BUMP = {"glass": 0.0, "rubber": 0.3, "carbon": 0.2, "neon_green": 0.0, "neon_cyan": 0.0, "neon_pink": 0.0,
        "exhaust": 0.0, "sodium": 0.0, "warning": 0.0, "steel": 0.4, "bare": 0.6, "gunmetal": 0.6, "missile": 0.5}
MODULES_SET = "Modules"


class TextureSet:
    """One texture set: its objects, size, and (after bake) the surface data over the valid texels."""

    def __init__(self, name, objects, size, skin_scale=0.5):
        self.name, self.objects, self.size = name, objects, size
        self.data_size = max(64, int(size * skin_scale))  # skin maps, occlusion and the baked surface data
        self.valid = None      # flat indices of texels that hold surface data
        self.surface = None    # dict for skins.compose
        self.normal = None     # (size, size, 4) floats 0..1
        self.occlusion = None  # (size, size) 0..1


def settings(style):
    return dict(DEFAULTS, **(style.get("textures") or {}))


def texture_sets(state):
    """Chassis sets (one per chassis, named after it) and the shared module set."""
    values = settings(state.style)
    chassis = [(part, obj) for part, obj in state.built.values() if part.kind == "chassis"]
    modules = [obj for part, obj in state.built.values() if part.kind == "module"]
    scale = values["skin_scale"]
    sets = [TextureSet(part.name.replace("Chassis_", ""), [obj], _size(obj, values), scale) for part, obj in chassis]
    sets.append(TextureSet(MODULES_SET, modules, int(values["module_size"]), scale))
    return sets


def _size(obj, values):
    area = sum(polygon.area for polygon in obj.data.polygons)
    side = math.sqrt(area / 0.45) * values["density"]  # about 45 % of the square holds islands (margins, small parts)
    size = 2 ** round(math.log2(max(side, 1.0)))
    return int(min(max(size, values["min_size"]), values["max_size"]))


# -- unwrap ---------------------------------------------------------------------------------------------------------

def unwrap(objects):
    """Smart UV projection + packing of every object's "UVMap" into one 0..1 square."""
    view_layer = bpy.context.view_layer
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.data.uv_layers.active = obj.data.uv_layers["UVMap"]
        obj.select_set(True)
    view_layer.objects.active = objects[0]
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.0, area_weight=0.0,
                             scale_to_bounds=False)
    bpy.ops.uv.pack_islands(rotate=True, margin=0.003)
    bpy.ops.object.mode_set(mode="OBJECT")


# -- bake -----------------------------------------------------------------------------------------------------------

def _id_image(names, overrides_bump=None):
    """Palette-layout image read through the "Palette" UVs: R = swatch index / 32, G = normal-map bump strength."""
    image = bpy.data.images.get("PF Swatch Id") or bpy.data.images.new("PF Swatch Id", palette.GRID * palette.CELL,
                                                                       palette.GRID * palette.CELL, alpha=True)
    image.colorspace_settings.name = "Non-Color"
    bump = dict(BUMP, **(overrides_bump or {}))
    palette._fill(image, lambda i: (i / 32.0, bump.get(names[i], 1.0), 0.0, 1.0))
    return image


def _bake_material(id_image):
    material = bpy.data.materials.get("PF Bake") or bpy.data.materials.new("PF Bake")
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    n = {}

    def node(key, kind, **values):
        n[key] = nodes.new(kind)
        for name, value in values.items():
            setattr(n[key], name, value)
        return n[key]

    node("coords", "ShaderNodeTexCoord")
    node("local", "ShaderNodeAttribute", attribute_type="GEOMETRY", attribute_name=LOCAL)
    node("geometry", "ShaderNodeNewGeometry")
    node("palette_uv", "ShaderNodeUVMap", uv_map="Palette")
    node("id", "ShaderNodeTexImage", image=id_image, interpolation="Closest")
    links.new(n["palette_uv"].outputs["UV"], n["id"].inputs["Vector"])
    node("split_id", "ShaderNodeSeparateColor")
    links.new(n["id"].outputs["Color"], n["split_id"].inputs["Color"])
    for key, source in (("position", n["local"].outputs["Vector"]), ("normal", n["coords"].outputs["Normal"])):
        scale = 1.0 / SPAN if key == "position" else 0.5
        node(key, "ShaderNodeVectorMath", operation="MULTIPLY_ADD")
        links.new(source, n[key].inputs[0])
        n[key].inputs[1].default_value = (scale, scale, scale)
        n[key].inputs[2].default_value = (0.5, 0.5, 0.5)
    node("ao", "ShaderNodeAmbientOcclusion", samples=4)
    n["ao"].inputs["Distance"].default_value = 0.8
    node("cavity", "ShaderNodeAmbientOcclusion", samples=4)
    n["cavity"].inputs["Distance"].default_value = 0.08
    node("bevel", "ShaderNodeBevel", samples=4)
    n["bevel"].inputs["Radius"].default_value = 0.025
    node("dot", "ShaderNodeVectorMath", operation="DOT_PRODUCT")
    links.new(n["bevel"].outputs["Normal"], n["dot"].inputs[0])
    links.new(n["geometry"].outputs["Normal"], n["dot"].inputs[1])
    node("edge", "ShaderNodeMapRange")
    n["edge"].inputs["From Min"].default_value = 1.0
    n["edge"].inputs["From Max"].default_value = 0.8
    links.new(n["dot"].outputs["Value"], n["edge"].inputs["Value"])
    node("masks", "ShaderNodeCombineColor")
    links.new(n["ao"].outputs["AO"], n["masks"].inputs[0])
    links.new(n["edge"].outputs["Result"], n["masks"].inputs[1])
    links.new(n["cavity"].outputs["AO"], n["masks"].inputs[2])
    # Fine cast-metal grain and a larger hammered dent pattern, scaled per swatch (G of the id image).
    node("grain", "ShaderNodeTexNoise")
    n["grain"].inputs["Scale"].default_value = 90.0
    n["grain"].inputs["Detail"].default_value = 4.0
    links.new(n["local"].outputs["Vector"], n["grain"].inputs["Vector"])
    node("dents", "ShaderNodeTexVoronoi", feature="SMOOTH_F1")
    n["dents"].inputs["Scale"].default_value = 7.0
    links.new(n["local"].outputs["Vector"], n["dents"].inputs["Vector"])
    node("height", "ShaderNodeMath", operation="MULTIPLY_ADD")
    links.new(n["dents"].outputs["Distance"], n["height"].inputs[0])
    n["height"].inputs[1].default_value = 0.35
    links.new(n["grain"].outputs["Fac"], n["height"].inputs[2])
    node("bump_strength", "ShaderNodeMath", operation="MULTIPLY")
    links.new(n["split_id"].outputs["Green"], n["bump_strength"].inputs[0])
    n["bump_strength"].inputs[1].default_value = 0.12
    node("bump", "ShaderNodeBump")
    n["bump"].inputs["Distance"].default_value = 0.002
    links.new(n["bump_strength"].outputs["Value"], n["bump"].inputs["Strength"])
    links.new(n["height"].outputs["Value"], n["bump"].inputs["Height"])
    node("diffuse", "ShaderNodeBsdfDiffuse")
    links.new(n["bump"].outputs["Normal"], n["diffuse"].inputs["Normal"])
    node("emission", "ShaderNodeEmission")
    node("output", "ShaderNodeOutputMaterial")
    node("target", "ShaderNodeTexImage")
    nodes.active = n["target"]
    return material, n


def _bake_image(name, size):
    image = bpy.data.images.get(name)
    if image is not None:
        bpy.data.images.remove(image)
    image = bpy.data.images.new(name, size, size, alpha=True, float_buffer=True)
    image.colorspace_settings.name = "Non-Color"
    image.generated_color = (0, 0, 0, 0)
    return image


def _pixels(image):
    data = np.empty(image.size[0] * image.size[1] * 4, dtype=np.float32)
    image.pixels.foreach_get(data)
    return data.reshape(image.size[1], image.size[0], 4)


LOCAL = "pf_local"  # point attribute on the bake object: each vertex's position in its own part's space
MODULE_SPACING = 4.0


def _bake_object(objects, material):
    """One temporary object that holds copies of the set's parts (modules side by side on a grid, so they do not
    shade each other), with their own-space positions in the LOCAL attribute. One bake per pass, not one per part."""
    collection = bpy.data.collections.get("PF Bake") or bpy.data.collections.new("PF Bake")
    if collection.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(collection)
    columns = max(1, math.ceil(math.sqrt(len(objects))))
    copies = []
    for i, obj in enumerate(objects):
        mesh = obj.data.copy()
        attribute = mesh.attributes.new(LOCAL, "FLOAT_VECTOR", "POINT")
        attribute.data.foreach_set("vector", [c for v in mesh.vertices for c in v.co])
        mesh.materials.clear()
        mesh.materials.append(material)
        copy = bpy.data.objects.new(f"PF Bake {obj.name}", mesh)
        collection.objects.link(copy)
        copy.location = ((i % columns) * MODULE_SPACING, (i // columns) * MODULE_SPACING, 0.0)
        copies.append(copy)
    bpy.ops.object.select_all(action="DESELECT")
    for copy in copies:
        copy.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    if len(copies) > 1:
        bpy.ops.object.join()
    return bpy.context.view_layer.objects.active


def bake(sets, style, names):
    """Unwraps and bakes every set; fills set.valid, set.surface, set.normal, set.occlusion. Each set bakes alone
    (every other object is left out of the render), from one temporary object."""
    values = settings(style)
    scene = bpy.context.scene
    engine, samples = scene.render.engine, scene.cycles.samples
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    bake_settings = scene.render.bake
    bake_settings.margin = int(values["margin"])
    bake_settings.margin_type = "EXTEND"
    bake_settings.use_clear = True
    bake_settings.use_selected_to_active = False
    bake_settings.normal_space = "TANGENT"
    material, n = _bake_material(_id_image(names, style.get("bump")))
    links = material.node_tree.links
    hidden = {}
    for texture_set in sets:
        for obj in texture_set.objects:
            for collection in obj.users_collection:
                hidden.setdefault(collection.name, collection.hide_viewport)
                collection.hide_viewport = False  # unwrapping needs them in the view layer
    render_hidden = {obj.name: obj.hide_render for obj in scene.objects}
    try:
        for texture_set in sets:
            started = time.time()
            unwrap(texture_set.objects)
            target = _bake_object(texture_set.objects, material)
            for obj in scene.objects:
                obj.hide_render = obj is not target
            maps = {}
            passes = (("position", "EMIT", 1), ("normal", "EMIT", 1), ("id", "EMIT", 1),
                      ("masks", "EMIT", int(values["samples"])), ("tangent", "NORMAL", 2))
            for key, kind, count in passes:
                image = _bake_image(f"PF Bake {key}", texture_set.size if key == "tangent" else texture_set.data_size)
                n["target"].image = image
                for link in list(n["output"].inputs["Surface"].links) + list(n["emission"].inputs["Color"].links):
                    links.remove(link)
                if kind == "EMIT":
                    source = n["id"].outputs["Color"] if key == "id" else n[key].outputs[0]
                    links.new(source, n["emission"].inputs["Color"])
                    links.new(n["emission"].outputs[0], n["output"].inputs["Surface"])
                else:
                    links.new(n["diffuse"].outputs[0], n["output"].inputs["Surface"])
                scene.cycles.samples = count
                scene.cycles.filter_width = 0.01 if count == 1 else 1.5
                bpy.ops.object.bake(type=kind)
                maps[key] = _pixels(image)
                bpy.data.images.remove(image)
            mesh = target.data
            bpy.data.objects.remove(target, do_unlink=True)
            bpy.data.meshes.remove(mesh)
            _decode(texture_set, maps, names)
            print(f"PartForge: baked {texture_set.name} ({texture_set.size} px) in {time.time() - started:.0f} s")
    finally:
        for name, viewport in hidden.items():
            collection = bpy.data.collections.get(name)
            if collection is not None:
                collection.hide_viewport = viewport
        for obj in scene.objects:
            if obj.name in render_hidden:
                obj.hide_render = render_hidden[obj.name]
        scratch = bpy.data.collections.get("PF Bake")
        if scratch is not None:
            bpy.data.collections.remove(scratch)
        scene.render.engine, scene.cycles.samples = engine, samples
        scene.cycles.filter_width = 1.5


def _unity(v):
    """Blender model space -> Unity model space (see geo.to_blender)."""
    return np.stack([-v[:, 0], v[:, 2], -v[:, 1]], axis=1)


def _decode(texture_set, maps, names):
    flat = {key: value.reshape(-1, 4) for key, value in maps.items()}
    # Texels the bake reached (islands + margin): a baked position is never exactly 0 (that is -20 m on every axis).
    valid = np.nonzero(flat["position"][:, :3].sum(axis=1) > 1e-6)[0]
    position = _unity((flat["position"][valid, :3] - 0.5) * SPAN)
    normal = _unity(flat["normal"][valid, :3] * 2.0 - 1.0)
    normal /= np.maximum(np.linalg.norm(normal, axis=1, keepdims=True), 1e-6)
    swatch = np.clip(np.round(flat["id"][valid, 0] * 32.0), 0, len(names) - 1).astype(np.int32)
    masks = flat["masks"][valid]
    low, high = position[:, 1].min(), position[:, 1].max()
    texture_set.valid = valid
    texture_set.surface = dict(position=position, normal=normal, swatch=swatch, ao=masks[:, 0],
                               edge=np.clip(masks[:, 1], 0, 1), cavity=masks[:, 2],
                               height=(position[:, 1] - low) / max(high - low, 1e-3),
                               soot=_soot(position, swatch, names.index("exhaust")))
    skins.prepare(texture_set.surface)
    tangent = maps["tangent"].reshape(-1, 4).copy()
    outside = tangent[:, :3].sum(axis=1) < 1e-6  # a baked normal is never (0, 0, 0)
    tangent[outside] = (0.5, 0.5, 1.0, 1.0)  # flat normal between islands (mipmaps blend into it)
    texture_set.normal = tangent.reshape(maps["tangent"].shape)
    occlusion = np.ones(texture_set.data_size * texture_set.data_size, dtype=np.float32)
    occlusion[valid] = 0.35 + 0.65 * masks[:, 0]
    texture_set.occlusion = occlusion.reshape(texture_set.data_size, texture_set.data_size)


def _soot(position, swatch, exhaust):
    """Soot behind every exhaust: sources are the exhaust faces (grouped per 0.35 m), each leaves a plume that
    widens and fades over about 2 m behind it (-z)."""
    points = position[swatch == exhaust]
    soot = np.zeros(len(position), dtype=np.float32)
    if len(points) == 0:
        return soot
    keys = np.round(points / 0.35).astype(np.int64)
    _unique, groups = np.unique(keys, axis=0, return_inverse=True)
    groups = groups.ravel()
    for g in range(groups.max() + 1):
        source = points[groups == g].mean(axis=0)
        along = source[2] - position[:, 2]
        lateral2 = (position[:, 0] - source[0]) ** 2 + (position[:, 1] - source[1]) ** 2
        width = 0.18 + 0.12 * np.clip(along, 0, None)
        plume = np.exp(-lateral2 / (width * width)) * np.exp(-np.clip(along, 0, None) / 2.0)
        soot = np.maximum(soot, np.where(along > -0.05, plume, 0.0))
    return np.clip(soot * 0.9, 0, 0.9)


# -- compose and write ----------------------------------------------------------------------------------------------

def palette_rows(style):
    """Per swatch: linear colour, linear emission, metallic, smoothness (with the style's overrides)."""
    overrides = style.get("palette") or {}
    rows = []
    for name, base, glow, metal, smooth in palette.SWATCHES:
        values = tuple(overrides.get(name, ())) + (base, glow, metal, smooth)[len(overrides.get(name, ())):]
        colour = skins.hex_linear(values[0])
        emission = skins.hex_linear(values[1]) if values[1] else np.zeros(3, dtype=np.float32)
        rows.append((colour, emission, values[2], values[3]))
    return rows


def _compose(texture_set, style, skin):
    """One skin on one set: (colour, metal_smooth, emission) as (size, size, 4) pixel arrays (sRGB colours)."""
    names = [name for name, *_ in palette.SWATCHES]
    colour, emission, metallic, smoothness = skins.compose(skin, texture_set.surface, palette_rows(style), names,
                                                           style_wear=max(style.get("wear", 0.0), 0.15))
    size, valid = texture_set.data_size, texture_set.valid
    zero = np.zeros_like(metallic)
    # Metallic and smoothness in 32 steps: no visible change, half the file size.
    metallic, smoothness = np.round(metallic * 32) / 32, np.round(smoothness * 32) / 32
    return (_image_array(size, valid, skins.linear_to_srgb(colour)),
            _image_array(size, valid, np.stack([metallic, zero, zero, smoothness], axis=1)),
            _image_array(size, valid, skins.linear_to_srgb(emission)))


def _image_array(size, valid, values):
    out = np.zeros((size * size, 4), dtype=np.float32)
    out[:, 3] = 1.0
    out[valid, :values.shape[1]] = values
    if values.shape[1] == 3:
        out[valid, 3] = 1.0
    return out.reshape(size, size, 4)


def write_png(path, pixels, channels):
    """Writes rows-from-the-bottom float pixels (h, w, 4) as an 8-bit PNG with 1 (grey), 3 (RGB) or 4 (RGBA) channels
    ("up" filter: small files for flat paint)."""
    data = np.clip(np.round(pixels[::-1] * 255.0), 0, 255).astype(np.uint8)
    data = data[:, :, 0:1] if channels == 1 else data[:, :, :channels]
    height, width = data.shape[:2]
    rows = data.reshape(height, width * channels)
    up = rows.copy()
    up[1:] = rows[1:] - rows[:-1]  # uint8 wraps, as the PNG "up" filter expects
    raw = np.concatenate([np.full((height, 1), 2, dtype=np.uint8), up], axis=1)
    raw[0, 0] = 0
    raw[0, 1:] = rows[0]
    colour_type = {1: 0, 3: 2, 4: 6}[channels]

    def chunk(kind, payload):
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)

    with open(path, "wb") as handle:
        handle.write(b"\x89PNG\r\n\x1a\n")
        handle.write(chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, colour_type, 0, 0, 0)))
        handle.write(chunk(b"IDAT", zlib.compress(raw.tobytes(), 7)))
        handle.write(chunk(b"IEND", b""))


def file_names(kit, set_name, skin_key=None):
    stem = f"{kit}_{set_name}"
    if skin_key is None:
        return {"normal": f"{stem}_Normal.png", "occlusion": f"{stem}_Occlusion.png", "emission": f"{stem}_Emission.png"}
    return {"colour": f"{stem}_{skin_key}_Colour.png", "metalSmooth": f"{stem}_{skin_key}_MetalSmooth.png",
            "emission": f"{stem}_{skin_key}_Emission.png"}


def write(kit, sets, style, skin_list, folder):
    """Composes every skin on every set and writes the maps (one skin at a time: the arrays are large). Returns the
    manifest's "textureSets" and "skins" entries (paths relative to the kit folder)."""
    os.makedirs(folder, exist_ok=True)
    for old in os.listdir(folder):
        # Old maps of this kit (renamed sets, removed skins, the old palette maps) go.
        if old.startswith(f"{kit}_") and (old.endswith(".png") or old.endswith(".png.meta")):
            os.remove(os.path.join(folder, old))
    rel = os.path.basename(folder)
    set_entries = []
    skin_entries = [dict(id=skin["id"], key=skin["key"], name=skin["name"], description=skin.get("description", ""),
                         sets=[]) for skin in skin_list]
    for texture_set in sets:
        shared = file_names(kit, texture_set.name)
        write_png(os.path.join(folder, shared["normal"]), texture_set.normal, 3)
        write_png(os.path.join(folder, shared["occlusion"]), np.repeat(texture_set.occlusion[:, :, None], 4, axis=2), 1)
        set_entries.append(dict(name=texture_set.name, size=texture_set.size, material=f"{kit}_{texture_set.name}",
                                normal=f"{rel}/{shared['normal']}", occlusion=f"{rel}/{shared['occlusion']}",
                                emission=f"{rel}/{shared['emission']}"))
        lights = None
        started = time.time()
        for skin, entry in zip(skin_list, skin_entries):
            colour, metal_smooth, emission = _compose(texture_set, style, skin)
            files = file_names(kit, texture_set.name, skin["key"])
            write_png(os.path.join(folder, files["colour"]), colour, 3)
            write_png(os.path.join(folder, files["metalSmooth"]), metal_smooth, 4)
            if lights is None:
                # The first skin's emission (the lights only, for skins without glow) is the shared one.
                lights = emission
                write_png(os.path.join(folder, shared["emission"]), lights, 3)
            if np.allclose(emission, lights, atol=1 / 255):
                glow_file = shared["emission"]
            else:
                write_png(os.path.join(folder, files["emission"]), emission, 3)
                glow_file = files["emission"]
            entry["sets"].append(dict(set=texture_set.name, colour=f"{rel}/{files['colour']}",
                                      metalSmooth=f"{rel}/{files['metalSmooth']}", emission=f"{rel}/{glow_file}"))
        print(f"PartForge: painted {len(skin_list)} skins on {texture_set.name} in {time.time() - started:.0f} s")
    return set_entries, skin_entries


# -- Blender materials (previews, viewport) --------------------------------------------------------------------------

def preview_material(kit, set_name, folder, skin_key):
    """A Blender material that matches the Unity one for this set and skin (textures loaded from the kit folder)."""
    name = f"{kit}_{set_name}"
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    shared, own = file_names(kit, set_name), file_names(kit, set_name, skin_key)
    emission_file = own["emission"] if os.path.exists(os.path.join(folder, own["emission"])) else shared["emission"]

    def texture(file_name, colour):
        node = nodes.new("ShaderNodeTexImage")
        node.image = bpy.data.images.load(os.path.join(folder, file_name), check_existing=True)
        node.image.reload()
        node.image.colorspace_settings.name = "sRGB" if colour else "Non-Color"
        uv = nodes.new("ShaderNodeUVMap")
        uv.uv_map = "UVMap"
        links.new(uv.outputs["UV"], node.inputs["Vector"])
        return node

    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    output = nodes.new("ShaderNodeOutputMaterial")
    links.new(bsdf.outputs[0], output.inputs["Surface"])
    colour = texture(own["colour"], True)
    occlusion = texture(shared["occlusion"], False)
    # Half of the occlusion on the base colour, like URP's occlusion on ambient light.
    half = nodes.new("ShaderNodeMath")
    half.operation = "MULTIPLY_ADD"
    links.new(occlusion.outputs["Color"], half.inputs[0])
    half.inputs[1].default_value = 0.5
    half.inputs[2].default_value = 0.5
    shade = nodes.new("ShaderNodeVectorMath")
    shade.operation = "MULTIPLY"
    links.new(colour.outputs["Color"], shade.inputs[0])
    links.new(half.outputs["Value"], shade.inputs[1])
    links.new(shade.outputs["Vector"], bsdf.inputs["Base Color"])
    metal = texture(own["metalSmooth"], False)
    split = nodes.new("ShaderNodeSeparateColor")
    links.new(metal.outputs["Color"], split.inputs["Color"])
    links.new(split.outputs["Red"], bsdf.inputs["Metallic"])
    rough = nodes.new("ShaderNodeMath")
    rough.operation = "SUBTRACT"
    rough.inputs[0].default_value = 1.0
    links.new(metal.outputs["Alpha"], rough.inputs[1])
    links.new(rough.outputs["Value"], bsdf.inputs["Roughness"])
    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.uv_map = "UVMap"
    links.new(texture(shared["normal"], False).outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], bsdf.inputs["Normal"])
    links.new(texture(emission_file, True).outputs["Color"], bsdf.inputs["Emission Color"])
    bsdf.inputs["Emission Strength"].default_value = 4.0
    material.diffuse_color = (0.3, 0.32, 0.3, 1.0)
    return material
