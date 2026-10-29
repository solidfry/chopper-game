"""
Writes Assets/Chopper/Art/Shaders/ChopperSkin.shadergraph: a URP Lit Shader Graph around the ChopperSkin.hlsl custom
function (runtime skins). Run it again when the function's inputs or outputs change; Unity re-imports the file.

    python Tools/PartForge/make_skin_graph.py

The graph is plain JSON (Shader Graph's multi-object format). Property reference names match KitAssets.EnsureRuntime and
SkinLibrary.SkinIndexProperty; _SkinIndex is declared "Hybrid Per Instance" (Entities Graphics per-entity override).
"""
import json
import os
import re
import uuid

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
FOLDER = os.path.join(ROOT, "Assets", "Chopper", "Art", "Shaders")
HLSL_META = os.path.join(FOLDER, "ChopperSkin.hlsl.meta")
OUT = os.path.join(FOLDER, "ChopperSkin.shadergraph")

# (reference, display name, kind, default); kind: tex, normal, float, vec2
PROPERTIES = [
    ("_BaseColourMap", "Base Colour", "tex", 0),
    ("_BaseMetalSmooth", "Base Metal Smooth", "tex", 1),
    ("_BaseEmission", "Base Emission", "tex", 1),
    ("_NormalMap", "Normal Map", "normal", 3),
    ("_OcclusionMap", "Occlusion", "tex", 0),
    ("_SkinMask", "Skin Mask", "tex", 1),
    ("_SkinWear", "Skin Wear", "tex", 1),
    ("_SkinTable", "Skin Table", "tex", 1),
    ("_SkinIndex", "Skin Index", "float", 0.0),
    ("_HeightRange", "Height Range", "vec2", (0.0, 1.0)),
    ("_EmissionStrength", "Emission Strength", "float", 3.0),
]
# Custom function inputs, in order: (name, slot type, source)
INPUTS = [
    ("PositionOS", "vec3", "position"), ("NormalOS", "vec3", "normal"), ("UV", "vec2", "uv"),
    ("BaseColour", "tex", "_BaseColourMap"), ("BaseMetalSmooth", "tex", "_BaseMetalSmooth"),
    ("BaseEmission", "tex", "_BaseEmission"), ("NormalMap", "tex", "_NormalMap"), ("Occlusion", "tex", "_OcclusionMap"),
    ("SkinMask", "tex", "_SkinMask"), ("SkinWear", "tex", "_SkinWear"), ("SkinTable", "tex", "_SkinTable"),
    ("SkinIndex", "float", "_SkinIndex"), ("HeightRange", "vec2", "_HeightRange"),
    ("EmissionStrength", "float", "_EmissionStrength"),
]
# Outputs, in order: (name, slot type, fragment block)
OUTPUTS = [
    ("Albedo", "vec3", "SurfaceDescription.BaseColor"), ("Metallic", "float", "SurfaceDescription.Metallic"),
    ("Smoothness", "float", "SurfaceDescription.Smoothness"), ("NormalTS", "vec3", "SurfaceDescription.NormalTS"),
    ("Emission", "vec3", "SurfaceDescription.Emission"), ("AmbientOcclusion", "float", "SurfaceDescription.Occlusion"),
]

objects = []


def new_id():
    return uuid.uuid4().hex


def add(obj):
    objects.append(obj)
    return obj["m_ObjectId"]


def ref(object_id):
    return {"m_Id": object_id}


def draw(x, y, w=200, h=120):
    return {"m_Expanded": True, "m_Position": {"serializedVersion": "2", "x": x, "y": y, "width": w, "height": h}}


def node(kind, name, slots, x, y, **extra):
    data = {"m_SGVersion": 0, "m_Type": f"UnityEditor.ShaderGraph.{kind}", "m_ObjectId": new_id(), "m_Group": ref(""),
            "m_Name": name, "m_DrawState": draw(x, y), "m_Slots": [ref(s) for s in slots], "synonyms": [],
            "m_Precision": 0, "m_PreviewExpanded": False, "m_DismissedVersion": 0, "m_PreviewMode": 0,
            "m_CustomColors": {"m_SerializableColors": []}}
    data.update(extra)
    return add(data)


