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

from . import audit, buildings, choppers, cranes, kit, layout, roads, shanties, specs, styles, terrain, terrain_biomes, traffic, wear
from .pf import geo, palette

EXPORT_COLLECTION = "CityForge: export"
DISTRICT_COLLECTION = "CityForge: district"
LODS_COLLECTION = "CityForge: LOD line-up"
STAGE_COLLECTION = "CityForge: stage"
STREET = 16.0                 # street width between plots in the district layout (m)
CHANNEL_GAP = 84.0            # gap left after the CHANNEL_AFTER row for the storm-drain channel (56 m + roads)
CHANNEL_AFTER = "industrial"
TERRAIN_EXPORT = "CityForge: terrain export"
TERRAIN_COLLECTION = "CityForge: terrain"
ROWS = (("slum",), ("block",), ("industrial",), ("tower",), ("twist", "halo", "orb"), ("mega", "twins"))  # front (camera side, +Z) to back
LOD_LINEUP = ("Tower_Spire", "Block_Corner", "Slum_Stack")
LOD_LINEUP_Z = -260.0
LOD_SCREEN_HEIGHTS = (0.30, 0.08, 0.012)  # suggested LOD Group transition heights (share of screen height)


class State:
    def __init__(self, kit_name, style, built, images, problems):
        self.kit, self.style, self.built, self.images, self.problems = kit_name, style, built, images, problems
        self.district, self.lineup, self.choppers = [], [], []
        self.placements = {}  # building name -> Unity position of its plot centre in the district
        self.terrain, self.terrain_chunks, self.terrain_shown = None, [], []
        self.surface = None
        self.centre, self.channel_z = (0.0, 0.0, 0.0), None


def _collection(scene, name):
    collection = bpy.data.collections.get(name)
    if collection is None:
        collection = bpy.data.collections.new(name)
    if collection.name not in scene.collection.children:
        scene.collection.children.link(collection)
    return collection


def clear():
    for name in (EXPORT_COLLECTION, DISTRICT_COLLECTION, LODS_COLLECTION, TERRAIN_EXPORT, TERRAIN_COLLECTION,
                 choppers.COLLECTION):
        collection = bpy.data.collections.get(name)
        if collection is None:
            continue
        for obj in list(collection.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(collection)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)


def all_specs(style, with_variants=True):
    """The style's base buildings (specs.for_style) and, for a city, their variants (layout.variant_specs)."""
    unique = set(layout.settings(style)["unique"])
    # One-off buildings (megatowers, landmarks) only when the style's layout places them.
    base = [spec for spec in specs.for_style(style["name"])
            if spec["type"] in ("tower", "block", "slum", "industrial") or spec["name"] in unique]
    return base + (layout.variant_specs(base, layout.settings(style)["variants"]) if with_variants else [])


def make_buildings(style, names=None, with_variants=False):
    """The Building objects (meshes still in the builders), for the audit and for generate()."""
    form = dict(kit.CITY_FORM, edge_radius=style.get("edge_radius", 0.0))
    geo.configure({"form": form})
    chosen = [spec for spec in all_specs(style, with_variants) if not names or spec["name"] in names]
    return [buildings.build(kit.Building(spec, style)) for spec in chosen]


def generate(style, kit_name=None, names=None, with_terrain=True):
    kit_name = kit_name or style["kit"]
    clear()
    scene = bpy.context.scene
    export_collection = _collection(scene, EXPORT_COLLECTION)
    images = palette.make_images(kit_name, style.get("palette"))
    kit.mark_windows(images[2])                 # the MetalSmooth map: G = 1 on window swatches (fake rooms)
    material = palette.make_material(kit_name, images)
    biome = terrain_biomes.BIOMES[style.get("biome", "desert")]
    wear.add_preview_nodes(material, dust=biome["cover"] or biome["ground"][0], wear=style.get("wear", 1.0),
                           rain=biome["wet"])
    city = None
    if with_terrain and not names:
        made = make_buildings(style, with_variants=True)
        city = layout.Layout(all_specs(style, True), layout.settings(style))
    else:
        made = make_buildings(style, names)
    problems = {b.name: audit.check(b) for b in made}
    built = {b.name: _building_objects(b, export_collection, material, style) for b in made}
    state = State(kit_name, style, built, images, problems)
    state.layout = city
    _ensure_stage(scene)
    if city is not None:
        _city(scene, state, city)
    else:
        _district(scene, state)
    _lod_lineup(scene, state)
    if with_terrain:
        _make_terrain(scene, state, material)
    if city is not None:
        _make_roads(scene, state, city, export_collection, material)
    _place_choppers(state)
    export_collection.hide_render = True
    export_collection.hide_viewport = True
    return state


def _building_objects(b, export_collection, material, style):
    """A Building's LOD meshes (palette material) and marker empties, in the export collection."""
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
    for obj in objects:
        if b.type != "props":
            wear.bake(obj)
        else:
            wear.clear(obj)   # zero masks: Unity reads a missing vertex colour as white (full grime)
    # Marker names repeat across buildings, so they carry the building name here; export() strips it.
    for kind, index, position, radius in b.markers:
        empty = bpy.data.objects.new(f"{b.name}:MARKER_{kind}_{index}", None)
        empty.empty_display_type = "CIRCLE" if radius > 0 else "ARROWS"
        empty.empty_display_size = max(radius, 0.5)
        export_collection.objects.link(empty)
        empty.parent = objects[0]
        empty.location = geo.to_blender(position)
        empty.rotation_euler = (math.radians(90), 0, 0) if radius > 0 else (0, 0, 0)  # circle lies flat
    return dict(building=b, objects=objects, triangles=triangles)


