"""
Generation steps (generate.py runs them all):

    generate(style)        build every building (3 LODs, markers) into the open file + lay out the district
    export(state, folder)  one FBX per building (<Name>_LOD0..2 + MARKER_ empties), palette maps, manifest
    render_previews(...)   Cycles: district by day and by night, a street view at chopper height, LOD line-up,
                           a sheet with every building
    save_blend(path)       the .blend, palette images packed in
"""
import json
import math
import os

import bpy
from mathutils import Vector

from . import audit, buildings, choppers, kit, specs, styles
from .pf import geo, palette

EXPORT_COLLECTION = "CityForge: export"
DISTRICT_COLLECTION = "CityForge: district"
LODS_COLLECTION = "CityForge: LOD line-up"
STAGE_COLLECTION = "CityForge: stage"
STREET = 16.0                 # street width between plots in the district layout (m)
ROWS = (("slum",), ("block",), ("industrial",), ("tower",), ("mega",))  # front (camera side, +Z) to back
LOD_LINEUP = ("Tower_Spire", "Block_Corner", "Slum_Stack")
LOD_LINEUP_Z = -260.0
LOD_SCREEN_HEIGHTS = (0.30, 0.08, 0.012)  # suggested LOD Group transition heights (share of screen height)


class State:
    def __init__(self, kit_name, style, built, images, problems):
        self.kit, self.style, self.built, self.images, self.problems = kit_name, style, built, images, problems
        self.district, self.lineup, self.choppers = [], [], []
        self.placements = {}  # building name -> Unity position of its plot centre in the district


def _collection(scene, name):
    collection = bpy.data.collections.get(name)
    if collection is None:
        collection = bpy.data.collections.new(name)
    if collection.name not in scene.collection.children:
        scene.collection.children.link(collection)
    return collection


def clear():
    for name in (EXPORT_COLLECTION, DISTRICT_COLLECTION, LODS_COLLECTION):
        collection = bpy.data.collections.get(name)
        if collection is None:
            continue
        for obj in list(collection.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(collection)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)


def make_buildings(style, names=None):
    """The Building objects (meshes still in the builders), for the audit and for generate()."""
    form = dict(kit.CITY_FORM, edge_radius=style.get("edge_radius", 0.0))
    geo.configure({"form": form})
    chosen = [spec for spec in specs.BUILDINGS if not names or spec["name"] in names]
    return [buildings.build(kit.Building(spec, style)) for spec in chosen]


def generate(style, kit_name=None, names=None):
    kit_name = kit_name or style["kit"]
    clear()
    scene = bpy.context.scene
    export_collection = _collection(scene, EXPORT_COLLECTION)
    images = palette.make_images(kit_name, style.get("palette"))
    material = palette.make_material(kit_name, images)
    made = make_buildings(style, names)
    problems = {b.name: audit.check(b) for b in made}
    built = {}
    for b in made:
        triangles = [audit.triangles(level.bm) for level in b.lods]
        objects = []
        for level, builder in enumerate(b.lods):
            bevel = style.get("edge_radius", 0.0) if level == 0 else 0.0
            obj = builder.to_object(f"{b.name}_LOD{level}", export_collection, bevel_width=bevel)
            palette.apply_uvs(obj, palette_only=True)
            _strip_builder_layers(obj)
            obj.data.materials.clear()
            obj.data.materials.append(material)
            for polygon in obj.data.polygons:
                polygon.material_index = 0
            objects.append(obj)
        # Marker names repeat across buildings, so they carry the building name here; export() strips it.
        for kind, index, position, radius in b.markers:
            empty = bpy.data.objects.new(f"{b.name}:MARKER_{kind}_{index}", None)
            empty.empty_display_type = "CIRCLE" if radius > 0 else "ARROWS"
            empty.empty_display_size = max(radius, 0.5)
            export_collection.objects.link(empty)
            empty.parent = objects[0]
            empty.location = geo.to_blender(position)
            empty.rotation_euler = (math.radians(90), 0, 0) if radius > 0 else (0, 0, 0)  # circle lies flat
        built[b.name] = dict(building=b, objects=objects, triangles=triangles)
    state = State(kit_name, style, built, images, problems)
    _ensure_stage(scene)
    _district(scene, state)
    _lod_lineup(scene, state)
    _place_choppers(state)
    export_collection.hide_render = True
    export_collection.hide_viewport = True
    return state


