"""
Generation steps, used by the command line (generate.py runs them all) and by the Blender panel (ui.py, one per button):

    generate(style)        build every part into the open file + lay out the showcase (choppers with loadouts, module grid)
    export(state, folder)  texture sets (unwrap, bake, every skin), one FBX per part, manifest
    render_previews(...)   Cycles line-up, close-ups, four-view sheets, module sheet, skin sheet
    save_blend(path)       the .blend, textures packed in, with the panel loader
"""
import json
import math
import os

import bpy
from mathutils import Vector

from . import kits, palette, parts, runtime, skins, styles, texture
from .geo import to_blender

EXPORT_COLLECTION = "PartForge: export parts"
CHOPPERS_COLLECTION = "PartForge: choppers"
MODULES_COLLECTION = "PartForge: modules"
STAGE_COLLECTION = "PartForge: stage"
MODULE_COLUMNS = 6
MODULE_GRID_Y = 30.0
CHASSIS_SPACING = 14.0
CACHE_DIR = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "cache")  # not in git


class State:
    """What generate() made: the export parts (by name) and the showcase objects."""

    def __init__(self, kit, style, specs, built, images, shown, tiles):
        self.kit, self.style, self.specs = kit, style, specs
        self.built, self.images, self.shown, self.tiles = built, images, shown, tiles
        self.texture_folder, self.skins, self.sets = None, [], []  # set by export()
        self.runtime_folder = None  # the runtime skin shader's result per skin (previews), set by export()


def chassis_specs(style):
    """The style's own chassis shapes (saved styles may carry them), else the defaults in kits.py."""
    return json.loads(json.dumps(style.get("chassis") or kits.CHASSIS))


# -- generate -------------------------------------------------------------------------------------------------------

def clear():
    """Removes what PartForge made in the open file (not your own objects, not the stage)."""
    for name in (EXPORT_COLLECTION, CHOPPERS_COLLECTION, MODULES_COLLECTION):
        collection = bpy.data.collections.get(name)
        if collection is None:
            continue
        for obj in list(collection.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(collection)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)


def generate(style, kit=None):
    kit = kit or style["kit"]
    parts.use_style(style)
    clear()
    scene = bpy.context.scene
    export_collection = _collection(scene, EXPORT_COLLECTION)
    images = palette.make_images(kit, style.get("palette"))
    material = palette.make_material(kit, images)
    specs = chassis_specs(style)

    built = {}
    for part in [parts.chassis(spec, style) for spec in specs] + parts.all_modules():
        detail = (part.seed, style.get("detail")) if part.kind == "chassis" and style.get("detail") else None
        obj = part.builder.to_object(part.name, export_collection, detail=detail)
        palette.apply_uvs(obj)
        # One material per kit: Geometry Nodes can leave an empty slot behind, so start clean.
        obj.data.materials.clear()
        obj.data.materials.append(material)
        for polygon in obj.data.polygons:
            polygon.material_index = 0
        # Socket names repeat across parts, so they carry the part name here; export() strips it.
        for slot, index, position, scale in part.sockets:
            empty = bpy.data.objects.new(f"{part.name}:SOCKET_{slot}_{index}", None)
            empty.empty_display_type = "ARROWS"
            empty.empty_display_size = 0.4
            export_collection.objects.link(empty)
            empty.parent = obj
            empty.location = to_blender(position)
            empty.scale = (scale, scale, scale)
        built[part.name] = (part, obj)

    _ensure_stage(scene)
    shown, tiles = _showcase(scene, built, specs)
    # The export parts all sit at the origin: keep them out of the view and the renders.
    export_collection.hide_render = True
    export_collection.hide_viewport = True
    return State(kit, style, specs, built, images, shown, tiles)


def _collection(scene, name):
    collection = bpy.data.collections.get(name)
    if collection is None:
        collection = bpy.data.collections.new(name)
    if collection.name not in scene.collection.children:
        scene.collection.children.link(collection)
    return collection