def _make_roads(scene, state, city, export_collection, material):
    """Street surfaces, bridges, roads out of the city, tunnel portals, the freeway and the sky roads (roads.py):
    each a Building, placed once at the city origin."""
    tops = []
    for (name, _position, _yaw), (_n, shape) in zip(city.instances, city.footprints):
        tops.append((shape, _unity_bounds(state.built[name]["objects"][0])[1][1]))
    state.building_tops = tops
    made = [roads.city_roads(city, state.terrain, state.style), roads.freeway(city, state.terrain, state.style),
            roads.sky_roads(city, state.terrain, state.style, tops), roads.utilities(city, state.terrain, state.style, tops, _wall_hit(state, city)),
            shanties.shanties(city, state.terrain, state.style)]
    made = [b for b in made if b is not None]
    collection = _collection(scene, DISTRICT_COLLECTION)
    _add_parts(state, made, export_collection, material, collection)
    # Tower cranes last: they need the finished district to stay clear of it (cranes.py).
    if state.terrain is not None:
        _add_parts(state, [cranes.cranes(city, state, _clearance(state))], export_collection, material, collection)
    state.problems["City_Roads"] = state.problems["City_Roads"] + city.audit()
    # Traffic vehicles (traffic.py) and the crane jib: exported like buildings, not placed (Unity moves them).
    for b in traffic.models(state.style) + [cranes.jib_model(state.style)]:
        state.problems[b.name] = audit.check(b)
        state.built[b.name] = _building_objects(b, export_collection, material, state.style)


def _add_parts(state, made, export_collection, material, collection):
    """City parts placed once at the origin: audit, export objects, a district copy."""
    for b in made:
        state.problems[b.name] = audit.check(b)
        state.built[b.name] = _building_objects(b, export_collection, material, state.style)
        if getattr(b, "face_up_at", None) is not None:
            _faces_up(state.built[b.name]["objects"], b.face_up_at)
        obj = bpy.data.objects.new(f"District_{b.name}", state.built[b.name]["objects"][0].data)
        collection.objects.link(obj)
        state.district.append(obj)
        state.placements.setdefault(b.name, (0.0, 0.0, 0.0))


def _wall_hit(state, city):
    """hit(k, origin, direction, distance): where a ray (Unity, city space) first meets the LOD0 mesh of layout
    instance k (in its place and yaw), as (x, y, z), or None. Uses the finished Blender mesh (one BVH per building)."""
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree

    trees = {}

    def tree(name):
        if name not in trees:
            mesh = state.built[name]["objects"][0].data
            trees[name] = BVHTree.FromPolygons([v.co.copy() for v in mesh.vertices],
                                               [tuple(p.vertices) for p in mesh.polygons])
        return trees[name]

    def hit(k, origin, direction, distance):
        name, (px, _py, pz), yaw = city.instances[k]
        c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
        dx, dz = origin[0] - px, origin[2] - pz
        lx, ly, lz = dx * c - dz * s_, origin[1], dx * s_ + dz * c                      # Unity, building space
        vx, vy, vz = direction[0] * c - direction[2] * s_, direction[1], direction[0] * s_ + direction[2] * c
        point, _normal, _index, _d = tree(name).ray_cast(Vector((-lx, -lz, ly)), Vector((-vx, -vz, vy)), distance)
        if point is None:
            return None
        ux, uy, uz = -point.x, point.z, -point.y                                         # Blender -> Unity
        return (px + ux * c + uz * s_, uy, pz - ux * s_ + uz * c)
    return hit


def _clearance(state):
    """distance(points, most): for Unity points (city space), the distance to the nearest placed mesh (buildings and
    city parts, as placed in the district), at most `most`. One BVH of every district mesh in world space."""
    import numpy as np
    from mathutils import Vector
    from mathutils.bvhtree import BVHTree

    bpy.context.view_layer.update()
    vertices, polygons = [], []
    for obj in state.district:
        matrix = obj.matrix_world
        base = len(vertices)
        vertices.extend(matrix @ v.co for v in obj.data.vertices)
        polygons.extend(tuple(base + i for i in p.vertices) for p in obj.data.polygons)
    tree = BVHTree.FromPolygons(vertices, polygons) if polygons else None

    def distance(points, most):
        out = np.full(len(points), float(most))
        if tree is None:
            return out
        for k, (x, y, z) in enumerate(points):
            hit = tree.find_nearest(Vector((-x, -z, y)), most)
            if hit[0] is not None:
                out[k] = hit[3]
        return out
    return distance


def _faces_up(objects, height):
    """Flat faces at a height (Unity y = Blender z) that point down are turned over (open surfaces)."""
    import bmesh

    for obj in objects:
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        flip = [f for f in bm.faces if abs(f.calc_center_median().z - height) < 0.005 and f.normal.z < 0.0]
        if flip:
            bmesh.ops.reverse_faces(bm, faces=flip)
        bm.to_mesh(obj.data)
        bm.free()


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
        if row[0]["building"].type == CHANNEL_AFTER:
            back = z + STREET
            state.channel_z = back - CHANNEL_GAP / 2
            z = back - CHANNEL_GAP
    if placements:
        rects = [(p[0] - e["building"].plot.w / 2, p[0] + e["building"].plot.w / 2,
                  p[2] - e["building"].plot.d / 2, p[2] + e["building"].plot.d / 2) for e, p in placements]
        x0, x1 = min(r[0] for r in rects), max(r[1] for r in rects)
        z0, z1 = min(r[2] for r in rects), max(r[3] for r in rects)
        state.centre = ((x0 + x1) / 2, 0.0, (z0 + z1) / 2)
        state.city_rect = (x0, x1, z0, z1)
        state.plot_rects = {e["building"].name: r for (e, _p), r in zip(placements, rects)}
    for entry, position in placements:
        state.placements[entry["building"].name] = position
        source = entry["objects"][0]
        obj = bpy.data.objects.new(f"District_{entry['building'].name}", source.data)
        collection.objects.link(obj)
        obj.location = geo.to_blender(position)
        state.district.append(obj)


