"""
Surface detail with Geometry Nodes: the "PF Surface Detail" node group, run on every chassis after the bevel and baked
into the exported mesh. It adds:

- inset panels: random large hull faces are inset and pushed in; some turn light or dark (palette swatches),
- hex nuts at the corners of every panel, aligned to the surface,
- greebles: small boxes scattered on large hull faces.

The group is made once per .blend and then reused, so it can be edited in Blender's Geometry Nodes editor; the
panel's "Reset detail nodes" makes it again from this file. Its inputs are set per part from the style's "detail"
values (see ART_DIRECTION.md). Only hull faces (by palette swatch) get panels and greebles: glass, lights and
warning paint stay clean.
"""

import bpy

from .palette import swatch as S

GROUP = "PF Surface Detail"

# Inputs: (name, socket type, default, min, max, description). Style "detail" keys map to these names (DETAIL_KEYS).
INPUTS = [
    ("Seed", "NodeSocketInt", 0, 0, 1_000_000, "Chance pattern of panels, colours and greebles"),
    ("Panels", "NodeSocketFloat", 0.5, 0.0, 1.0, "Share of large hull faces that become inset panels"),
    ("Min Panel Area", "NodeSocketFloat", 0.12, 0.0, 5.0, "Faces smaller than this (m²) get no panel or greebles"),
    ("Inset", "NodeSocketFloat", 0.82, 0.5, 0.98, "Panel size relative to its face"),
    ("Depth", "NodeSocketFloat", 0.02, 0.0, 0.1, "How far panels sink into the hull (m)"),
    ("Recolour", "NodeSocketFloat", 0.5, 0.0, 1.0, "Share of panels painted light or dark"),
    ("Bolt Size", "NodeSocketFloat", 0.035, 0.0, 0.12, "Hex nut radius (m); 0 = no nuts"),
    ("Greebles", "NodeSocketFloat", 0.3, 0.0, 4.0, "Small boxes per m² on large hull faces"),
    ("Greeble Size", "NodeSocketFloat", 0.12, 0.02, 0.6, "Largest greeble (m)"),
    ("Hull Swatch", "NodeSocketInt", S("hull"), 0, 24, "Palette cell that counts as hull"),
    ("Light Swatch", "NodeSocketInt", S("hull_light"), 0, 24, "Palette cell for light panels"),
    ("Dark Swatch", "NodeSocketInt", S("hull_dark"), 0, 24, "Palette cell for dark panels and greebles"),
    ("Bolt Swatch", "NodeSocketInt", S("gunmetal"), 0, 24, "Palette cell for nuts"),
]
DETAIL_KEYS = {"panels": "Panels", "min_area": "Min Panel Area", "inset": "Inset", "depth": "Depth",
               "recolour": "Recolour", "bolt_size": "Bolt Size", "greebles": "Greebles", "greeble_size": "Greeble Size"}
DEFAULTS = {key: next(default for name, _t, default, *_ in INPUTS if name == socket) for key, socket in DETAIL_KEYS.items()}


def reset():
    """Removes the node group, so the next build makes it again from this file."""
    group = bpy.data.node_groups.get(GROUP)
    if group is not None:
        bpy.data.node_groups.remove(group)


def ensure_group():
    """The node group in this .blend (kept if it has every input, so your edits survive rebuilds)."""
    group = bpy.data.node_groups.get(GROUP)
    if group is not None:
        names = {item.name for item in group.interface.items_tree if getattr(item, "in_out", "") == "INPUT"}
        if all(name in names for name, *_ in INPUTS):
            return group
        bpy.data.node_groups.remove(group)
    return _build_group()


def add_modifier(obj, seed, values):
    """Adds the detail modifier to obj with the style's values (missing keys use the defaults)."""
    group = ensure_group()
    modifier = obj.modifiers.new("PF Surface Detail", "NODES")
    modifier.node_group = group
    settings = dict(DEFAULTS, **(values or {}))
    by_name = {item.name: item.identifier for item in group.interface.items_tree if getattr(item, "in_out", "") == "INPUT"}
    _set_input(modifier, by_name["Seed"], int(seed) % 1_000_000)
    for key, socket in DETAIL_KEYS.items():
        _set_input(modifier, by_name[socket], float(settings[key]))
    return modifier


def _set_input(modifier, identifier, value):
    """Blender 5: modifier.properties.inputs.<identifier>.value; Blender 4: modifier[identifier]."""
    properties = getattr(modifier, "properties", None)
    if properties is not None and hasattr(properties, "inputs"):
        getattr(properties.inputs, identifier).value = value
    else:
        modifier[identifier] = value


# -- node group -----------------------------------------------------------------------------------------------------