def _strip_builder_layers(obj):
    """PartForge's builder adds cockpit-screen layers; buildings have no screens."""
    mesh = obj.data
    if "screen" in mesh.attributes:
        mesh.attributes.remove(mesh.attributes["screen"])
    layer = mesh.uv_layers.get("Screen")
    if layer is not None:
        mesh.uv_layers.remove(layer)


def _district(scene, state):
    """Every building's LOD0 on its plot, in rows by type with streets between (Unity space, then to Blender)."""
    collection = _collection(scene, DISTRICT_COLLECTION)
    z = 0.0
    rows = []
    for types in ROWS:
        row = [entry for entry in state.built.values() if entry["building"].type in types]
        if row:
            rows.append(row)
    placements = []
    for row in rows:
        depth = max(entry["building"].plot.d for entry in row)
        width = sum(entry["building"].plot.w for entry in row) + STREET * (len(row) - 1)
        x = -width / 2
        for entry in row:
            plot = entry["building"].plot
            placements.append((entry, (x + plot.w / 2, 0.0, z - depth / 2)))
            x += plot.w + STREET
        z -= depth + STREET
    for entry, position in placements:
        state.placements[entry["building"].name] = position
        source = entry["objects"][0]
        obj = bpy.data.objects.new(f"District_{entry['building'].name}", source.data)
        collection.objects.link(obj)
        obj.location = geo.to_blender(position)
        state.district.append(obj)


def _lod_lineup(scene, state):
    collection = _collection(scene, LODS_COLLECTION)
    names = [name for name in LOD_LINEUP if name in state.built]
    x = 0.0
    columns = []
    for name in names:
        entry = state.built[name]
        w = entry["building"].plot.w
        columns.append((entry, x))
        x += (w + 10.0) * kit.LOD_COUNT + 20.0
    offset = x / 2
    for entry, start in columns:
        w = entry["building"].plot.w
        for level, source in enumerate(entry["objects"]):
            obj = bpy.data.objects.new(f"Lods_{source.name}", source.data)
            collection.objects.link(obj)
            # Unity +X shows on the left of the preview camera, so LOD0 starts on the high-X side.
            obj.location = geo.to_blender((offset - start - level * (w + 10.0), 0.0, LOD_LINEUP_Z))
            state.lineup.append(obj)


def _place_choppers(state):
    """The game's choppers in the district for scale: one on each wall pad of the tallest megatower, one flying past
    it, one over the street, one at a slum. Only when the PartForge kit is in the project."""
    if not choppers.available():
        return
    library = choppers.Library()
    state.library = library

    def at(name, offset):
        base = state.placements.get(name)
        return None if base is None else tuple(a + b for a, b in zip(base, offset))

    mega = state.built.get("Mega_Arcology")
    if mega:
        for kind, index, position, radius in mega["building"].markers:
            if kind == "RoofPad":
                spot = at("Mega_Arcology", position)
                lift = library.ground_offset("Gunship")
                yaw = math.degrees(math.atan2(position[0], position[2]))
                state.choppers.append(library.chopper("Gunship", (spot[0], spot[1] + lift, spot[2]), yaw + 180.0,
                                                      name=f"Chopper_Pad{index}"))
        plot = mega["building"].plot
        flyby = at("Mega_Arcology", (-plot.w * 0.25, 140.0, plot.d / 2 + 22.0))
        state.choppers.append(library.chopper("Scout", flyby, -90.0, name="Chopper_Flyby"))
    street = at("Block_Corner", (0.0, 45.0, 30.0))
    if street:
        state.choppers.append(library.chopper("Lifter", street, 60.0, name="Chopper_Street"))
    slum = at("Slum_Tower", (12.0, 30.0, 14.0))
    if slum:
        state.choppers.append(library.chopper("Gunship", slum, -140.0, name="Chopper_Slum"))


# -- export -----------------------------------------------------------------------------------------------------------

