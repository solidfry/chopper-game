"""
Generation steps, used by the command line (generate.py runs them all) and by the Blender panel (ui.py, one per button):

    generate(style)        build every part into the open file + lay out the showcase (choppers with loadouts, module grid)
    export(state, folder)  palette PNGs, one FBX per part, manifest
    render_previews(...)   Cycles line-up, close-ups and module sheet
    save_blend(path)       the .blend, textures packed in, with the panel loader
"""
import json
import math
import os

import bpy
from mathutils import Vector

from . import kits, palette, parts, styles
from .geo import to_blender

EXPORT_COLLECTION = "PartForge: export parts"
CHOPPERS_COLLECTION = "PartForge: choppers"
MODULES_COLLECTION = "PartForge: modules"
STAGE_COLLECTION = "PartForge: stage"
MODULE_COLUMNS = 6
MODULE_GRID_Y = 30.0
CHASSIS_SPACING = 14.0


class State:
    """What generate() made: the export parts (by name) and the showcase objects."""

    def __init__(self, kit, style, specs, built, images, shown, tiles):
        self.kit, self.style, self.specs = kit, style, specs
        self.built, self.images, self.shown, self.tiles = built, images, shown, tiles


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
        obj = part.builder.to_object(part.name, export_collection)
        palette.apply_uvs(obj)
        obj.data.materials.append(material)
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
    """Palette PNGs, one FBX per part (sockets as SOCKET_<Slot>_<Index> children) and the manifest."""
    models_dir = os.path.join(out_dir, "Models")
    os.makedirs(models_dir, exist_ok=True)
    paths = palette.save_images(state.images, os.path.join(out_dir, "Textures"))
    material_name = f"{state.kit}_Kit"
    manifest = {
        "kit": state.kit,
        "generator": "Tools/PartForge (Blender)",
        "style": state.style.get("name", ""),
        "units": "metres",
        "axes": "Unity space: +X right, +Y up, +Z forward. Socket rotation is identity (modules point +Z).",
        "material": {"name": material_name, "colour": os.path.basename(paths[0]), "emission": os.path.basename(paths[1]),
                     "metalSmooth": os.path.basename(paths[2])},
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
                "moduleIds": part.module_ids, "triangles": _triangles(obj),
                "sockets": [{"name": f"SOCKET_{slot}_{index}", "slot": slot, "index": index, "position": list(position),
                             "scale": scale}
                            for slot, index, position, scale in part.sockets],
            })
    finally:
        export_collection.hide_viewport = True

    with open(os.path.join(out_dir, f"{state.kit}.manifest.json"), "w") as handle:
        json.dump(manifest, handle, indent=2)
    return manifest


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

def _ensure_stage(scene):
    """World, render settings, lights, ground and camera, made once per file (kept across rebuilds)."""
    stage = _collection(scene, STAGE_COLLECTION)
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 88
    scene.view_settings.view_transform = "AgX"
    if scene.world is None or scene.world.name != "PartForge World":
        world = bpy.data.worlds.get("PartForge World") or bpy.data.worlds.new("PartForge World")
        world.use_nodes = True
        world.node_tree.nodes["Background"].inputs[0].default_value = (0.012, 0.016, 0.02, 1.0)
        world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
        scene.world = world

    def light(name, energy, rotation, colour):
        if bpy.data.objects.get(name) is not None:
            return
        data = bpy.data.lights.new(name, "SUN")
        data.energy = energy
        data.color = colour
        obj = bpy.data.objects.new(name, data)
        obj.rotation_euler = [math.radians(a) for a in rotation]
        stage.objects.link(obj)

    light("PF Key", 3.0, (50, 0, -35), (1.0, 0.95, 0.88))
    light("PF Rim", 2.5, (70, 0, 160), (0.55, 0.9, 1.0))
    light("PF Fill", 0.6, (110, 0, 60), (1, 1, 1))

    if bpy.data.objects.get("PF Ground") is None:
        mesh = bpy.data.meshes.new("PF Ground")
        mesh.from_pydata([(-80, -80, 0), (80, -80, 0), (80, 80, 0), (-80, 80, 0)], [], [(0, 1, 2, 3)])
        material = bpy.data.materials.new("PF Ground")
        material.use_nodes = True
        bsdf = material.node_tree.nodes["Principled BSDF"]
        bsdf.inputs["Base Color"].default_value = (0.006, 0.008, 0.008, 1)
        bsdf.inputs["Roughness"].default_value = 0.7
        mesh.materials.append(material)
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

    # A close-up of every chassis.
    for index, spec in enumerate(state.specs):
        file_name = f"{spec['name'].lower()}_closeup.jpg"
        size = max(1.0, spec["width"] / 2.0)
        aim.location = shown[index].location + Vector((0, 0, 0.6 * size))
        camera.location = shown[index].location + Vector((-11, -7, 1.8)) * size
        camera.data.lens = 42
        for other in shown:
            _set_render_visible([other], other is shown[index])
        _render(scene, os.path.join(preview_dir, file_name))

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
    for image in bpy.data.images:
        if image.packed_file is None and (image.filepath or image.has_data):
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