def slot(kind, slot_id, name, direction, stage=3, output_name=None, **extra):
    """direction: 0 input, 1 output."""
    defaults = {"Vector1MaterialSlot": 0.0, "Vector2MaterialSlot": {"x": 0.0, "y": 0.0},
                "Vector3MaterialSlot": {"x": 0.0, "y": 0.0, "z": 0.0},
                "Vector4MaterialSlot": {"x": 0.0, "y": 0.0, "z": 0.0, "w": 0.0}}
    data = {"m_SGVersion": 0, "m_Type": f"UnityEditor.ShaderGraph.{kind}", "m_ObjectId": new_id(), "m_Id": slot_id,
            "m_DisplayName": name, "m_SlotType": direction, "m_Hidden": False,
            "m_ShaderOutputName": output_name or name, "m_StageCapability": stage}
    if kind in defaults:
        data.update({"m_Value": defaults[kind], "m_DefaultValue": defaults[kind], "m_Labels": []})
    data.update(extra)
    return add(data)


SLOT_KIND = {"float": "Vector1MaterialSlot", "vec2": "Vector2MaterialSlot", "vec3": "Vector3MaterialSlot"}
edges = []


def connect(out_node, out_slot, in_node, in_slot):
    edges.append({"m_OutputSlot": {"m_Node": ref(out_node), "m_SlotId": out_slot},
                  "m_InputSlot": {"m_Node": ref(in_node), "m_SlotId": in_slot}})