def export(state, out_dir):
    models_dir = os.path.join(out_dir, "Models")
    texture_dir = os.path.join(out_dir, "Textures")
    os.makedirs(models_dir, exist_ok=True)
    palette_files = [os.path.basename(path) for path in palette.save_images(state.images, texture_dir)]
    manifest = {
        "kit": state.kit,
        "generator": "Tools/CityForge (Blender)",
        "style": state.style.get("name", ""),
        "units": "metres",
        "axes": "Unity space: +X right, +Y up, +Z forward (the building front). Origin = plot centre on the ground.",
        "uv": "UV0 and UV1 = palette swatch (cell centre of a 5 x 5 grid; point filtering).",
        "palette": {"material": palette.material_name(state.kit),
                    **{key: f"{os.path.basename(texture_dir)}/{name}"
                       for key, name in zip(("colour", "emission", "metalSmooth"), palette_files)}},
        "lod": {"levels": kit.LOD_COUNT, "naming": "<Name>_LOD0.._LOD2 meshes in one FBX (Unity makes the LOD Group)",
                "screenHeights": list(LOD_SCREEN_HEIGHTS)},
        "colliders": "Boxes: centre, size (Unity space); yaw = Unity euler Y in degrees.",
        "markers": "RoofPad = landing pad (spawn, Lifter objectives); Roof = clear circle on a flat roof (objective zones). "
                   "Position = surface point; radius = clear circle (checked by the audit).",
        "buildings": [],
    }
    export_collection = bpy.data.collections[EXPORT_COLLECTION]
    export_collection.hide_viewport = False
    try:
        for name, entry in state.built.items():
            b, objects = entry["building"], entry["objects"]
            prefix = f"{name}:"
            for child in objects[0].children:
                child.name = child.name[len(prefix):] if child.name.startswith(prefix) else child.name
            file_name = f"{name}.fbx"
            _export_fbx(objects, os.path.join(models_dir, file_name))
            for child in objects[0].children:
                child.name = prefix + child.name
            low, high = _unity_bounds(objects[0])
            manifest["buildings"].append({
                "name": name, "type": b.type, "seed": b.spec["seed"], "file": f"Models/{file_name}",
                "plot": [b.plot.w, b.plot.d],
                "bounds": {"min": [round(v, 3) for v in low], "max": [round(v, 3) for v in high]},
                "triangles": entry["triangles"],
                "colliders": [{"centre": _r(centre), "size": _r(size), "yaw": round(yaw, 3)}
                              for centre, size, yaw in b.colliders],
                "markers": [{"name": f"MARKER_{kind}_{index}", "kind": kind, "index": index, "position": _r(position),
                             "radius": round(radius, 3)} for kind, index, position, radius in b.markers],
                "audit": state.problems.get(name, []),
            })
    finally:
        export_collection.hide_viewport = True
    with open(os.path.join(out_dir, f"{state.kit}.manifest.json"), "w") as handle:
        json.dump(manifest, handle, indent=2)
    return manifest


def _r(values):
    return [round(v, 3) for v in values]


def _unity_bounds(obj):
    """Bounding box of a finished mesh in Unity space (geo.to_blender undone: x = -bx, y = bz, z = -by)."""
    points = [(-v.co.x, v.co.z, -v.co.y) for v in obj.data.vertices]
    return ([min(p[k] for p in points) for k in range(3)], [max(p[k] for p in points) for k in range(3)])


def _export_fbx(objects, path):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
        for child in obj.children:
            child.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="EDGE", use_triangles=True, add_leaf_bones=False, bake_anim=False, path_mode="STRIP",
        use_mesh_modifiers=True)


# -- stage and previews -----------------------------------------------------------------------------------------------

DAY_SKY = ((0.05, 0.05, 0.05, 1.0), (0.17, 0.165, 0.16, 1.0), (0.45, 0.46, 0.47, 1.0))
NIGHT_SKY = ((0.003, 0.003, 0.004, 1.0), (0.012, 0.014, 0.02, 1.0), (0.006, 0.008, 0.014, 1.0))