def _build_group():
    group = bpy.data.node_groups.new(GROUP, "GeometryNodeTree")
    group.use_fake_user = True  # kept in the .blend for editing, even though the baked parts no longer use it
    group.interface.new_socket("Geometry", in_out="INPUT", socket_type="NodeSocketGeometry")
    for name, socket_type, default, low, high, description in INPUTS:
        socket = group.interface.new_socket(name, in_out="INPUT", socket_type=socket_type)
        socket.default_value, socket.min_value, socket.max_value = default, low, high
        socket.description = description
    group.interface.new_socket("Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")

    nodes, links = group.nodes, group.links
    x = [0]

    def node(kind, **props):
        n = nodes.new(kind)
        for key, value in props.items():
            setattr(n, key, value)
        n.location = (x[0], 0)
        x[0] += 180
        return n

    def link(a, b):
        links.new(a, b)

    gi = node("NodeGroupInput")
    go = node("NodeGroupOutput")

    def gin(name):
        return gi.outputs[name]

    # -- hull faces, large enough --------------------------------------------------------------------------------
    swatch_attr = node("GeometryNodeInputNamedAttribute", data_type="INT")
    swatch_attr.inputs["Name"].default_value = "swatch"
    is_hull = node("FunctionNodeCompare", data_type="INT", operation="EQUAL")
    link(swatch_attr.outputs["Attribute"], is_hull.inputs["A"])
    link(gin("Hull Swatch"), is_hull.inputs["B"])
    area = node("GeometryNodeInputMeshFaceArea")
    big = node("FunctionNodeCompare", data_type="FLOAT", operation="GREATER_THAN")
    link(area.outputs["Area"], big.inputs["A"])
    link(gin("Min Panel Area"), big.inputs["B"])
    hull_big = node("FunctionNodeBooleanMath", operation="AND")
    link(is_hull.outputs["Result"], hull_big.inputs[0])
    link(big.outputs["Result"], hull_big.inputs[1])

    # -- inset panels --------------------------------------------------------------------------------------------
    pick = node("FunctionNodeRandomValue", data_type="BOOLEAN")
    link(gin("Panels"), pick.inputs["Probability"])
    link(gin("Seed"), pick.inputs["Seed"])
    panel_sel = node("FunctionNodeBooleanMath", operation="AND")
    link(hull_big.outputs["Boolean"], panel_sel.inputs[0])
    link(pick.outputs["Value"], panel_sel.inputs[1])

    ring = node("GeometryNodeExtrudeMesh", mode="FACES")
    ring.inputs["Offset Scale"].default_value = 0.0
    ring.inputs["Individual"].default_value = True
    link(gin("Geometry"), ring.inputs["Mesh"])
    link(panel_sel.outputs["Boolean"], ring.inputs["Selection"])
    shrink = node("GeometryNodeScaleElements", domain="FACE")
    link(ring.outputs["Mesh"], shrink.inputs["Geometry"])
    link(ring.outputs["Top"], shrink.inputs["Selection"])
    link(gin("Inset"), shrink.inputs["Scale"])
    sink_depth = node("ShaderNodeMath", operation="MULTIPLY")
    sink_depth.inputs[1].default_value = -1.0
    link(gin("Depth"), sink_depth.inputs[0])
    sink = node("GeometryNodeExtrudeMesh", mode="FACES")
    sink.inputs["Individual"].default_value = True
    link(shrink.outputs["Geometry"], sink.inputs["Mesh"])
    link(ring.outputs["Top"], sink.inputs["Selection"])
    link(sink_depth.outputs["Value"], sink.inputs["Offset Scale"])

    # Some panels light, some dark.
    recolour = node("FunctionNodeRandomValue", data_type="BOOLEAN")
    link(gin("Recolour"), recolour.inputs["Probability"])
    seed_b = node("FunctionNodeIntegerMath", operation="ADD")
    link(gin("Seed"), seed_b.inputs[0])
    seed_b.inputs[1].default_value = 17
    link(seed_b.outputs[0], recolour.inputs["Seed"])
    light_or_dark = node("FunctionNodeRandomValue", data_type="BOOLEAN")
    light_or_dark.inputs["Probability"].default_value = 0.5
    seed_c = node("FunctionNodeIntegerMath", operation="ADD")
    link(gin("Seed"), seed_c.inputs[0])
    seed_c.inputs[1].default_value = 31
    link(seed_c.outputs[0], light_or_dark.inputs["Seed"])
    paint = node("GeometryNodeSwitch", input_type="INT")
    link(light_or_dark.outputs["Value"], paint.inputs["Switch"])
    link(gin("Dark Swatch"), paint.inputs["False"])
    link(gin("Light Swatch"), paint.inputs["True"])
    paint_sel = node("FunctionNodeBooleanMath", operation="AND")
    link(sink.outputs["Top"], paint_sel.inputs[0])
    link(recolour.outputs["Value"], paint_sel.inputs[1])
    store_paint = node("GeometryNodeStoreNamedAttribute", data_type="INT", domain="FACE")
    store_paint.inputs["Name"].default_value = "swatch"
    link(sink.outputs["Mesh"], store_paint.inputs["Geometry"])
    link(paint_sel.outputs["Boolean"], store_paint.inputs["Selection"])
    link(paint.outputs["Output"], store_paint.inputs["Value"])
    panelled = store_paint.outputs["Geometry"]

    # -- hex nuts at the panel corners ---------------------------------------------------------------------------
    copy = node("GeometryNodeDuplicateElements", domain="FACE")
    link(panelled, copy.inputs["Geometry"])
    link(sink.outputs["Top"], copy.inputs["Selection"])
    corner_in = node("GeometryNodeScaleElements", domain="FACE")
    corner_in.inputs["Scale"].default_value = 0.86
    link(copy.outputs["Geometry"], corner_in.inputs["Geometry"])
    normal = node("GeometryNodeInputNormal")
    store_normal = node("GeometryNodeStoreNamedAttribute", data_type="FLOAT_VECTOR", domain="POINT")
    store_normal.inputs["Name"].default_value = "pf_normal"
    link(corner_in.outputs["Geometry"], store_normal.inputs["Geometry"])
    link(normal.outputs["Normal"], store_normal.inputs["Value"])
    corners = node("GeometryNodeMeshToPoints", mode="VERTICES")
    link(store_normal.outputs["Geometry"], corners.inputs["Mesh"])
    nut = node("GeometryNodeMeshCylinder", fill_type="NGON")
    nut.inputs["Vertices"].default_value = 6
    link(gin("Bolt Size"), nut.inputs["Radius"])
    nut_depth = node("ShaderNodeMath", operation="MULTIPLY")
    nut_depth.inputs[1].default_value = 0.6
    link(gin("Bolt Size"), nut_depth.inputs[0])
    link(nut_depth.outputs["Value"], nut.inputs["Depth"])
    nut_paint = node("GeometryNodeStoreNamedAttribute", data_type="INT", domain="FACE")
    nut_paint.inputs["Name"].default_value = "swatch"
    link(nut.outputs["Mesh"], nut_paint.inputs["Geometry"])
    link(gin("Bolt Swatch"), nut_paint.inputs["Value"])
    corner_normal = node("GeometryNodeInputNamedAttribute", data_type="FLOAT_VECTOR")
    corner_normal.inputs["Name"].default_value = "pf_normal"
    align = node("FunctionNodeAlignRotationToVector", axis="Z")
    link(corner_normal.outputs["Attribute"], align.inputs["Vector"])
    has_bolts = node("FunctionNodeCompare", data_type="FLOAT", operation="GREATER_THAN")
    link(gin("Bolt Size"), has_bolts.inputs["A"])
    has_bolts.inputs["B"].default_value = 0.001
    # Points copy the mesh's "swatch"; drop it so instances keep their own (else it becomes a per-point attribute).
    corners_clean = node("GeometryNodeRemoveAttribute")
    corners_clean.inputs["Name"].default_value = "swatch"
    link(corners.outputs["Points"], corners_clean.inputs["Geometry"])
    nuts = node("GeometryNodeInstanceOnPoints")
    link(corners_clean.outputs["Geometry"], nuts.inputs["Points"])
    link(has_bolts.outputs["Result"], nuts.inputs["Selection"])
    link(nut_paint.outputs["Geometry"], nuts.inputs["Instance"])
    link(align.outputs["Rotation"], nuts.inputs["Rotation"])
    nuts_real = node("GeometryNodeRealizeInstances")
    link(nuts.outputs["Instances"], nuts_real.inputs["Geometry"])
    drop_normal = node("GeometryNodeRemoveAttribute")
    drop_normal.inputs["Name"].default_value = "pf_normal"
    link(nuts_real.outputs["Geometry"], drop_normal.inputs["Geometry"])

    # -- greebles on large hull faces (not on panels) ------------------------------------------------------------
    not_panel = node("FunctionNodeBooleanMath", operation="NOT")
    link(sink.outputs["Top"], not_panel.inputs[0])
    swatch_now = node("GeometryNodeInputNamedAttribute", data_type="INT")
    swatch_now.inputs["Name"].default_value = "swatch"
    is_hull_now = node("FunctionNodeCompare", data_type="INT", operation="EQUAL")
    link(swatch_now.outputs["Attribute"], is_hull_now.inputs["A"])
    link(gin("Hull Swatch"), is_hull_now.inputs["B"])
    area_now = node("GeometryNodeInputMeshFaceArea")
    big_now = node("FunctionNodeCompare", data_type="FLOAT", operation="GREATER_THAN")
    link(area_now.outputs["Area"], big_now.inputs["A"])
    link(gin("Min Panel Area"), big_now.inputs["B"])
    greeble_sel_a = node("FunctionNodeBooleanMath", operation="AND")
    link(is_hull_now.outputs["Result"], greeble_sel_a.inputs[0])
    link(big_now.outputs["Result"], greeble_sel_a.inputs[1])
    greeble_sel = node("FunctionNodeBooleanMath", operation="AND")
    link(greeble_sel_a.outputs["Boolean"], greeble_sel.inputs[0])
    link(not_panel.outputs["Boolean"], greeble_sel.inputs[1])
    scatter = node("GeometryNodeDistributePointsOnFaces", distribute_method="RANDOM")
    link(panelled, scatter.inputs["Mesh"])
    link(greeble_sel.outputs["Boolean"], scatter.inputs["Selection"])
    link(gin("Greebles"), scatter.inputs["Density"])
    link(gin("Seed"), scatter.inputs["Seed"])
    # Greeble shapes: a rounded box (subdivided cube) and a short cylinder, picked at random, smooth shaded.
    box = node("GeometryNodeMeshCube")
    rounded = node("GeometryNodeSubdivisionSurface")
    rounded.inputs["Level"].default_value = 1
    link(box.outputs["Mesh"], rounded.inputs["Mesh"])
    puck = node("GeometryNodeMeshCylinder", fill_type="NGON")
    puck.inputs["Vertices"].default_value = 10
    puck.inputs["Radius"].default_value = 0.5
    puck.inputs["Depth"].default_value = 0.6
    shapes = node("GeometryNodeJoinGeometry")
    link(rounded.outputs["Mesh"], shapes.inputs["Geometry"])
    link(puck.outputs["Mesh"], shapes.inputs["Geometry"])
    smooth = node("GeometryNodeSetShadeSmooth")
    link(shapes.outputs["Geometry"], smooth.inputs["Mesh"])
    shape_paint = node("GeometryNodeStoreNamedAttribute", data_type="INT", domain="FACE")
    shape_paint.inputs["Name"].default_value = "swatch"
    link(smooth.outputs["Mesh"], shape_paint.inputs["Geometry"])
    link(gin("Dark Swatch"), shape_paint.inputs["Value"])
    box_paint = node("GeometryNodeSplitToInstances", domain="FACE")
    island = node("GeometryNodeInputMeshIsland")
    link(shape_paint.outputs["Geometry"], box_paint.inputs["Geometry"])
    link(island.outputs["Island Index"], box_paint.inputs["Group ID"])
    size_min = node("ShaderNodeVectorMath", operation="SCALE")
    size_min.inputs[0].default_value = (0.35, 0.25, 0.12)
    link(gin("Greeble Size"), size_min.inputs["Scale"])
    size_max = node("ShaderNodeVectorMath", operation="SCALE")
    size_max.inputs[0].default_value = (1.0, 0.7, 0.35)
    link(gin("Greeble Size"), size_max.inputs["Scale"])
    box_size = node("FunctionNodeRandomValue", data_type="FLOAT_VECTOR")
    link(size_min.outputs["Vector"], box_size.inputs["Min"])
    link(size_max.outputs["Vector"], box_size.inputs["Max"])
    link(gin("Seed"), box_size.inputs["Seed"])
    scatter_clean = node("GeometryNodeRemoveAttribute")
    scatter_clean.inputs["Name"].default_value = "swatch"
    link(scatter.outputs["Points"], scatter_clean.inputs["Geometry"])
    boxes = node("GeometryNodeInstanceOnPoints")
    link(scatter_clean.outputs["Geometry"], boxes.inputs["Points"])
    link(box_paint.outputs["Instances"], boxes.inputs["Instance"])
    boxes.inputs["Pick Instance"].default_value = True
    pick_shape = node("FunctionNodeRandomValue", data_type="INT")
    pick_shape.inputs["Min"].default_value = 0
    pick_shape.inputs["Max"].default_value = 1
    link(gin("Seed"), pick_shape.inputs["Seed"])
    link(pick_shape.outputs["Value"], boxes.inputs["Instance Index"])
    link(scatter.outputs["Rotation"], boxes.inputs["Rotation"])
    link(box_size.outputs["Value"], boxes.inputs["Scale"])
    boxes_real = node("GeometryNodeRealizeInstances")
    link(boxes.outputs["Instances"], boxes_real.inputs["Geometry"])

    join = node("GeometryNodeJoinGeometry")
    link(panelled, join.inputs["Geometry"])
    link(drop_normal.outputs["Geometry"], join.inputs["Geometry"])
    link(boxes_real.outputs["Geometry"], join.inputs["Geometry"])
    link(join.outputs["Geometry"], go.inputs["Geometry"])
    return group
