"""Builds every part, exports one FBX per part, writes the manifest, and renders previews."""
import json
import math
import os

import bpy
from mathutils import Vector

from . import kits, palette, parts, styles
from .geo import to_blender


def _reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def _export_fbx(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    for child in obj.children:
        child.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="STRIP", use_mesh_modifiers=True)


def _triangles(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def build(out_dir, preview_dir=None, kit=None, style_name="dystopia", render=True, samples=32):
    style = styles.get(style_name)
    kit = kit or style["kit"]
    parts.use_style(style)
    _reset()
    scene = bpy.context.scene
    collection = scene.collection
    models_dir = os.path.join(out_dir, "Models")
    os.makedirs(models_dir, exist_ok=True)
    maps = palette.write_maps(os.path.join(out_dir, "Textures"), kit, style.get("palette"))
    material = palette.make_material(kit, maps)

    built = {}
    manifest = {
        "kit": kit,
        "generator": "Tools/PartForge (Blender)",
        "style": style["name"],
        "units": "metres",
        "axes": "Unity space: +X right, +Y up, +Z forward. Socket rotation is identity (modules point +Z).",
        "material": {"name": material.name, "colour": os.path.basename(maps[0]), "emission": os.path.basename(maps[1]),
                     "metalSmooth": os.path.basename(maps[2])},
        "parts": [],
    }

    for part in [parts.chassis(spec, style) for spec in kits.CHASSIS] + parts.all_modules():
        obj = part.builder.to_object(part.name, collection)
        palette.apply_uvs(obj)
        obj.data.materials.append(material)

        sockets = []
        for slot, index, position in part.sockets:
            empty = bpy.data.objects.new(f"SOCKET_{slot}_{index}", None)
            empty.empty_display_type = "ARROWS"
            empty.empty_display_size = 0.4
            collection.objects.link(empty)
            empty.parent = obj
            empty.location = to_blender(position)
            sockets.append({"name": empty.name, "slot": slot, "index": index, "position": list(position)})

        file_name = f"{part.name}.fbx"
        _export_fbx(obj, os.path.join(models_dir, file_name))
        # Socket names repeat across parts; rename after export so the next part can use them.
        for child in obj.children:
            child.name = f"{part.name}:{child.name}"

        manifest["parts"].append({
            "name": part.name, "file": f"Models/{file_name}", "kind": part.kind, "slot": part.slot,
            "moduleIds": part.module_ids, "triangles": _triangles(obj), "sockets": sockets,
        })
        built[part.name] = (part, obj)

    with open(os.path.join(out_dir, f"{kit}.manifest.json"), "w") as handle:
        json.dump(manifest, handle, indent=2)

    if render and preview_dir:
        os.makedirs(preview_dir, exist_ok=True)
        _render_previews(scene, built, preview_dir, samples)
    return manifest


# -- previews -------------------------------------------------------------------------------------------------------

def _setup_render(scene, samples):
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 1600, 900
    scene.render.film_transparent = False
    scene.render.image_settings.file_format = "JPEG"
    scene.render.image_settings.quality = 88
    scene.view_settings.view_transform = "AgX"
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.012, 0.016, 0.02, 1.0)
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    scene.world = world

    def light(name, kind, energy, rotation, location=(0, 0, 0), size=None, colour=(1, 1, 1)):
        data = bpy.data.lights.new(name, kind)
        data.energy = energy
        data.color = colour
        if size is not None:
            data.size = size
        obj = bpy.data.objects.new(name, data)
        obj.location = location
        obj.rotation_euler = [math.radians(a) for a in rotation]
        scene.collection.objects.link(obj)

    light("Key", "SUN", 3.0, (50, 0, -35), colour=(1.0, 0.95, 0.88))
    light("Rim", "SUN", 2.5, (70, 0, 160), colour=(0.55, 0.9, 1.0))
    light("Fill", "SUN", 0.6, (110, 0, 60))

    ground_mesh = bpy.data.meshes.new("Ground")
    ground_mesh.from_pydata([(-80, -80, 0), (80, -80, 0), (80, 80, 0), (-80, 80, 0)], [], [(0, 1, 2, 3)])
    ground = bpy.data.objects.new("Ground", ground_mesh)
    ground_material = bpy.data.materials.new("Ground")
    ground_material.use_nodes = True
    bsdf = ground_material.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.006, 0.008, 0.008, 1)
    bsdf.inputs["Roughness"].default_value = 0.7
    ground_mesh.materials.append(ground_material)
    scene.collection.objects.link(ground)
    return ground