def _city(scene, state, city):
    """The generated city (layout.py): an instance of a building's LOD0 per plot (shared mesh), turned by its yaw,
    and the bridges over the channel. Origin = the city centre = the terrain origin."""
    collection = _collection(scene, DISTRICT_COLLECTION)
    state.centre = (0.0, 0.0, 0.0)
    state.city_rect = city.city_rect()
    state.channel_z = city.channel_z
    for k, (name, position, yaw) in enumerate(city.instances):
        obj = bpy.data.objects.new(f"District_{name}_{k:03d}", state.built[name]["objects"][0].data)
        collection.objects.link(obj)
        obj.location = geo.to_blender(position)
        obj.rotation_euler = (0.0, 0.0, math.radians(-yaw))
        state.district.append(obj)
        state.placements.setdefault(name, position)


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


def _make_terrain(scene, state, material):
    """Terrain round the district (terrain.py): numpy field, chunk meshes (export copies at the origin = the city
    centre, preview copies moved onto the district)."""
    cx, _cy, cz = state.centre
    x0, x1, z0, z1 = state.city_rect
    if state.layout is not None:
        plots, exterior = state.layout.plots(), state.layout.exterior()
    else:
        plots = [(name, (r[0] - cx, r[1] - cx, r[2] - cz, r[3] - cz)) for name, r in state.plot_rects.items()]
        exterior = None
    channel_z = (state.channel_z if state.channel_z is not None else z0 - 60.0) - cz
    t = terrain.Terrain(state.style, (x0 - cx, x1 - cx, z0 - cz, z1 - cz), channel_z, plots, exterior)
    t.make_markers()
    state.terrain = t
    state.terrain_problems = t.audit()
    export_collection = _collection(scene, TERRAIN_EXPORT)
    # Ground material: the biome shader's preview (terrain_biomes.py); the game uses TerrainBiome.shadergraph.
    state.biome = state.style.get("biome", "desert")
    state.surface = terrain_biomes.surface_images(t, state.kit)
    ground = terrain_biomes.make_material(state.biome, f"{state.kit}_Terrain", state.surface)
    state.terrain_chunks = terrain.make_chunks(t, export_collection, ground)
    state.backdrop = [terrain.make_backdrop(t, export_collection, ground),
                      terrain.make_skyline(t, export_collection, material)]
    shown = _collection(scene, TERRAIN_COLLECTION)
    for source in [objects[0] for _name, objects, _factors in state.terrain_chunks] + state.backdrop:
        obj = bpy.data.objects.new(f"Show_{source.name}", source.data)
        shown.objects.link(obj)
        obj.location = geo.to_blender(state.centre)
        state.terrain_shown.append(obj)
    export_collection.hide_render = True
    export_collection.hide_viewport = True


def _world(state, point):
    """Terrain point (relative to the city centre) -> district (Unity) position."""
    return (point[0] + state.centre[0], point[1], point[2] + state.centre[2])


def _channel_spot(state):
    """x in the storm drain halfway between the two bridges nearest the city centre."""
    if state.layout is None or len(state.layout.bridges) < 2:
        t = state.terrain
        return (t.city[0] + t.city[1]) / 2 + 72.0
    xs = sorted(x for x, _z, _yaw, _w in state.layout.bridges)
    pairs = sorted(zip(xs, xs[1:]), key=lambda p: abs((p[0] + p[1]) / 2))
    return (pairs[0][0] + pairs[0][1]) / 2


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

    mega = state.built.get("Mega_Arcology") if "Mega_Arcology" in state.placements else None
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
    twins = state.built.get("Mega_Twins") if "Mega_Twins" in state.placements else None
    if twins:
        for kind, index, position, radius in twins["building"].markers:
            if kind == "RoofPad":
                spot = at("Mega_Twins", position)
                lift = library.ground_offset("Lifter")
                state.choppers.append(library.chopper("Lifter", (spot[0], spot[1] + lift, spot[2]), 90.0,
                                                      name="Chopper_Deck"))
        bridges = [height for kind, height, _w in twins["building"].spec["bridges"] if kind == "glass"]
        if bridges:
            state.choppers.append(library.chopper("Gunship", at("Mega_Twins", (2.0, bridges[-1] - 9.0, 6.0)), 15.0,
                                                  name="Chopper_Bridge"))
    orb = state.built.get("Landmark_Orb") if "Landmark_Orb" in state.placements else None
    if orb:
        for kind, index, position, radius in orb["building"].markers[:1]:
            spot = at("Landmark_Orb", position)
            state.choppers.append(library.chopper("Scout", (spot[0], spot[1] + library.ground_offset("Scout"),
                                                            spot[2]), 135.0, name="Chopper_Orb"))
    t = state.terrain
    if t is not None:
        # In the canyon, low, flying in towards the city.
        k = len(t.canyon) // 3
        (ax, az), (bx, bz) = t.canyon[k], t.canyon[k + 1]
        floor = float(t.sample(ax, az))
        yaw = math.degrees(math.atan2(-(bx - ax), -(bz - az)))
        state.choppers.append(library.chopper("Gunship", _world(state, (ax, floor + 22.0, az)), yaw,
                                              name="Chopper_Canyon"))
        # In the storm drain, below street level, through the city (between two bridges).
        cx = _channel_spot(state)
        state.choppers.append(library.chopper("Scout", _world(state, (cx, -1.0, t.channel_z)), 90.0,
                                              name="Chopper_Channel"))
        # Landed on the mesa top nearest the city.
        tops = sorted(t.markers, key=lambda m: math.hypot(m[2][0], m[2][2]))
        if tops:
            _kind, _i, (mx, my, mz), _r = tops[0]
            state.choppers.append(library.chopper("Lifter", _world(state, (mx, my + library.ground_offset("Lifter"),
                                                                           mz)), math.degrees(math.atan2(-mx, -mz)),
                                                  name="Chopper_Mesa"))
    if state.layout is not None and "City_SkyRoads" in state.built:
        city = state.layout
        state.choppers.append(library.chopper("Gunship", (-250.0, 9.0, city.channel_z), 90.0, name="Chopper_Freeway"))
        if t is not None and t.portals:
            px, pz = t.portals[0]["position"]
            state.choppers.append(library.chopper("Scout", (px + 80.0, 22.0, pz - 6.0), 270.0, name="Chopper_Tunnel"))
        sky = state.built["City_SkyRoads"]["building"]
        k = len(sky.loop) // 3
        a, c = sky.loop[k], sky.loop[k + 1]
        yaw = math.degrees(math.atan2(c[0] - a[0], c[2] - a[2]))
        state.choppers.append(library.chopper("Gunship", (a[0], a[1] + 12.0, a[2]), yaw, name="Chopper_Sky"))
        sx, sy, sz = sky.spiral_centre
        state.choppers.append(library.chopper("Scout", (sx + 20.0, sy * 0.45, sz - 70.0), 200.0, name="Chopper_Spiral"))
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
            _export_fbx(objects, os.path.join(models_dir, file_name), colours="LINEAR")
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