def _showcase(scene, built, specs):
    """Assembled choppers in a row (preview loadouts on the sockets) and a grid of all modules behind them.
    Copies share mesh data with the export parts. Returns (choppers, tiles)."""
    choppers_collection = _collection(scene, CHOPPERS_COLLECTION)
    modules_collection = _collection(scene, MODULES_COLLECTION)
    shown, tiles = [], []
    count = len(specs)
    for i, spec in enumerate(specs):
        part, source = built[f"Chassis_{spec['name']}"]
        holder = bpy.data.objects.new(f"Show_{spec['name']}", source.data)
        choppers_collection.objects.link(holder)
        # Unity x = -Blender x; line them up left to right as seen from the front-left camera.
        holder.location = Vector(((i - (count - 1) / 2) * -CHASSIS_SPACING, 0, 2.6))
        holder.rotation_euler = (0, 0, math.radians(-35))
        shown.append(holder)
        loadout = kits.PREVIEW_LOADOUTS.get(spec["name"], {})
        for slot, index, position, scale in part.sockets:
            names = loadout.get(slot, [])
            # Twin rotors: the one rotor module shows on every rotor hub.
            name = names[min(index, len(names) - 1)] if slot == "Rotor" and names else (names[index] if index < len(names) else None)
            if name not in built:
                continue
            _module, module_source = built[name]
            attached = bpy.data.objects.new(f"{holder.name}_{slot}{index}", module_source.data)
            choppers_collection.objects.link(attached)
            attached.parent = holder
            attached.location = to_blender(position)
            attached.scale = (scale, scale, scale)

    modules = [obj for part, obj in built.values() if part.kind == "module"]
    for i, source in enumerate(modules):
        tile = bpy.data.objects.new(f"Tile_{source.name}", source.data)
        modules_collection.objects.link(tile)
        col, row = i % MODULE_COLUMNS, i // MODULE_COLUMNS
        big = source.name.startswith("Rotor")
        tile.location = Vector(((col - (MODULE_COLUMNS - 1) / 2) * -3.2, MODULE_GRID_Y + row * 3.6, 1.0 if not big else 0.5))
        tile.rotation_euler = (0, 0, math.radians(-40))
        if big:
            tile.scale = (0.35, 0.35, 0.35)
        tiles.append(tile)
    return shown, tiles


# -- export ---------------------------------------------------------------------------------------------------------

def export(state, out_dir):
    """Texture sets (unwrap, bake, every skin: texture.py, skins.py), one FBX per part (sockets as SOCKET_<Slot>_<Index>
    children, the material named <kit>_<set>) and the manifest. After this the file shows the baked textures."""
    models_dir = os.path.join(out_dir, "Models")
    texture_dir = os.path.join(out_dir, "Textures")
    os.makedirs(models_dir, exist_ok=True)
    names = [name for name, *_ in palette.SWATCHES]
    sets = texture.texture_sets(state)
    texture.bake(sets, state.style, names)
    skin_list = skins.every(state.style)
    runtime_dir = os.path.join(CACHE_DIR, state.kit, "runtime")
    set_entries, skin_entries = texture.write(state.kit, sets, state.style, skin_list, texture_dir, runtime_dir)
    state.texture_folder, state.skins, state.sets = texture_dir, skin_list, sets
    state.runtime_folder = runtime_dir
    set_of = {}
    for texture_set in sets:
        material = texture.preview_material(state.kit, texture_set.name, texture_dir, skin_list[0]["key"])
        for obj in texture_set.objects:
            obj.data.materials.clear()
            obj.data.materials.append(material)
            set_of[obj.name] = texture_set.name
        texture_set.surface = None  # large; not needed after the maps are written
    manifest = {
        "kit": state.kit,
        "generator": "Tools/PartForge (Blender)",
        "style": state.style.get("name", ""),
        "units": "metres",
        "axes": "Unity space: +X right, +Y up, +Z forward. Socket rotation is identity (modules point +Z).",
        "uv": "UV0 = baked texture set (unique unwrap); UV1 = palette swatch (cell centre of a 5 x 5 grid).",
        "textureSets": set_entries,
        "skins": skin_entries,
        "runtime": {"tableWidth": runtime.TABLE_WIDTH, "styleWear": runtime.style_wear(state.style),
                    "patterns": list(runtime.PATTERNS), "coverTable": list(runtime.COVER_TABLE)},
        "parts": [],
    }

    export_collection = bpy.data.collections[EXPORT_COLLECTION]
    export_collection.hide_viewport = False  # hidden objects cannot be selected for the exporter
    try:
        for name, (part, obj) in state.built.items():
            prefix = f"{part.name}:"
            for child in obj.children:
                child.name = child.name[len(prefix):] if child.name.startswith(prefix) else child.name
            file_name = f"{part.name}.fbx"
            _export_fbx(obj, os.path.join(models_dir, file_name))
            for child in obj.children:
                child.name = prefix + child.name
            manifest["parts"].append({
                "name": part.name, "file": f"Models/{file_name}", "kind": part.kind, "slot": part.slot,
                "moduleIds": part.module_ids, "triangles": _triangles(obj), "textureSet": set_of[obj.name],
                "sockets": [{"name": f"SOCKET_{slot}_{index}", "slot": slot, "index": index, "position": list(position),
                             "scale": scale}
                            for slot, index, position, scale in part.sockets],
            })
    finally:
        export_collection.hide_viewport = True

    with open(os.path.join(out_dir, f"{state.kit}.manifest.json"), "w") as handle:
        json.dump(manifest, handle, indent=2)
    return manifest