def _camera(scene, location, target, lens=40):
    data = bpy.data.cameras.new("Camera")
    data.lens = lens
    camera = bpy.data.objects.new("Camera", data)
    camera.location = location
    scene.collection.objects.link(camera)
    aim = bpy.data.objects.new("Aim", None)
    aim.location = target
    scene.collection.objects.link(aim)
    track = camera.constraints.new("TRACK_TO")
    track.target = aim
    track.track_axis = "TRACK_NEGATIVE_Z"
    track.up_axis = "UP_Y"
    scene.camera = camera
    return camera, aim


def _render(scene, path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def _render_previews(scene, built, preview_dir, samples):
    ground = _setup_render(scene, samples)
    for _part, obj in built.values():
        obj.hide_render = True

    # Chassis line-up with their preview loadouts snapped onto the sockets.
    shown = []
    spacing = 14.0
    for i, spec in enumerate(kits.CHASSIS):
        part, source = built[f"Chassis_{spec['name']}"]
        holder = bpy.data.objects.new(f"Show_{spec['name']}", source.data)
        scene.collection.objects.link(holder)
        # Unity x = -Blender x; line them up left to right as seen from the front-left camera.
        holder.location = Vector(((i - 1.5) * -spacing, 0, 2.6))
        holder.rotation_euler = (0, 0, math.radians(-35))
        shown.append(holder)
        loadout = kits.PREVIEW_LOADOUTS.get(spec["name"], {})
        for slot, index, position in part.sockets:
            names = loadout.get(slot, [])
            if index >= len(names):
                continue
            _module, module_source = built[names[index]]
            attached = bpy.data.objects.new(f"{holder.name}_{slot}{index}", module_source.data)
            scene.collection.objects.link(attached)
            attached.parent = holder
            attached.location = to_blender(position)

    camera, aim = _camera(scene, (-22, -52, 11), (0, 0, 2.2), lens=32)
    _render(scene, os.path.join(preview_dir, "chassis_lineup.jpg"))

    # Close-ups of the attack chassis (Gunship, Assault).
    for index, file_name in ((1, "gunship_closeup.jpg"), (2, "assault_closeup.jpg")):
        aim.location = shown[index].location + Vector((0, 0, 0.6))
        camera.location = shown[index].location + Vector((-11, -7, 1.8))
        camera.data.lens = 42
        for other in shown:
            other.hide_render = other is not shown[index]
            for child in other.children:
                child.hide_render = other.hide_render
        _render(scene, os.path.join(preview_dir, file_name))

    # Module sheet.
    for other in shown:
        other.hide_render = True
        for child in other.children:
            child.hide_render = True
    modules = [obj for part, obj in built.values() if part.kind == "module"]
    columns = 6
    for i, source in enumerate(modules):
        tile = bpy.data.objects.new(f"Tile_{source.name}", source.data)
        scene.collection.objects.link(tile)
        col, row = i % columns, i // columns
        big = source.name.startswith("Rotor")
        tile.location = Vector(((col - (columns - 1) / 2) * -3.2, row * 3.6, 1.0 if not big else 0.5))
        tile.rotation_euler = (0, 0, math.radians(-40))
        if big:
            tile.scale = (0.35, 0.35, 0.35)
    rows = math.ceil(len(modules) / columns)
    aim.location = (0, (rows - 1) * 3.6 / 2, 0.8)
    camera.location = (-5, (rows - 1) * 3.6 / 2 - 19, 11)
    camera.data.lens = 40
    _render(scene, os.path.join(preview_dir, "modules_sheet.jpg"))
