"""
Writes the CityForge Shader Graphs (URP Lit, one custom function each) into Assets/Chopper/Art/Shaders:
    TerrainBiome.shadergraph   TerrainBiome.hlsl: procedural terrain ground for every biome
    BuildingWear.shadergraph   BuildingWear.hlsl: palette colours graded with wear and grime
Run it again when a function's inputs or outputs change:

    python Tools/CityForge/make_city_graphs.py

Uses the JSON helpers of Tools/PartForge/make_skin_graph.py (the same Shader Graph file format, tested in the editor
with the ChopperSkin graph). Written without the editor (4 Nov 2026): open the graphs in Unity once and check them.
"""
import json
import os
import re
import sys
import uuid

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "PartForge"))
import make_skin_graph as sg  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
FOLDER = os.path.join(ROOT, "Assets", "Chopper", "Art", "Shaders")
OUTPUTS = [("Albedo", "vec3", "SurfaceDescription.BaseColor"), ("Metallic", "float", "SurfaceDescription.Metallic"),
           ("Smoothness", "float", "SurfaceDescription.Smoothness"), ("NormalOut", "vec3", "SurfaceDescription.NormalWS"),
           ("Emission", "vec3", "SurfaceDescription.Emission"), ("AmbientOcclusion", "float", "SurfaceDescription.Occlusion")]
KIND = {"float": "Vector1MaterialSlot", "vec2": "Vector2MaterialSlot", "vec3": "Vector3MaterialSlot",
        "vec4": "Vector4MaterialSlot"}


GRAPHS = {
    "TerrainBiome": dict(
        properties=[("_MaskMap", "Mask Map", "tex", 1), ("_SurfaceNormal", "Surface Normal", "tex", 2),
                    ("_MapSize", "Map Size", "float", 0.0),
                    ("_BiomeTable", "Biome Table", "tex", 1), ("_BiomeIndex", "Biome Index", "float", 0.0),
                    ("_RockColour", "Rock Colour", "tex", 0), ("_RockNormal", "Rock Normal", "normal", 3),
                    ("_SandColour", "Sand Colour", "tex", 0), ("_SandNormal", "Sand Normal", "normal", 3),
                    ("_MudColour", "Mud Colour", "tex", 0), ("_CoverColour", "Cover Colour", "tex", 0),
                    ("_DetailStrength", "Detail Strength", "float", 1.0)],
        inputs=[("PositionWS", "vec3", "position"), ("NormalWS", "vec3", "normal"),
                ("MaskMap", "tex", "_MaskMap"), ("SurfaceNormal", "tex", "_SurfaceNormal"), ("MapSize", "float", "_MapSize"),
                ("BiomeTable", "tex", "_BiomeTable"), ("BiomeIndex", "float", "_BiomeIndex"),
                ("RockColour", "tex", "_RockColour"), ("RockNormal", "tex", "_RockNormal"),
                ("SandColour", "tex", "_SandColour"), ("SandNormal", "tex", "_SandNormal"),
                ("MudColour", "tex", "_MudColour"), ("CoverColour", "tex", "_CoverColour"),
                ("DetailStrength", "float", "_DetailStrength")]),
    "BuildingWear": dict(
        properties=[("_PaletteColour", "Palette Colour", "tex", 0), ("_PaletteEmission", "Palette Emission", "tex", 1),
                    ("_PaletteMetalSmooth", "Palette Metal Smooth", "tex", 1), ("_BiomeTable", "Biome Table", "tex", 1),
                    ("_BiomeIndex", "Biome Index", "float", 0.0), ("_Wear", "Wear", "float", 1.0),
                    ("_EmissionStrength", "Emission Strength", "float", 4.0),
                    ("_ConcreteColour", "Concrete Colour", "tex", 0), ("_ConcreteNormal", "Concrete Normal", "normal", 3)],
        inputs=[("UV", "vec2", "uv"), ("PositionWS", "vec3", "position"), ("NormalWS", "vec3", "normal"),
                ("Masks", "vec4", "colour"), ("PaletteColour", "tex", "_PaletteColour"),
                ("PaletteEmission", "tex", "_PaletteEmission"), ("PaletteMetalSmooth", "tex", "_PaletteMetalSmooth"),
                ("BiomeTable", "tex", "_BiomeTable"), ("BiomeIndex", "float", "_BiomeIndex"), ("Wear", "float", "_Wear"),
                ("EmissionStrength", "float", "_EmissionStrength"), ("ConcreteColour", "tex", "_ConcreteColour"),
                ("ConcreteNormal", "tex", "_ConcreteNormal")]),
}


def main():
    for name, spec in GRAPHS.items():
        write_graph(name, spec["properties"], spec["inputs"])