def _export_fbx(objects, path, colours="NONE"):
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
        use_mesh_modifiers=True, colors_type=colours)


def _city_lights(state):
    """Every real light in city space: from each placed building (its local lights turned and moved to the layout
    spot), the city parts (roads, utilities, shanties) and glow points along the lava. Unity keeps the most important
    ones (CityAssets.BuildScene)."""
    out = []

    def add(kind, p, colour, range_m, intensity):
        out.append({"kind": kind, "position": _r(p), "colour": colour, "range": round(range_m, 1),
                    "intensity": round(intensity, 2)})

    for kind, p, colour, range_m, intensity in _placed(state, "lights"):
        add(kind, p, colour, range_m, intensity)
    t = state.terrain
    glow = terrain_biomes.BIOMES[state.biome].get("glow")
    if glow and getattr(t, "lava", None):
        from shapely.geometry import LineString
        for path, width in t.lava:
            line = LineString(path)
            d = 0.0
            while d < line.length:
                p = line.interpolate(d)
                add("lava", (p.x, float(t.sample(p.x, p.y)) + 4.0 + width * 0.15, p.y), glow, 30.0 + width * 1.5, 4.0)
                d += 55.0
    return out


def _placed(state, attribute):
    """(record with its position in city space) for every placed building and city part: buildings' local records
    are turned and moved to their layout spot. Records are tuples (kind, position, *rest)."""
    if state.layout is not None:
        for name, (px, _py, pz), yaw in state.layout.instances:
            c, s_ = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
            for kind, (lx, ly, lz), *rest in getattr(state.built[name]["building"], attribute):
                yield (kind, (px + lx * c + lz * s_, ly, pz - lx * s_ + lz * c), *rest)
    for name in roads.CITY_PARTS:
        if name in state.built:
            yield from getattr(state.built[name]["building"], attribute)


def _lava_paths(state):
    """Lava centre lines for the heat shimmer in the game (CityTrafficSystem): points every 8 m on the lava surface
    (flat x, y, z list: JsonUtility cannot read nested arrays) and the width. Empty on maps without lava."""
    import numpy as np
    from shapely.geometry import LineString
    t = state.terrain
    out = []
    for path, width in getattr(t, "lava", None) or []:
        line = LineString(path)
        points = []
        for d in np.arange(0.0, line.length + 0.1, 8.0):
            p = line.interpolate(float(d))
            points += [round(p.x, 2), round(float(t.sample(p.x, p.y)) + 0.3, 2), round(p.y, 2)]
        out.append({"points": points, "width": round(float(width), 2)})
    return out


def _city_emitters(state):
    """Steam, smoke and heat outlets in city space (CityAssets.PlaceEmitters makes particle systems there)."""
    return [{"kind": kind, "position": _r(p), "radius": round(radius, 2)} for kind, p, radius in _placed(state, "emitters")]


# Height fog per biome: colour (hex), density at the base (1/m), base height (m, city floor = 0), falloff height (m:
# the density drops to 1/e this much higher), most opacity. A style's "height_fog" overrides it.
HEIGHT_FOG = {
    "volcanic": dict(colour="#4a3d37", density=0.0016, base=0.0, falloff=55.0, most=0.88),
    "toxic": dict(colour="#5b6a3a", density=0.0014, base=0.0, falloff=45.0, most=0.85),
    "rain_desert": dict(colour="#7d7a74", density=0.0010, base=0.0, falloff=90.0, most=0.8),
    "arctic": dict(colour="#c9d3da", density=0.0008, base=0.0, falloff=80.0, most=0.75),
    "jungle": dict(colour="#7f8f80", density=0.0012, base=0.0, falloff=50.0, most=0.8),
    "desert": dict(colour="#c2ad8f", density=0.0004, base=0.0, falloff=140.0, most=0.6),
}


def _height_fog(state):
    """The map's height fog for the game (TerrainBiome and buildings get it from the full-screen fog pass)."""
    fog = dict(HEIGHT_FOG.get(state.biome, HEIGHT_FOG["desert"]), **(state.style.get("height_fog") or {}))
    return {key: (round(value, 6) if isinstance(value, float) else value) for key, value in fog.items()}