def _ensure_stage(scene):
    """A grey review studio like PartForge's, but city sized: sky ramp, sun, fill, floor with a 10 m grid."""
    stage = _collection(scene, STAGE_COLLECTION)
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
    world = bpy.data.worlds.get("CityForge World") or bpy.data.worlds.new("CityForge World")
    world.use_nodes = True
    nodes, links = world.node_tree.nodes, world.node_tree.links
    if nodes.get("CF Sky") is None:
        nodes.clear()
        coords = nodes.new("ShaderNodeTexCoord")
        split = nodes.new("ShaderNodeSeparateXYZ")
        to_ramp = nodes.new("ShaderNodeMapRange")
        to_ramp.inputs["From Min"].default_value = -1.0
        ramp = nodes.new("ShaderNodeValToRGB")
        ramp.name = "CF Sky"
        ramp.color_ramp.elements[0].position = 0.42
        ramp.color_ramp.elements[1].position = 0.75
        ramp.color_ramp.elements.new(0.5)
        background = nodes.new("ShaderNodeBackground")
        output = nodes.new("ShaderNodeOutputWorld")
        links.new(coords.outputs["Generated"], split.inputs[0])
        links.new(split.outputs["Z"], to_ramp.inputs["Value"])
        links.new(to_ramp.outputs["Result"], ramp.inputs["Fac"])
        links.new(ramp.outputs["Color"], background.inputs["Color"])
        links.new(background.outputs[0], output.inputs[0])
    scene.world = world

    def sun(name, energy, rotation, colour, angle, shadows=True):
        obj = bpy.data.objects.get(name)
        if obj is None:
            data = bpy.data.lights.new(name, "SUN")
            obj = bpy.data.objects.new(name, data)
            stage.objects.link(obj)
        obj.data.energy, obj.data.color = energy, colour
        obj.data.angle, obj.data.use_shadow = math.radians(angle), shadows
        obj.rotation_euler = [math.radians(a) for a in rotation]
        obj["day_energy"] = energy

    sun("CF Sun", 4.0, (50, 0, -35), (1.0, 0.94, 0.85), 2)
    sun("CF Fill", 1.0, (65, 0, 150), (0.8, 0.88, 1.0), 20, shadows=False)
    if bpy.data.objects.get("CF Ground") is None:
        size = 2000.0
        mesh = bpy.data.meshes.new("CF Ground")
        mesh.from_pydata([(-size, -size, 0), (size, -size, 0), (size, size, 0), (-size, size, 0)], [], [(0, 1, 2, 3)])
        mesh.materials.append(_ground_material())
        stage.objects.link(bpy.data.objects.new("CF Ground", mesh))
    if bpy.data.objects.get("CF Camera") is None:
        camera = bpy.data.objects.new("CF Camera", bpy.data.cameras.new("CF Camera"))
        stage.objects.link(camera)
        aim = bpy.data.objects.new("CF Aim", None)
        stage.objects.link(aim)
        track = camera.constraints.new("TRACK_TO")
        track.target = aim
        track.track_axis = "TRACK_NEGATIVE_Z"
        track.up_axis = "UP_Y"
        camera.data.clip_end = 5000.0
    scene.camera = bpy.data.objects["CF Camera"]
    set_time(scene, night=False)


def _ground_material():
    """Light asphalt grey with a faint 10 m grid (scale) that fades into the horizon: a clear, readable ground."""
    material = bpy.data.materials.get("CF Ground") or bpy.data.materials.new("CF Ground")
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    coords = nodes.new("ShaderNodeTexCoord")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.inputs["Roughness"].default_value = 0.8
    scale = nodes.new("ShaderNodeVectorMath")
    scale.operation = "SCALE"
    scale.inputs["Scale"].default_value = 0.1
    grid = nodes.new("ShaderNodeTexBrick")
    grid.inputs["Scale"].default_value = 1.0
    grid.inputs["Mortar Size"].default_value = 0.01
    grid.inputs["Color1"].default_value = (0.23, 0.22, 0.2, 1)
    grid.inputs["Color2"].default_value = (0.22, 0.21, 0.19, 1)
    grid.inputs["Mortar"].default_value = (0.15, 0.15, 0.14, 1)
    grid.offset = 0.0
    grid.squash = 1.0
    links.new(coords.outputs["Object"], scale.inputs[0])
    links.new(scale.outputs[0], grid.inputs["Vector"])
    links.new(grid.outputs["Color"], bsdf.inputs["Base Color"])
    output = nodes.new("ShaderNodeOutputMaterial")
    links.new(bsdf.outputs[0], output.inputs["Surface"])
    return material