def write_graph(name, PROPERTIES, INPUTS):
    HLSL_META = os.path.join(FOLDER, f"{name}.hlsl.meta")
    OUT = os.path.join(FOLDER, f"{name}.shadergraph")
    OUT_META = OUT + ".meta"
    sg.objects.clear()
    sg.edges.clear()
    with open(HLSL_META) as handle:
        hlsl_guid = re.search(r"guid: (\w+)", handle.read()).group(1)

    properties = {}
    for reference, display, kind, default in PROPERTIES:
        base = {"m_SGVersion": 0, "m_ObjectId": sg.new_id(), "m_Guid": {"m_GuidSerialized": str(uuid.uuid4())},
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
        else:
            base.update({"m_SGVersion": 1, "m_Type": "UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty",
                         "m_Value": default, "m_FloatType": 0, "m_RangeValues": {"x": 0.0, "y": 1.0},
                         "m_SliderType": 0, "m_SliderPower": 3.0, "m_EnumType": 0, "m_CSharpEnumString": "",
                         "m_EnumNames": ["Default"], "m_EnumValues": [0]})
        properties[reference] = (sg.add(base), kind, display)

    property_nodes = {}
    for index, (reference, (property_id, kind, display)) in enumerate(properties.items()):
        slot_kind = "Texture2DMaterialSlot" if kind in ("tex", "normal") else "Vector1MaterialSlot"
        extra = {"m_BareResource": False} if kind in ("tex", "normal") else {}
        out = sg.slot(slot_kind, 0, display, 1, output_name="Out", **extra)
        property_nodes[reference] = sg.node("PropertyNode", "Property", [out], -1100, -200 + index * 60,
                                            m_Property=sg.ref(property_id))

    position = sg.node("PositionNode", "Position", [sg.slot("Vector3MaterialSlot", 0, "Out", 1)], -1100, -560,
                       m_SGVersion=1, m_Space=2, m_PositionSource=0)            # world space
    normal = sg.node("NormalVectorNode", "Normal Vector", [sg.slot("Vector3MaterialSlot", 0, "Out", 1)], -1100, -440,
                     m_Space=2)                                                  # world space
    colour = sg.node("VertexColorNode", "Vertex Color", [sg.slot("Vector4MaterialSlot", 0, "Out", 1)], -1100, -320)
    uv = sg.node("UVNode", "UV", [sg.slot("Vector4MaterialSlot", 0, "Out", 1)], -1100, -260, m_OutputChannel=0)
    sources = {"position": position, "normal": normal, "colour": colour, "uv": uv}

    function_slots = []
    for slot_id, (slot_name, kind, _source) in enumerate(INPUTS):
        if kind == "tex":
            function_slots.append(sg.slot("Texture2DInputMaterialSlot", slot_id, slot_name, 0, m_BareResource=False,
                                          m_Texture={"m_SerializedTexture": "{\"texture\":{\"instanceID\":0}}",
                                                     "m_Guid": ""}, m_DefaultType=0))
        else:
            function_slots.append(sg.slot(KIND[kind], slot_id, slot_name, 0))
    for offset, (slot_name, kind, _block) in enumerate(OUTPUTS):
        function_slots.append(sg.slot(KIND[kind], len(INPUTS) + offset, slot_name, 1))
    function = sg.node("CustomFunctionNode", f"{name} (Custom Function)", function_slots, -600, -300,
                       m_SGVersion=1, synonyms=["code", "HLSL"], m_SourceType=0, m_FunctionName=name,
                       m_FunctionSource=hlsl_guid, m_FunctionBody="")
    for slot_id, (_name, _kind, source) in enumerate(INPUTS):
        sg.connect(sources.get(source) or property_nodes[source], 0, function, slot_id)

    def block(descriptor, slot_id):
        return sg.node("BlockNode", descriptor, [slot_id], 0, 0, m_SerializedDescriptor=descriptor,
                       m_DrawState={"m_Expanded": True, "m_Position": {"serializedVersion": "2", "x": 0.0, "y": 0.0,
                                                                       "width": 0.0, "height": 0.0}})

    zero = {"x": 0.0, "y": 0.0, "z": 0.0}
    vertex_blocks = [
        block("VertexDescription.Position", sg.slot("PositionMaterialSlot", 0, "Position", 0, 1, m_Value=zero, m_DefaultValue=zero, m_Labels=[], m_Space=0)),
        block("VertexDescription.Normal", sg.slot("NormalMaterialSlot", 0, "Normal", 0, 1, m_Value=zero, m_DefaultValue=zero, m_Labels=[], m_Space=0)),
        block("VertexDescription.Tangent", sg.slot("TangentMaterialSlot", 0, "Tangent", 0, 1, m_Value=zero, m_DefaultValue=zero, m_Labels=[], m_Space=0)),
    ]
    colour_slot = dict(m_Value={"x": 0.5, "y": 0.5, "z": 0.5}, m_DefaultValue={"x": 0.5, "y": 0.5, "z": 0.5}, m_Labels=[],
                       m_ColorMode=0, m_DefaultColor={"r": 0.5, "g": 0.5, "b": 0.5, "a": 1.0})
    emission_slot = dict(m_Value=zero, m_DefaultValue=zero, m_Labels=[], m_ColorMode=1,
                         m_DefaultColor={"r": 0.0, "g": 0.0, "b": 0.0, "a": 1.0})
    block_slots = {
        "SurfaceDescription.BaseColor": ("ColorRGBMaterialSlot", "Base Color", "BaseColor", colour_slot),
        "SurfaceDescription.NormalWS": ("NormalMaterialSlot", "Normal (World Space)", "NormalWS",
                                        dict(m_Value=zero, m_DefaultValue=zero, m_Labels=[], m_Space=2)),
        "SurfaceDescription.Metallic": ("Vector1MaterialSlot", "Metallic", "Metallic", {}),
        "SurfaceDescription.Smoothness": ("Vector1MaterialSlot", "Smoothness", "Smoothness", {}),
        "SurfaceDescription.Emission": ("ColorRGBMaterialSlot", "Emission", "Emission", emission_slot),
        "SurfaceDescription.Occlusion": ("Vector1MaterialSlot", "Ambient Occlusion", "Occlusion", {}),
    }
    fragment_blocks = []
    for offset, (_name, _kind, descriptor) in enumerate(OUTPUTS):
        kind, display, output_name, extra = block_slots[descriptor]
        block_id = block(descriptor, sg.slot(kind, 0, display, 0, 2, output_name=output_name, **extra))
        fragment_blocks.append(block_id)
        sg.connect(function, len(INPUTS) + offset, block_id, 0)
    alpha_slot = sg.slot("Vector1MaterialSlot", 0, "Alpha", 0, 2)
    for obj in sg.objects:
        if obj["m_ObjectId"] == alpha_slot:
            obj["m_Value"] = obj["m_DefaultValue"] = 1.0
    fragment_blocks.append(block("SurfaceDescription.Alpha", alpha_slot))

    sub_target = sg.add({"m_SGVersion": 2, "m_Type": "UnityEditor.Rendering.Universal.ShaderGraph.UniversalLitSubTarget",
                         "m_ObjectId": sg.new_id(), "m_WorkflowMode": 1, "m_NormalDropOffSpace": 2, "m_ClearCoat": False,
                         "m_BlendModePreserveSpecular": True})
    target = sg.add({"m_SGVersion": 1, "m_Type": "UnityEditor.Rendering.Universal.ShaderGraph.UniversalTarget",
                     "m_ObjectId": sg.new_id(), "m_Datas": [], "m_ActiveSubTarget": sg.ref(sub_target),
                     "m_AllowMaterialOverride": False, "m_SurfaceType": 0, "m_ZTestMode": 4, "m_ZWriteControl": 0,
                     "m_AlphaMode": 0, "m_RenderFace": 2, "m_AlphaClip": False, "m_CastShadows": True,
                     "m_ReceiveShadows": True, "m_DisableTint": False, "m_AdditionalMotionVectorMode": 0,
                     "m_AlembicMotionVectors": False, "m_SupportsLODCrossFade": False, "m_CustomEditorGUI": "",
                     "m_SupportVFX": False})
    category = sg.add({"m_SGVersion": 0, "m_Type": "UnityEditor.ShaderGraph.CategoryData", "m_ObjectId": sg.new_id(),
                       "m_Name": "", "m_ChildObjectList": [sg.ref(p[0]) for p in properties.values()]})
    graph = {"m_SGVersion": 3, "m_Type": "UnityEditor.ShaderGraph.GraphData", "m_ObjectId": sg.new_id(),
             "m_Properties": [sg.ref(p[0]) for p in properties.values()], "m_Keywords": [], "m_Dropdowns": [],
             "m_CategoryData": [sg.ref(category)],
             "m_Nodes": [sg.ref(o["m_ObjectId"]) for o in sg.objects if o["m_Type"].endswith("Node")],
             "m_GroupDatas": [], "m_StickyNoteDatas": [], "m_Edges": sg.edges,
             "m_VertexContext": {"m_Position": {"x": 0.0, "y": -600.0}, "m_Blocks": [sg.ref(b) for b in vertex_blocks]},
             "m_FragmentContext": {"m_Position": {"x": 0.0, "y": -300.0}, "m_Blocks": [sg.ref(b) for b in fragment_blocks]},
             "m_PreviewData": {"serializedMesh": {"m_SerializedMesh": "{\"mesh\":{\"instanceID\":0}}", "m_Guid": ""},
                               "preventRotation": False},
             "m_Path": "Shader Graphs", "m_GraphPrecision": 1, "m_PreviewMode": 2, "m_OutputNode": sg.ref(""),
             "m_SubDatas": [], "m_ActiveTargets": [sg.ref(target)]}
    with open(OUT, "w", newline="\n") as handle:
        handle.write(json.dumps(graph, indent=4) + "\n\n")
        for obj in sorted(sg.objects, key=lambda o: o["m_ObjectId"]):
            handle.write(json.dumps(obj, indent=4) + "\n\n")
    if not os.path.exists(OUT_META):
        # Same importer as ChopperSkin.shadergraph.meta, a new guid.
        with open(os.path.join(FOLDER, "ChopperSkin.shadergraph.meta")) as handle:
            meta = handle.read()
        with open(OUT_META, "w", newline="\n") as handle:
            handle.write(re.sub(r"guid: \w+", f"guid: {uuid.uuid4().hex}", meta, count=1))
    print(f"Wrote {OUT} ({len(sg.objects)} objects, {len(sg.edges)} edges)")


if __name__ == "__main__":
    main()