def export_terrain(state, out_dir):
    """Terrain/<chunk>.fbx (LOD0..2 each), a 16-bit RAW height map and <kit>.terrain.json (chunks, height map,
    markers, paths, the building layout on the terrain). Returns a short summary for the main manifest."""
    t = state.terrain
    folder = os.path.join(out_dir, "Terrain")
    os.makedirs(folder, exist_ok=True)
    collection = bpy.data.collections[TERRAIN_EXPORT]
    collection.hide_viewport = False
    chunks = []
    try:
        for name, objects, factors in state.terrain_chunks:
            _export_fbx(objects, os.path.join(folder, f"{name}.fbx"), colours="LINEAR")
            chunks.append({"name": name, "file": f"Terrain/{name}.fbx", "stepFactors": list(factors),
                           "triangles": [sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects]})
        backdrop = []
        for obj in state.backdrop:
            _export_fbx([obj], os.path.join(folder, f"{obj.name}.fbx"), colours="LINEAR")
            backdrop.append({"name": obj.name, "file": f"Terrain/{obj.name}.fbx",
                             "triangles": sum(len(p.vertices) - 2 for p in obj.data.polygons)})
    finally:
        collection.hide_viewport = True
    masks, normals = f"{state.kit}_TerrainMasks.png", f"{state.kit}_TerrainNormal.png"
    t.write_surface(os.path.join(folder, masks), os.path.join(folder, normals))
    table = terrain_biomes.write_table(os.path.join(folder, f"{state.kit}_BiomeTable.png"))
    raw = f"{state.kit}_Height.raw"
    low, high = t.write_raw(os.path.join(folder, raw))
    cx, _cy, cz = state.centre
    data = {
        "kit": state.kit,
        "generator": "Tools/CityForge (terrain.py)",
        "axes": "Unity space, metres. Origin = the city centre on the ground (city floor y = 0).",
        "size": t.values["size"], "step": terrain.STEP, "chunkSize": terrain.STEP * terrain.CHUNK_CELLS,
        "lod": {"levels": len(terrain.LOD_FACTORS), "screenHeights": list(LOD_SCREEN_HEIGHTS),
                "note": "Grid step per LOD = step x the chunk's stepFactors (edge chunks have half the detail). "
                        "Skirts hang 10 m down from every chunk edge, so LOD seams show no cracks."},
        "uv": "UV0 = palette swatch (fallback). No vertex colours: the TerrainBiome shader graph reads the surface "
              "textures by world position.",
        "surface": {"masks": f"Terrain/{masks}", "normal": f"Terrain/{normals}", "size": t.values["size"],
                    "resolution": t.values["surface"]["resolution"],
                    "note": "Whole map, centred on the origin; first image row = +Z edge. uv = xz / size + 0.5. Both "
                            "linear (sRGB off), bilinear, clamp. Masks RGBA: R flow, G hollow, B made ground (city, "
                            "roads, channel), A scree. Normal RGB = world normal * 0.5 + 0.5 (Unity axes)."},
        "biomes": dict(table, file=f"Terrain/{table['file']}", current=state.biome,
                       note="Biome table: one row of 16 pixels per biome (terrain_biomes.row). Import sRGB off, point, "
                            "no mipmaps, no compression. _BiomeIndex = row."),
        "collider": "Mesh collider from each chunk's LOD0 mesh.",
        "heightmap": {"file": f"Terrain/{raw}", "format": "RAW 16-bit little-endian (Unity: Windows byte order)",
                      "resolution": t.count, "min": round(low, 3), "max": round(high, 3),
                      "rows": "first row = -Z edge"},
        "chunks": chunks,
        "backdrop": {"parts": backdrop,
                     "note": "Far scenery out to 20 km (mountains, the canyon bending away, the megacity skyline beyond "
                             "the walls). No colliders; outside the flight bounds. Hide its far edge with fog."},
        "atmosphere": {"fogStart": 300.0, "fogScale": 3000.0,
                       "note": "Suggested exponential distance fog (the previews use the same numbers): "
                               "factor = 1 - exp(-(distance - start) / scale)."},
        "markers": [{"kind": kind, "index": index, "position": _r(position), "radius": radius}
                    for kind, index, position, radius in t.markers],
        "paths": {"canyon": [_r(p) for p in t.path_points(t.canyon)],
                  "channel": [_r(p) for p in t.path_points(t.channel)]},
        "layout": ([{"building": name, "position": _r(p), "yaw": yaw} for name, p, yaw in state.layout.instances]
                   + [{"building": name, "position": [0.0, 0.0, 0.0], "yaw": 0.0}
                      for name in roads.CITY_PARTS if name in state.built]
                   if state.layout is not None else
                   [{"building": name, "position": _r((p[0] - cx, p[1], p[2] - cz)), "yaw": 0.0}
                    for name, p in state.placements.items()]),
        "lights": _city_lights(state),
        "emitters": _city_emitters(state),
        "heightFog": _height_fog(state),
        "traffic": traffic.paths(state, _clearance(state)),
        "lava": _lava_paths(state),
        "flightBounds": {"centre": [0.0, 450.0, 0.0], "size": [t.values["size"] - 400.0, 900.0, t.values["size"] - 400.0]},
        "audit": state.terrain_problems,
    }
    with open(os.path.join(out_dir, f"{state.kit}.terrain.json"), "w") as handle:
        json.dump(data, handle, indent=2)
    return {"file": f"{state.kit}.terrain.json", "chunks": len(chunks), "audit": state.terrain_problems}


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


HAZE = {False: (300.0, 3000.0, 0.85), True: (200.0, 2200.0, 0.9)}  # night: (start, scale, max)