def main():
    with open(HLSL_META) as handle:
        hlsl_guid = re.search(r"guid: (\w+)", handle.read()).group(1)

    # Properties.
    properties = {}
    for reference, display, kind, default in PROPERTIES:
        base = {"m_SGVersion": 0, "m_ObjectId": new_id(), "m_Guid": {"m_GuidSerialized": str(uuid.uuid4())},
                "m_Name": display, "m_DefaultRefNameVersion": 1, "m_RefNameGeneratedByDisplayName": display,
                "m_DefaultReferenceName": reference, "m_OverrideReferenceName": reference,
                "m_GeneratePropertyBlock": True, "m_UseCustomSlotLabel": False, "m_CustomSlotLabel": "",
                "m_DismissedVersion": 0, "m_Precision": 0, "overrideHLSLDeclaration": False,
                "hlslDeclarationOverride": 0, "m_Hidden": False}
        if kind in ("tex", "normal"):
            base.update({"m_Type": "UnityEditor.ShaderGraph.Internal.Texture2DShaderProperty",
                         "m_Value": {"m_SerializedTexture": "{\"texture\":{\"instanceID\":0}}", "m_Guid": ""},
                         "isMainTexture": False, "useTilingAndOffset": False, "m_Modifiable": True,
                         "m_DefaultType": default})
        elif kind == "float":
            base.update({"m_SGVersion": 1, "m_Type": "UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty",
                         "m_Value": default, "m_FloatType": 0, "m_RangeValues": {"x": 0.0, "y": 1.0},
                         "m_SliderType": 0, "m_SliderPower": 3.0, "m_EnumType": 0, "m_CSharpEnumString": "",
                         "m_EnumNames": ["Default"], "m_EnumValues": [0]})
            if reference == "_SkinIndex":
                # One value per entity (Entities Graphics) or per renderer (MaterialPropertyBlock).
                base.update({"overrideHLSLDeclaration": True, "hlslDeclarationOverride": 3})
        else:
            base.update({"m_SGVersion": 1, "m_Type": "UnityEditor.ShaderGraph.Internal.Vector2ShaderProperty",
                         "m_Value": {"x": default[0], "y": default[1], "z": 0.0, "w": 0.0}})
        properties[reference] = (add(base), kind, display)

    # Property nodes.
    property_nodes = {}
    for index, (reference, (property_id, kind, display)) in enumerate(properties.items()):
        slot_kind = {"tex": "Texture2DMaterialSlot", "normal": "Texture2DMaterialSlot", "float": "Vector1MaterialSlot",
                     "vec2": "Vector2MaterialSlot"}[kind]
        extra = {"m_BareResource": False} if slot_kind == "Texture2DMaterialSlot" else {}
        out = slot(slot_kind, 0, display, 1, output_name="Out", **extra)
        property_nodes[reference] = node("PropertyNode", "Property", [out], -1100, -300 + index * 60,
                                         m_Property=ref(property_id))

    # Geometry nodes.
    position_slot = slot("Vector3MaterialSlot", 0, "Out", 1)
    position = node("PositionNode", "Position", [position_slot], -1100, -560, m_SGVersion=1, m_Space=0,
                    m_PositionSource=0)
    normal_slot = slot("Vector3MaterialSlot", 0, "Out", 1)
    normal = node("NormalVectorNode", "Normal Vector", [normal_slot], -1100, -440, m_Space=0)
    uv_slot = slot("Vector4MaterialSlot", 0, "Out", 1)
    uv = node("UVNode", "UV", [uv_slot], -1100, -340, m_OutputChannel=0)
    sources = {"position": position, "normal": normal, "uv": uv}

    # Custom function.
    function_slots = []
    for slot_id, (name, kind, _source) in enumerate(INPUTS):
        if kind == "tex":
            function_slots.append(slot("Texture2DInputMaterialSlot", slot_id, name, 0, m_BareResource=False,
                                       m_Texture={"m_SerializedTexture": "{\"texture\":{\"instanceID\":0}}",
                                                  "m_Guid": ""}, m_DefaultType=0))
        else:
            function_slots.append(slot(SLOT_KIND[kind], slot_id, name, 0))
    for offset, (name, kind, _block) in enumerate(OUTPUTS):
        function_slots.append(slot(SLOT_KIND[kind], len(INPUTS) + offset, name, 1))
    function = node("CustomFunctionNode", "ChopperSkin (Custom Function)", function_slots, -600, -300,
                    m_SGVersion=1, synonyms=["code", "HLSL"], m_SourceType=0, m_FunctionName="ChopperSkin",
                    m_FunctionSource=hlsl_guid, m_FunctionBody="")
    for slot_id, (_name, _kind, source) in enumerate(INPUTS):
        connect(sources.get(source) or property_nodes[source], 0, function, slot_id)

    # Output blocks.
    def block(descriptor, slot_id):
        return node("BlockNode", descriptor, [slot_id], 0, 0, m_SerializedDescriptor=descriptor,
                    m_DrawState={"m_Expanded": True, "m_Position": {"serializedVersion": "2", "x": 0.0, "y": 0.0,
                                                                    "width": 0.0, "height": 0.0}})

    vertex_blocks = [
        block("VertexDescription.Position", slot("PositionMaterialSlot", 0, "Position", 0, 1, m_Value={"x": 0.0, "y": 0.0, "z": 0.0}, m_DefaultValue={"x": 0.0, "y": 0.0, "z": 0.0}, m_Labels=[], m_Space=0)),
        block("VertexDescription.Normal", slot("NormalMaterialSlot", 0, "Normal", 0, 1, m_Value={"x": 0.0, "y": 0.0, "z": 0.0}, m_DefaultValue={"x": 0.0, "y": 0.0, "z": 0.0}, m_Labels=[], m_Space=0)),
        block("VertexDescription.Tangent", slot("TangentMaterialSlot", 0, "Tangent", 0, 1, m_Value={"x": 0.0, "y": 0.0, "z": 0.0}, m_DefaultValue={"x": 0.0, "y": 0.0, "z": 0.0}, m_Labels=[], m_Space=0)),
    ]
    colour_slot = dict(m_Value={"x": 0.5, "y": 0.5, "z": 0.5}, m_DefaultValue={"x": 0.5, "y": 0.5, "z": 0.5}, m_Labels=[],
                       m_ColorMode=0, m_DefaultColor={"r": 0.5, "g": 0.5, "b": 0.5, "a": 1.0})
    emission_slot = dict(m_Value={"x": 0.0, "y": 0.0, "z": 0.0}, m_DefaultValue={"x": 0.0, "y": 0.0, "z": 0.0}, m_Labels=[],
                         m_ColorMode=1, m_DefaultColor={"r": 0.0, "g": 0.0, "b": 0.0, "a": 1.0})
    block_slots = {
        "SurfaceDescription.BaseColor": ("ColorRGBMaterialSlot", "Base Color", "BaseColor", colour_slot),
        "SurfaceDescription.NormalTS": ("NormalMaterialSlot", "Normal (Tangent Space)", "NormalTS",
                                        dict(m_Value={"x": 0.0, "y": 0.0, "z": 0.0}, m_DefaultValue={"x": 0.0, "y": 0.0, "z": 0.0}, m_Labels=[], m_Space=3)),
        "SurfaceDescription.Metallic": ("Vector1MaterialSlot", "Metallic", "Metallic", {}),
        "SurfaceDescription.Smoothness": ("Vector1MaterialSlot", "Smoothness", "Smoothness", {}),
        "SurfaceDescription.Emission": ("ColorRGBMaterialSlot", "Emission", "Emission", emission_slot),
        "SurfaceDescription.Occlusion": ("Vector1MaterialSlot", "Ambient Occlusion", "Occlusion", {}),
    }
    fragment_blocks = []
    for offset, (_name, _kind, descriptor) in enumerate(OUTPUTS):
        kind, display, output_name, extra = block_slots[descriptor]
        block_id = block(descriptor, slot(kind, 0, display, 0, 2, output_name=output_name, **extra))
        fragment_blocks.append(block_id)
        connect(function, len(INPUTS) + offset, block_id, 0)
    alpha = block("SurfaceDescription.Alpha", slot("Vector1MaterialSlot", 0, "Alpha", 0, 2))
    for obj in objects:
        if obj["m_ObjectId"] in [s["m_Id"] for o in objects if o["m_ObjectId"] == alpha for s in o["m_Slots"]]:
            obj["m_Value"] = obj["m_DefaultValue"] = 1.0
    fragment_blocks.append(alpha)

    # Target: URP Lit, metallic workflow, opaque.
    sub_target = add({"m_SGVersion": 2, "m_Type": "UnityEditor.Rendering.Universal.ShaderGraph.UniversalLitSubTarget",
                      "m_ObjectId": new_id(), "m_WorkflowMode": 1, "m_NormalDropOffSpace": 0, "m_ClearCoat": False,
                      "m_BlendModePreserveSpecular": True})
    target = add({"m_SGVersion": 1, "m_Type": "UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget",
                  "m_ObjectId": new_id(), "m_Datas": [], "m_ActiveSubTarget": ref(sub_target),
                  "m_AllowMaterialOverride": False, "m_SurfaceType": 0, "m_ZTestMode": 4, "m_ZWriteControl": 0,
                  "m_AlphaMode": 0, "m_RenderFace": 2, "m_AlphaClip": False, "m_CastShadows": True,
                  "m_ReceiveShadows": True, "m_DisableTint": False, "m_AdditionalMotionVectorMode": 0,
                  "m_AlembicMotionVectors": False, "m_SupportsLODCrossFade": False, "m_CustomEditorGUI": "",
                  "m_SupportVFX": False})
    category = add({"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.CategoryData", "m_ObjectId": new_id(),
                    "m_Name": "", "m_ChildObjectList": [ref(p[0]) for p in properties.values()]})

    block_ids = set(vertex_blocks + fragment_blocks)
    graph = {"m_SGVersion": 3, "m_Type": "UnityEditor.ShaderGraph.GraphData", "m_ObjectId": new_id(),
             "m_Properties": [ref(p[0]) for p in properties.values()], "m_Keywords": [], "m_Dropdowns": [],
             "m_CategoryData": [ref(category)],
             "m_Nodes": [ref(o["m_ObjectId"]) for o in objects if o["m_Type"].endswith("Node")],
             "m_GroupDatas": [], "m_StickyNoteDatas": [], "m_Edges": edges,
             "m_VertexContext": {"m_Position": {"x": 0.0, "y": -600.0}, "m_Blocks": [ref(b) for b in vertex_blocks]},
             "m_FragmentContext": {"m_Position": {"x": 0.0, "y": -300.0}, "m_Blocks": [ref(b) for b in fragment_blocks]},
             "m_PreviewData": {"serializedMesh": {"m_SerializedMesh": "{\"mesh\":{\"instanceID\":0}}", "m_Guid": ""},
                               "preventRotation": False},
             "m_Path": "Shader Graphs", "m_GraphPrecision": 1, "m_PreviewMode": 2, "m_OutputNode": ref(""),
             "m_SubDatas": [], "m_ActiveTargets": [ref(target)]}
    assert all(b in block_ids for b in block_ids)
    with open(OUT, "w", newline="\n") as handle:
        handle.write(json.dumps(graph, indent=4) + "\n\n")
        for obj in sorted(objects, key=lambda o: o["m_ObjectId"]):
            handle.write(json.dumps(obj, indent=4) + "\n\n")
    print(f"Wrote {OUT} ({len(objects)} objects, {len(edges)} edges)")


if __name__ == "__main__":
    main()