def show_skin(state, key, use_runtime=False):
    """Puts one skin on every texture set (Blender materials; after export): the baked skin, or (use_runtime) the
    runtime skin shader's result."""
    folder = state.runtime_folder if use_runtime else state.texture_folder
    for texture_set in state.sets:
        texture.preview_material(state.kit, texture_set.name, folder, key)


def _export_fbx(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    for child in obj.children:
        child.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="EDGE", add_leaf_bones=False, bake_anim=False, path_mode="STRIP", use_mesh_modifiers=True)


def _triangles(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


# -- stage and previews ---------------------------------------------------------------------------------------------

STAGE_VERSION = 2  # bump to replace the stage in saved files (lights, world, floor) on the next build


def _ensure_stage(scene):
    """Review studio: neutral grey world (sky light from above, darker below), a floor that fades into the horizon,
    a soft key from the camera side, a fill from the other side and a cool rim behind; AgX with a little contrast.
    Made once per file and kept across rebuilds (tweak it freely); STAGE_VERSION replaces an older stage."""
    stage = _collection(scene, STAGE_COLLECTION)
    if scene.get("partforge_stage", 0) != STAGE_VERSION:
        for obj in list(stage.objects):
            if obj.name != "PF Camera" and obj.name != "PF Aim":
                bpy.data.objects.remove(obj, do_unlink=True)
        scene["partforge_stage"] = STAGE_VERSION
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 88
    scene.view_settings.view_transform = "AgX"
    try:
        scene.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        pass
    scene.view_settings.exposure = 0.4

    world = bpy.data.worlds.get("PartForge World") or bpy.data.worlds.new("PartForge World")
    world.use_nodes = True
    nodes, links = world.node_tree.nodes, world.node_tree.links
    if nodes.get("PF Sky") is None:
        nodes.clear()
        coords = nodes.new("ShaderNodeTexCoord")
        split = nodes.new("ShaderNodeSeparateXYZ")
        ramp = nodes.new("ShaderNodeValToRGB")
        ramp.name = "PF Sky"
        ramp.color_ramp.elements[0].position = 0.42
        ramp.color_ramp.elements[0].color = HORIZON_LOW
        ramp.color_ramp.elements[1].position = 0.75
        ramp.color_ramp.elements[1].color = (0.42, 0.45, 0.48, 1.0)
        mid = ramp.color_ramp.elements.new(0.5)
        mid.color = HORIZON
        to_ramp = nodes.new("ShaderNodeMapRange")
        to_ramp.inputs["From Min"].default_value = -1.0
        background = nodes.new("ShaderNodeBackground")
        background.inputs["Strength"].default_value = 1.0
        output = nodes.new("ShaderNodeOutputWorld")
        links.new(coords.outputs["Generated"], split.inputs[0])
        links.new(split.outputs["Z"], to_ramp.inputs["Value"])
        links.new(to_ramp.outputs["Result"], ramp.inputs["Fac"])
        links.new(ramp.outputs["Color"], background.inputs["Color"])
        links.new(background.outputs[0], output.inputs[0])
    scene.world = world

    def sun(name, energy, rotation, colour, angle, shadows=True):
        if bpy.data.objects.get(name) is not None:
            return
        data = bpy.data.lights.new(name, "SUN")
        data.energy = energy
        data.color = colour
        data.angle = math.radians(angle)
        data.use_shadow = shadows
        obj = bpy.data.objects.new(name, data)
        obj.rotation_euler = [math.radians(a) for a in rotation]
        stage.objects.link(obj)

    # Rotations are (tilt from straight down, 0, heading); the camera looks along +Y from the front left.
    sun("PF Key", 4.0, (52, 0, -40), (1.0, 0.96, 0.9), 6)
    sun("PF Fill", 1.4, (68, 0, 60), (0.85, 0.92, 1.0), 25, shadows=False)
    sun("PF Rim", 3.0, (60, 0, 165), (0.75, 0.9, 1.0), 4)
    sun("PF Top", 0.8, (8, 0, 0), (1.0, 1.0, 1.0), 40, shadows=False)

    if bpy.data.objects.get("PF Ground") is None:
        size = 400.0
        mesh = bpy.data.meshes.new("PF Ground")
        mesh.from_pydata([(-size, -size, 0), (size, -size, 0), (size, size, 0), (-size, size, 0)], [], [(0, 1, 2, 3)])
        mesh.materials.append(_ground_material())
        stage.objects.link(bpy.data.objects.new("PF Ground", mesh))

    if bpy.data.objects.get("PF Camera") is None:
        data = bpy.data.cameras.new("PF Camera")
        camera = bpy.data.objects.new("PF Camera", data)
        stage.objects.link(camera)
        aim = bpy.data.objects.new("PF Aim", None)
        stage.objects.link(aim)
        track = camera.constraints.new("TRACK_TO")
        track.target = aim
        track.track_axis = "TRACK_NEGATIVE_Z"
        track.up_axis = "UP_Y"
        _aim_lineup(camera, aim)
    scene.camera = bpy.data.objects["PF Camera"]


HORIZON = (0.16, 0.17, 0.18, 1.0)
HORIZON_LOW = (0.05, 0.05, 0.05, 1.0)


def _ground_material():
    """Mid-grey concrete floor with a faint 1 m grid (scale reference) that fades into the horizon colour."""
    material = bpy.data.materials.get("PF Ground") or bpy.data.materials.new("PF Ground")
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    coords = nodes.new("ShaderNodeTexCoord")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.75
    grid = nodes.new("ShaderNodeTexBrick")
    grid.inputs["Scale"].default_value = 1.0
    grid.inputs["Mortar Size"].default_value = 0.012
    grid.inputs["Color1"].default_value = (0.2, 0.2, 0.2, 1)
    grid.inputs["Color2"].default_value = (0.19, 0.19, 0.19, 1)
    grid.inputs["Mortar"].default_value = (0.14, 0.14, 0.14, 1)
    grid.offset = 0.0
    grid.squash = 1.0
    links.new(coords.outputs["Object"], grid.inputs["Vector"])
    links.new(grid.outputs["Color"], bsdf.inputs["Base Color"])
    # Fade to the horizon: far floor = the world colour, so there is no hard edge.
    distance = nodes.new("ShaderNodeVectorMath")
    distance.operation = "LENGTH"
    links.new(coords.outputs["Object"], distance.inputs[0])
    fade = nodes.new("ShaderNodeMapRange")
    fade.inputs["From Min"].default_value = 40.0
    fade.inputs["From Max"].default_value = 160.0
    links.new(distance.outputs["Value"], fade.inputs["Value"])
    far = nodes.new("ShaderNodeEmission")
    far.inputs["Color"].default_value = HORIZON
    mix = nodes.new("ShaderNodeMixShader")
    links.new(fade.outputs["Result"], mix.inputs["Fac"])
    links.new(bsdf.outputs[0], mix.inputs[1])
    links.new(far.outputs[0], mix.inputs[2])
    output = nodes.new("ShaderNodeOutputMaterial")
    links.new(mix.outputs[0], output.inputs["Surface"])
    return material


def _aim_lineup(camera, aim):
    aim.location = (0, 0, 2.2)
    camera.location = (-22, -52, 11)
    camera.data.lens = 32


def _set_render_visible(objects, visible):
    for obj in objects:
        obj.hide_render = not visible
        for child in obj.children:
            child.hide_render = not visible


def _render(scene, path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


# Review views in the chopper's own frame (Blender: forward -Y, left +X, up +Z): direction from the chopper, label.
REVIEW_VIEWS = (((0.8, -1.0, 0.35), "front left"), ((-0.85, 1.0, 0.45), "rear right"),
                ((-1.0, 0.0, 0.1), "right side"), ((0.12, 0.2, 1.0), "top"))


def _review_sheet(scene, camera, aim, holder, path):
    """Four views of one chopper (front left, rear right, right side, top) in one 2 x 2 image."""
    import numpy as np

    width, height = scene.render.resolution_x, scene.render.resolution_y
    scene.render.resolution_x, scene.render.resolution_y = width // 2, height // 2
    corners = _world_corners(holder)
    centre = sum(corners, Vector()) / len(corners)
    radius = max((c - centre).length for c in corners)
    tiles = []
    folder = os.path.dirname(path)
    try:
        for i, (direction, _label) in enumerate(REVIEW_VIEWS):
            offset = holder.matrix_world.to_3x3() @ Vector(direction).normalized()
            aim.location = centre
            camera.location = centre + offset * radius * 2.3
            camera.data.lens = 50
            tile = os.path.join(folder, f".view_{i}.jpg")
            _render(scene, tile)
            image = bpy.data.images.load(tile)
            pixels = np.empty(len(image.pixels), dtype=np.float32)
            image.pixels.foreach_get(pixels)
            tiles.append(pixels.reshape(image.size[1], image.size[0], 4))
            bpy.data.images.remove(image)
            os.remove(tile)
    finally:
        scene.render.resolution_x, scene.render.resolution_y = width, height
    # Image rows go up from the bottom: the first two views are the top row.
    sheet = np.concatenate([np.concatenate(tiles[2:4], axis=1), np.concatenate(tiles[0:2], axis=1)], axis=0)
    out = bpy.data.images.new("PF Review", sheet.shape[1], sheet.shape[0])
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = path
    out.file_format = "JPEG"
    out.save()
    bpy.data.images.remove(out)


SKIN_SHEET_CHASSIS = "Gunship"


def _skin_sheet(state, scene, camera, aim, path, use_runtime=False):
    """Every skin on one chassis (close-up, half size), two columns, the skin name in the corner of each tile; baked
    skins, or (use_runtime) the runtime skin shader's result."""
    import numpy as np

    names = [spec["name"] for spec in state.specs]
    index = names.index(SKIN_SHEET_CHASSIS) if SKIN_SHEET_CHASSIS in names else 0
    holder, spec = state.shown[index], state.specs[index]
    for other in state.shown:
        _set_render_visible([other], other is holder)
    size = max(1.0, spec["width"] / 2.0)
    aim.location = holder.location + Vector((0, 0, 0.6 * size))
    camera.location = holder.location + Vector((-11, -7, 1.8)) * size
    camera.data.lens = 42
    label = _label(camera)
    width, height = scene.render.resolution_x, scene.render.resolution_y
    scene.render.resolution_x, scene.render.resolution_y = width // 2, height // 2
    tiles = []
    try:
        for skin in state.skins:
            show_skin(state, skin["key"], use_runtime)
            label.data.body = skin["name"].upper() + ("  (RUNTIME)" if use_runtime else "")
            tile = os.path.join(os.path.dirname(path), ".skin.jpg")
            _render(scene, tile)
            image = bpy.data.images.load(tile)
            pixels = np.empty(len(image.pixels), dtype=np.float32)
            image.pixels.foreach_get(pixels)
            tiles.append(pixels.reshape(image.size[1], image.size[0], 4))
            bpy.data.images.remove(image)
            os.remove(tile)
    finally:
        scene.render.resolution_x, scene.render.resolution_y = width, height
        label.hide_render = True
        show_skin(state, state.skins[0]["key"])
    if len(tiles) % 2:
        tiles.append(np.zeros_like(tiles[0]))
    rows = [np.concatenate(tiles[i:i + 2], axis=1) for i in range(0, len(tiles), 2)]
    sheet = np.concatenate(rows[::-1], axis=0)  # image rows go up from the bottom: first skins on top
    out = bpy.data.images.new("PF Skins", sheet.shape[1], sheet.shape[0])
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = path
    out.file_format = "JPEG"
    out.save()
    bpy.data.images.remove(out)


def _label(camera):
    """A text label in the top left corner of the camera view (used by the skin sheet)."""
    label = bpy.data.objects.get("PF Label")
    if label is None:
        curve = bpy.data.curves.new("PF Label", "FONT")
        curve.size = 0.07
        label = bpy.data.objects.new("PF Label", curve)
        bpy.data.collections[STAGE_COLLECTION].objects.link(label)
        material = bpy.data.materials.new("PF Label")
        material.use_nodes = True
        bsdf = material.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (0, 0, 0, 1)
        bsdf.inputs["Emission Color"].default_value = (1, 1, 1, 1)
        bsdf.inputs["Emission Strength"].default_value = 3.0
        curve.materials.append(material)
    label.parent = camera
    label.matrix_parent_inverse.identity()
    label.location = (-0.76, 0.36, -2.0)  # camera space: left, up, in front (42 mm lens, 16:9)
    label.rotation_euler = (0, 0, 0)
    label.hide_render = False
    label.hide_viewport = True
    return label


def _world_corners(holder):
    """Bounding box corners (world space) of a chopper and its attached modules."""
    corners = []
    for obj in [holder] + list(holder.children):
        if obj.type == "MESH":
            corners += [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    return corners


def render_previews(state, preview_dir, samples=32):
    scene = bpy.context.scene
    scene.cycles.samples = samples
    os.makedirs(preview_dir, exist_ok=True)
    camera = bpy.data.objects["PF Camera"]
    aim = bpy.data.objects["PF Aim"]
    shown, tiles = state.shown, state.tiles

    _set_render_visible(tiles, False)
    _set_render_visible(shown, True)
    _aim_lineup(camera, aim)
    _render(scene, os.path.join(preview_dir, "chassis_lineup.jpg"))

    # A close-up and a review sheet (four views) of every chassis.
    for index, spec in enumerate(state.specs):
        name = spec["name"].lower()
        size = max(1.0, spec["width"] / 2.0)
        aim.location = shown[index].location + Vector((0, 0, 0.6 * size))
        camera.location = shown[index].location + Vector((-11, -7, 1.8)) * size
        camera.data.lens = 42
        for other in shown:
            _set_render_visible([other], other is shown[index])
        _render(scene, os.path.join(preview_dir, f"{name}_closeup.jpg"))
        _review_sheet(scene, camera, aim, shown[index], os.path.join(preview_dir, f"{name}_views.jpg"))

    if state.skins:
        _skin_sheet(state, scene, camera, aim, os.path.join(preview_dir, "skins_sheet.jpg"))
        _skin_sheet(state, scene, camera, aim, os.path.join(preview_dir, "skins_runtime_sheet.jpg"), use_runtime=True)

    _set_render_visible(shown, False)
    _set_render_visible(tiles, True)
    rows = math.ceil(len(tiles) / MODULE_COLUMNS)
    aim.location = (0, MODULE_GRID_Y + (rows - 1) * 3.6 / 2, 0.8)
    camera.location = (-5, MODULE_GRID_Y + (rows - 1) * 3.6 / 2 - 19, 11)
    camera.data.lens = 40
    _render(scene, os.path.join(preview_dir, "modules_sheet.jpg"))

    # Leave the file as it opens best: everything visible, camera on the line-up.
    _set_render_visible(shown + tiles, True)
    _aim_lineup(camera, aim)


# -- .blend ---------------------------------------------------------------------------------------------------------

LOADER_NAME = "partforge_panel.py"
LOADER = '''# PartForge: adds the PartForge tab to the 3D view sidebar (N key) when this file opens.
# Blender asks to allow scripts in this file: allow them, or run this text once (Text > Run Script).
import os
import sys

import bpy

root = os.path.normpath(bpy.path.abspath("//.."))  # Tools/PartForge (this file lives in Tools/PartForge/blend)
if root not in sys.path:
    sys.path.insert(0, root)
from partforge import ui  # noqa: E402

ui.register()
'''


def save_blend(path):
    """The .blend: textures packed in (opens anywhere) and a loader text that registers the panel on open."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    folder = os.path.dirname(os.path.abspath(path))
    for image in bpy.data.images:
        source = bpy.path.abspath(image.filepath) if image.filepath else ""
        if source and os.path.exists(source) and image.packed_file is None:
            # Baked kit textures stay files (Assets/Chopper/Art/Kits/...), linked by relative path.
            image.filepath = bpy.path.relpath(source, start=folder)
        elif image.packed_file is None and (image.filepath or image.has_data):
            image.pack()
    text = bpy.data.texts.get(LOADER_NAME) or bpy.data.texts.new(LOADER_NAME)
    text.clear()
    text.write(LOADER)
    text.use_module = True
    bpy.context.preferences.filepaths.save_version = 0  # no .blend1 backups next to the file
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True)


# -- command line ---------------------------------------------------------------------------------------------------

def build(out_dir, preview_dir=None, kit=None, style_name="dystopia", render=True, samples=32, blend_path=None):
    style = styles.get(style_name)
    if bpy.app.background:
        bpy.ops.wm.read_factory_settings(use_empty=True)
    state = generate(style, kit)
    manifest = export(state, out_dir)
    if render and preview_dir:
        render_previews(state, preview_dir, samples)
    if blend_path:
        # Store the panel settings in the file so its sliders start at this style.
        from . import ui
        ui.register()
        ui.load_style(bpy.context.scene.partforge, style, style_name, state.kit)
        save_blend(blend_path)
    return manifest