def _depth_material():
    material = bpy.data.materials.get("CF Depth")
    if material is None:
        material = bpy.data.materials.new("CF Depth")
        material.use_nodes = True
        nodes, links = material.node_tree.nodes, material.node_tree.links
        nodes.clear()
        camera = nodes.new("ShaderNodeCameraData")
        emission = nodes.new("ShaderNodeEmission")
        emission.inputs["Color"].default_value = (1, 1, 1, 1)
        output = nodes.new("ShaderNodeOutputMaterial")
        links.new(camera.outputs["View Distance"], emission.inputs["Strength"])
        links.new(emission.outputs[0], output.inputs["Surface"])
    return material


def _render_view(state, scene, path, night):
    """A preview render; with terrain shown, distance haze too: a second, one-sample render writes the camera
    distance of every pixel (material override), and numpy blends the picture towards the sky colour at the horizon
    (the Blender compositor needs a GPU here)."""
    _render(scene, path)
    if not state.terrain_shown:
        return
    import numpy as np

    layer = scene.view_layers[0]
    world_before = scene.world
    saved = (scene.cycles.samples, scene.cycles.use_denoising, scene.render.image_settings.file_format,
             scene.view_settings.view_transform, scene.view_settings.look, scene.view_settings.exposure,
             scene.render.filter_size)
    depth_world = bpy.data.worlds.get("CF Depth World") or bpy.data.worlds.new("CF Depth World")
    depth_world.use_nodes = True
    depth_world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.0
    depth_path = os.path.join(os.path.dirname(path), ".depth.exr")
    try:
        layer.material_override = _depth_material()
        scene.world = depth_world
        scene.cycles.samples, scene.cycles.use_denoising = 1, False
        scene.render.image_settings.file_format = "OPEN_EXR"
        scene.view_settings.view_transform = "Standard"
        scene.view_settings.look = "None"
        scene.view_settings.exposure = 0.0
        scene.render.filter_size = 0.01
        _render(scene, depth_path)
    finally:
        layer.material_override = None
        scene.world = world_before
        (scene.cycles.samples, scene.cycles.use_denoising, scene.render.image_settings.file_format,
         scene.view_settings.view_transform, scene.view_settings.look, scene.view_settings.exposure,
         scene.render.filter_size) = saved

    def load(file_path):
        image = bpy.data.images.load(file_path)
        pixels = np.empty(len(image.pixels), dtype=np.float32)
        image.pixels.foreach_get(pixels)
        shape = (image.size[1], image.size[0], 4)
        bpy.data.images.remove(image)
        return pixels.reshape(shape)

    picture, depth = load(path), load(depth_path)[:, :, 0]
    os.remove(depth_path)
    sky = depth <= 0.0
    start, scale, most = HAZE[night]
    factor = np.clip(1.0 - np.exp(-np.maximum(depth - start, 0.0) / scale), 0.0, most)
    factor[sky] = 0.0
    # Fog colour = the sky just above the horizon (rows go up from the bottom).
    colour = np.array([0.42, 0.42, 0.42]) if not night else np.array([0.02, 0.025, 0.035])
    if sky.any():
        rows = np.where(sky.any(axis=1))[0]
        band = sky[rows[0]: rows[0] + 25]
        colour = picture[rows[0]: rows[0] + 25][band][:, :3].mean(axis=0)
    picture[:, :, :3] = picture[:, :, :3] * (1.0 - factor[..., None]) + colour * factor[..., None]
    out = bpy.data.images.new("CF Haze", picture.shape[1], picture.shape[0])
    out.pixels.foreach_set(picture.ravel())
    out.filepath_raw = path
    out.file_format = "JPEG"
    out.save()
    bpy.data.images.remove(out)


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

    ground = bpy.data.objects.get("CF Ground")
    if state.terrain_shown:
        _visible(state.terrain_shown, True)
        ground.hide_render = True
    # District by day and by night (same camera: a chopper coming in high from the front left).
    _visible(lineup, False)
    _visible(district, True)
    _frame(camera, aim, district, (-0.5, -1.0, 0.3), lens=35.0, margin=0.5)
    set_time(scene, night=False)
    _render_view(state, scene, os.path.join(preview_dir, "district_day.jpg"), False)
    set_time(scene, night=True)
    _render_view(state, scene, os.path.join(preview_dir, "district_night.jpg"), True)
    # Street view at chopper height.
    if state.layout is not None:
        # From the front edge of the city, low, looking in towards the core.
        half_d = state.layout.depth / 2
        camera.location = geo.to_blender((-70.0, 32.0, half_d + 45.0))
        aim.location = geo.to_blender((10.0, 70.0, 60.0))
    else:
        street = [obj for obj in district if obj.name.startswith(("District_Block", "District_Ind", "District_Tower"))]
        corners = _corners(street)
        low_y = min(c.y for c in _corners([o for o in district if o.name.startswith("District_Block")]))
        aim.location = Vector((min(c.x for c in corners) + 40.0, low_y - STREET / 2 + 4.0, 22.0))
        camera.location = Vector((max(c.x for c in corners) + 30.0, low_y - STREET / 2 - 6.0, 26.0))
    camera.data.lens = 28.0
    _render_view(state, scene, os.path.join(preview_dir, "street_night.jpg"), True)
    set_time(scene, night=False)
    _render_view(state, scene, os.path.join(preview_dir, "street_day.jpg"), False)
    _scale_shots(state, scene, camera, aim, preview_dir)
    if state.terrain_shown:
        _biome_sheet(state, scene, camera, aim, os.path.join(preview_dir, "biomes_sheet.jpg"))
    chopper_objects = state.library.objects() if getattr(state, "library", None) else []
    _visible(chopper_objects, False)

    # LOD line-up: LOD0, LOD1, LOD2 of a few buildings, side by side (studio floor, no terrain).
    _visible(state.terrain_shown, False)
    ground.hide_render = False
    _visible(district, False)
    _visible(lineup, True)
    _frame(camera, aim, lineup, (-0.2, -1.0, 0.3), lens=50.0, margin=0.6)
    _render(scene, os.path.join(preview_dir, "lods.jpg"))
    _visible(lineup, False)

    if sheet:
        _building_sheet(state, scene, camera, aim, os.path.join(preview_dir, "buildings_sheet.jpg"), np)
    _visible(district + lineup + chopper_objects, True)
    _frame(camera, aim, district, (-0.55, -1.0, 0.6), lens=35.0, margin=0.72)