def set_time(scene, night):
    ramp = scene.world.node_tree.nodes["CF Sky"].color_ramp
    sky = NIGHT_SKY if night else DAY_SKY
    for element, colour in zip(sorted(ramp.elements, key=lambda e: e.position), sky):
        element.color = colour
    for name in ("CF Sun", "CF Fill"):
        obj = bpy.data.objects[name]
        obj.data.energy = obj["day_energy"] * (0.02 if night else 1.0)
        obj.data.color = (0.6, 0.7, 1.0) if night else (1.0, 0.94, 0.85) if name == "CF Sun" else (0.8, 0.88, 1.0)
    scene.view_settings.exposure = 0.0 if night else 0.3


def _render(scene, path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def _corners(objects):
    bpy.context.view_layer.update()  # new or moved objects: refresh matrix_world
    corners = []
    for obj in objects:
        corners += [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    return corners


def _frame(camera, aim, objects, direction, lens=35.0, margin=1.1):
    """Aims at the middle of the objects and backs off along `direction` until their bounding sphere fits the
    picture height (16:9, 36 mm sensor width)."""
    corners = _corners(objects)
    centre = sum(corners, Vector()) / len(corners)
    radius = max((c - centre).length for c in corners)
    aim.location = centre
    camera.data.lens = lens
    half_height = math.atan(18.0 / lens * 9.0 / 16.0)
    camera.location = centre + Vector(direction).normalized() * radius * margin / math.sin(half_height)


def _visible(objects, visible):
    for obj in objects:
        obj.hide_render = not visible


def render_previews(state, preview_dir, samples=32, sheet=True):
    import numpy as np

    scene = bpy.context.scene
    scene.cycles.samples = samples
    os.makedirs(preview_dir, exist_ok=True)
    camera, aim = bpy.data.objects["CF Camera"], bpy.data.objects["CF Aim"]
    district, lineup = state.district, state.lineup

    # District by day and by night (same camera: a chopper coming in high from the front left).
    _visible(lineup, False)
    _visible(district, True)
    _frame(camera, aim, district, (-0.5, -1.0, 0.3), lens=35.0, margin=0.5)
    set_time(scene, night=False)
    _render(scene, os.path.join(preview_dir, "district_day.jpg"))
    set_time(scene, night=True)
    _render(scene, os.path.join(preview_dir, "district_night.jpg"))
    # Street view at chopper height: low over the street between the block row and the industrial row.
    street = [obj for obj in district if obj.name.startswith(("District_Block", "District_Ind", "District_Tower"))]
    corners = _corners(street)
    low_y = min(c.y for c in _corners([o for o in district if o.name.startswith("District_Block")]))
    aim.location = Vector((min(c.x for c in corners) + 40.0, low_y - STREET / 2 + 4.0, 22.0))
    camera.location = Vector((max(c.x for c in corners) + 30.0, low_y - STREET / 2 - 6.0, 26.0))
    camera.data.lens = 28.0
    _render(scene, os.path.join(preview_dir, "street_night.jpg"))
    set_time(scene, night=False)
    _render(scene, os.path.join(preview_dir, "street_day.jpg"))
    _scale_shots(state, scene, camera, aim, preview_dir)
    chopper_objects = state.library.objects() if getattr(state, "library", None) else []
    _visible(chopper_objects, False)

    # LOD line-up: LOD0, LOD1, LOD2 of a few buildings, side by side.
    _visible(district, False)
    _visible(lineup, True)
    _frame(camera, aim, lineup, (-0.2, -1.0, 0.3), lens=50.0, margin=0.6)
    _render(scene, os.path.join(preview_dir, "lods.jpg"))
    _visible(lineup, False)

    if sheet:
        _building_sheet(state, scene, camera, aim, os.path.join(preview_dir, "buildings_sheet.jpg"), np)
    _visible(district + lineup + chopper_objects, True)
    _frame(camera, aim, district, (-0.55, -1.0, 0.6), lens=35.0, margin=0.72)


def _scale_shots(state, scene, camera, aim, preview_dir):
    """Choppers against the megatower: one on a wall pad (seen from the air beside it), one flying past (seen from
    below and behind, the tower going up out of the picture). By day and by night."""
    by_name = {obj.name: obj for obj in state.choppers}
    shots = []
    pad = by_name.get("Chopper_Pad0")
    if pad is not None:
        p = Vector(pad.location)
        out = Vector((p.x - geo.to_blender(state.placements["Mega_Arcology"]).x,
                      p.y - geo.to_blender(state.placements["Mega_Arcology"]).y, 0.0)).normalized()
        side = Vector((-out.y, out.x, 0.0))
        shots.append(("scale_pad", p + out * 30.0 + side * 16.0 + Vector((0, 0, 4.0)),
                      p + Vector((0, 0, 9.0)) - out * 4.0, 22.0))
    flyby = by_name.get("Chopper_Flyby")
    if flyby is not None:
        p = Vector(flyby.location)
        tower = geo.to_blender(state.placements["Mega_Arcology"])
        out = Vector((p.x - tower.x, p.y - tower.y, 0.0)).normalized()
        side = Vector((-out.y, out.x, 0.0))
        shots.append(("scale_flyby", p + out * 26.0 + side * 9.0 + Vector((0, 0, -5.0)),
                      p - out * 10.0 + Vector((0, 0, 16.0)), 20.0))
    if "Mega_Arcology" in state.placements and "Slum_Low" in state.placements:
        # Skyline from chopper height at the edge of the district, looking past the low blocks at the megatowers.
        start = geo.to_blender(state.placements["Slum_Low"])
        tower = geo.to_blender(state.placements["Mega_Arcology"])
        shots.append(("skyline", Vector((start.x - 40.0, start.y - 70.0, 38.0)),
                      Vector((tower.x, tower.y, 170.0)), 22.0))
    for name, camera_at, aim_at, lens in shots:
        camera.location, aim.location, camera.data.lens = camera_at, aim_at, lens
        set_time(scene, night=False)
        _render(scene, os.path.join(preview_dir, f"{name}_day.jpg"))
        set_time(scene, night=True)
        _render(scene, os.path.join(preview_dir, f"{name}_night.jpg"))
    set_time(scene, night=False)


def _building_sheet(state, scene, camera, aim, path, np):
    """Every building alone (front left, by day), four per row, half size tiles."""
    width, height = scene.render.resolution_x, scene.render.resolution_y
    scene.render.resolution_x, scene.render.resolution_y = width // 2, height // 2
    tiles = []
    try:
        for obj in state.district:
            for other in state.district:
                other.hide_render = other is not obj
            _frame(camera, aim, [obj], (-0.75, -1.0, 0.45), lens=40.0, margin=0.95)
            tile = os.path.join(os.path.dirname(path), ".tile.jpg")
            _render(scene, tile)
            image = bpy.data.images.load(tile)
            pixels = np.empty(len(image.pixels), dtype=np.float32)
            image.pixels.foreach_get(pixels)
            tiles.append(pixels.reshape(image.size[1], image.size[0], 4))
            bpy.data.images.remove(image)
            os.remove(tile)
    finally:
        scene.render.resolution_x, scene.render.resolution_y = width, height
        for obj in state.district:
            obj.hide_render = True
    columns = 4
    while len(tiles) % columns:
        tiles.append(np.zeros_like(tiles[0]))
    rows = [np.concatenate(tiles[i:i + columns], axis=1) for i in range(0, len(tiles), columns)]
    sheet = np.concatenate(rows[::-1], axis=0)
    out = bpy.data.images.new("CF Sheet", sheet.shape[1], sheet.shape[0])
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = path
    out.file_format = "JPEG"
    out.save()
    bpy.data.images.remove(out)


# -- .blend -----------------------------------------------------------------------------------------------------------

def save_blend(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for image in bpy.data.images:
        if image.packed_file is None and image.has_data:
            image.pack()
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True)


# -- command line -----------------------------------------------------------------------------------------------------

def build(out_dir, preview_dir=None, kit_name=None, style_name="dystopia", render=True, samples=32, blend_path=None,
          names=None, sheet=True):
    style = styles.get(style_name)
    if bpy.app.background:
        bpy.ops.wm.read_factory_settings(use_empty=True)
    state = generate(style, kit_name, names)
    manifest = export(state, out_dir)
    if render and preview_dir:
        render_previews(state, preview_dir, samples, sheet=sheet)
    if blend_path:
        save_blend(blend_path)
    return state, manifest