def _terrain_shots(state, by_name):
    """Terrain views (Blender camera, aim, lens): overview from high over the canyon side, a chopper in the canyon,
    one in the storm drain, one on a mesa top."""
    t = state.terrain
    if t is None:
        return []

    def b(point):
        return geo.to_blender(_world(state, point))

    shots = [("terrain_overview", b((1150.0, 760.0, 1250.0)), b((60.0, 40.0, -80.0)), 24.0),
             # From mid height over the city, looking out at the rim: the wall should hide the map edge.
             ("rim", b((0.0, 220.0, 260.0)), b((-400.0, 230.0, 2400.0)), 24.0)]
    if "Chopper_Canyon" in by_name:
        k = len(t.canyon) // 3
        ahead, behind = t.canyon[max(0, k - 3)], t.canyon[min(len(t.canyon) - 1, k + 2)]
        shots.append(("canyon", b((behind[0], float(t.sample(*behind)) + 34.0, behind[1])),
                      b((ahead[0], float(t.sample(*ahead)) + 14.0, ahead[1])), 22.0))
    if "Chopper_Channel" in by_name:
        cx = _channel_spot(state)
        shots.append(("channel", b((cx - 46.0, 3.0, t.channel_z + 6.0)), b((cx + 40.0, 7.0, t.channel_z)), 22.0))
    if "Chopper_Freeway" in by_name:
        p = by_name["Chopper_Freeway"].location
        shots.append(("freeway", Vector((p.x + 70.0, p.y + 34.0, p.z + 1.0)), Vector((p.x - 60.0, p.y - 24.0, p.z + 6.0)),
                      24.0))
    if "Chopper_Tunnel" in by_name and t.portals:
        px, pz = t.portals[0]["position"]
        shots.append(("tunnel", b((px + 190.0, 34.0, pz + 45.0)), b((px, 10.0, pz)), 28.0))
    if "Chopper_Sky" in by_name:
        sky = state.built["City_SkyRoads"]["building"]
        k = len(sky.loop) // 3
        behind, ahead = sky.loop[k - 6], sky.loop[(k + 14) % len(sky.loop)]
        shots.append(("skyloop", geo.to_blender((behind[0], behind[1] + 24.0, behind[2])),
                      geo.to_blender((ahead[0], ahead[1] + 4.0, ahead[2])), 22.0))
    if "Chopper_Spiral" in by_name:
        sx, sy, sz = state.built["City_SkyRoads"]["building"].spiral_centre
        shots.append(("spiral", geo.to_blender((sx + 150.0, sy * 0.55, sz - 190.0)),
                      geo.to_blender((sx, sy * 0.45, sz)), 24.0))
    if getattr(t, "lava", None):
        # Low over the lava moat on the far side from the canyon, looking along it to the crater wall.
        moat = t.lava[0][0]
        k = len(moat) * 3 // 8
        here, ahead = moat[k], moat[(k + 6) % len(moat)]
        shots.append(("lava", b((here[0] * 0.92, 40.0, here[1] * 0.92)), b((ahead[0] * 1.15, 25.0, ahead[1] * 1.15)), 22.0))
    if state.layout is not None:
        # The refinery: the first cooling tower (else refinery) the layout placed, from chopper height beside it.
        for wanted in ("Ind_Cooling", "Ind_Refinery"):
            spot = next((p for name, p, _yaw in state.layout.instances if name.split("_V")[0] == wanted), None)
            if spot is not None:
                x, _y, z = spot
                away = Vector((x, z)).normalized() if math.hypot(x, z) > 1.0 else Vector((1.0, 0.0))
                shots.append(("refinery", geo.to_blender((x - away.x * 110.0 + away.y * 50.0, 55.0,
                                                          z - away.y * 110.0 - away.x * 50.0)),
                              geo.to_blender((x, 22.0, z)), 24.0))
                break
    racks = getattr(state.built.get("City_Utilities", {}).get("building"), "racks", None)
    if racks:
        # Down a refinery street: the longest pipe rack, from low beside it.
        rack = max(racks, key=lambda line: line.length)
        mid, ahead = rack.interpolate(0.3, normalized=True), rack.interpolate(0.8, normalized=True)
        dx, dz = ahead.x - mid.x, ahead.y - mid.y
        n = math.hypot(dx, dz) or 1.0
        shots.append(("pipes", geo.to_blender((mid.x - dx / n * 40.0 + dz / n * 18.0, 22.0, mid.y - dz / n * 40.0 - dx / n * 18.0)),
                      geo.to_blender((ahead.x, 6.0, ahead.y)), 22.0))
    clusters = getattr(state.built.get("City_Shanties", {}).get("building"), "clusters", None)
    if clusters:
        # The wall town: the tallest cluster, from out over the crater floor at its height.
        cluster = max(clusters, key=len)
        x, y, z, _w, _h, (nx, nz) = cluster[len(cluster) // 2]
        shots.append(("shanties", geo.to_blender((x + nx * 110.0 - nz * 40.0, y + 15.0, z + nz * 110.0 + nx * 40.0)),
                      geo.to_blender((x, y, z)), 26.0))
    if "Chopper_Mesa" in by_name and t.markers:
        _kind, _i, (mx, my, mz), _r = sorted(t.markers, key=lambda m: math.hypot(m[2][0], m[2][2]))[0]
        away = Vector((mx, mz)).normalized()
        shots.append(("mesa", b((mx + away.x * 45.0, my + 14.0, mz + away.y * 45.0)), b((0.0, 160.0, 0.0)), 22.0))
    return shots


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
    bridge = by_name.get("Chopper_Bridge")
    if bridge is not None:
        p = Vector(bridge.location)
        # Blender -Y = Unity +Z (the plot front): stand in front of the twins, a little below the bridge.
        shots.append(("bridge", p + Vector((-14.0, -58.0, -6.0)), p + Vector((0.0, 0.0, 10.0)), 24.0))
    landmarks = [obj for obj in state.district if obj.name.startswith("District_Landmark")]
    if landmarks:
        corners = _corners(landmarks)
        centre = sum(corners, Vector()) / len(corners)
        low = min(c.y for c in corners)
        width = max(c.x for c in corners) - min(c.x for c in corners)
        shots.append(("landmarks", Vector((centre.x - width * 0.25, low - width * 0.75, 110.0)),
                      Vector((centre.x, centre.y, 150.0)), 24.0))
    shots += _terrain_shots(state, by_name)
    for name, camera_at, aim_at, lens in shots:
        camera.location, aim.location, camera.data.lens = camera_at, aim_at, lens
        set_time(scene, night=False)
        _render_view(state, scene, os.path.join(preview_dir, f"{name}_day.jpg"), False)
        set_time(scene, night=True)
        _render_view(state, scene, os.path.join(preview_dir, f"{name}_night.jpg"), True)
    set_time(scene, night=False)


def _biome_sheet(state, scene, camera, aim, path):
    """The same terrain view in every biome (terrain_biomes.NAMES order, 3 per row, by day, with haze)."""
    import numpy as np

    t = state.terrain
    by_name = {obj.name: obj for obj in state.choppers}
    shots = {name: (c, a, lens) for name, c, a, lens in _terrain_shots(state, by_name)}
    view = shots.get("mesa") or shots["terrain_overview"]
    camera.location, aim.location, camera.data.lens = view
    width, height = scene.render.resolution_x, scene.render.resolution_y
    scene.render.resolution_x, scene.render.resolution_y = width // 2, height // 2
    tiles = []
    set_time(scene, night=False)
    try:
        for biome in terrain_biomes.NAMES:
            terrain_biomes.make_material(biome, f"{state.kit}_Terrain", state.surface)
            tile = os.path.join(os.path.dirname(path), ".biome.jpg")
            _render_view(state, scene, tile, False)
            image = bpy.data.images.load(tile)
            pixels = np.empty(len(image.pixels), dtype=np.float32)
            image.pixels.foreach_get(pixels)
            tiles.append(pixels.reshape(image.size[1], image.size[0], 4))
            bpy.data.images.remove(image)
            os.remove(tile)
    finally:
        scene.render.resolution_x, scene.render.resolution_y = width, height
        terrain_biomes.make_material(state.biome, f"{state.kit}_Terrain", state.surface)
    columns = 3
    rows = [np.concatenate(tiles[i:i + columns], axis=1) for i in range(0, len(tiles), columns)]
    sheet = np.concatenate(rows[::-1], axis=0)
    out = bpy.data.images.new("CF Biomes", sheet.shape[1], sheet.shape[0])
    out.pixels.foreach_set(sheet.ravel())
    out.filepath_raw = path
    out.file_format = "JPEG"
    out.save()
    bpy.data.images.remove(out)
    del t


def _building_sheet(state, scene, camera, aim, path, np):
    """Every building alone (front left, by day), four per row, half size tiles."""
    width, height = scene.render.resolution_x, scene.render.resolution_y
    scene.render.resolution_x, scene.render.resolution_y = width // 2, height // 2
    tiles = []
    stage = bpy.data.collections[STAGE_COLLECTION]
    base = [entry for name, entry in state.built.items()
            if "variantOf" not in entry["building"].spec and entry["building"].type != "props"]
    _visible(state.district, False)
    try:
        for entry in base:
            obj = bpy.data.objects.new("Sheet_Tile", entry["objects"][0].data)
            stage.objects.link(obj)
            _frame(camera, aim, [obj], (-0.75, -1.0, 0.45), lens=40.0, margin=0.95)
            tile = os.path.join(os.path.dirname(path), ".tile.jpg")
            _render(scene, tile)
            image = bpy.data.images.load(tile)
            pixels = np.empty(len(image.pixels), dtype=np.float32)
            image.pixels.foreach_get(pixels)
            tiles.append(pixels.reshape(image.size[1], image.size[0], 4))
            bpy.data.images.remove(image)
            os.remove(tile)
            bpy.data.objects.remove(obj, do_unlink=True)
    finally:
        scene.render.resolution_x, scene.render.resolution_y = width, height
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
          names=None, sheet=True, with_terrain=True):
    style = styles.get(style_name)
    if bpy.app.background:
        bpy.ops.wm.read_factory_settings(use_empty=True)
    state = generate(style, kit_name, names, with_terrain=with_terrain)
    manifest = export(state, out_dir)
    if state.terrain is not None:
        manifest["terrain"] = export_terrain(state, out_dir)
        with open(os.path.join(out_dir, f"{state.kit}.manifest.json"), "w") as handle:
            json.dump(manifest, handle, indent=2)
    if render and preview_dir:
        render_previews(state, preview_dir, samples, sheet=sheet)
    if blend_path:
        save_blend(blend_path)
    return state, manifest
