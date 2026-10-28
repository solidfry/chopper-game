"""
The PartForge panel: 3D View > Sidebar (N) > PartForge. Every style value and every chassis shape is a slider or a menu;
the buttons run the generation steps in build.py on the open file. Settings live in the scene, so they save with the
.blend. "Save style" writes Tools/PartForge/styles/<name>.json, which the command line can use too (--style <name>).
"""
import os
import random

import bpy
from bpy.props import (BoolProperty, CollectionProperty, EnumProperty, FloatProperty, FloatVectorProperty, IntProperty,
                       PointerProperty, StringProperty)
from bpy.types import Operator, Panel, PropertyGroup

from . import build, detail, geo, palette, skins, styles

TOOL_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))       # Tools/PartForge
REPO_DIR = os.path.normpath(os.path.join(TOOL_DIR, "..", ".."))
GLOWS = [(name, name.replace("_", " ").title(), "") for name in
         ("sodium", "warning", "neon_green", "neon_cyan", "neon_pink", "exhaust")]
PAINT = ("hull", "hull_dark", "hull_light")

_style_items = []   # Blender needs the dynamic enum list kept alive
_skin_items = []
_rebuild_pending = False
_exported = None    # build.State of the last export in this session (its parts carry the baked textures)


# -- auto rebuild ---------------------------------------------------------------------------------------------------

def _rebuild_later():
    global _rebuild_pending
    _rebuild_pending = False
    scene = bpy.context.scene
    if scene is not None and hasattr(scene, "partforge"):
        rebuild(scene.partforge)
    return None


def _changed(self, context):
    """Any slider: rebuild shortly after the last change (only with Auto rebuild on)."""
    global _rebuild_pending
    settings = context.scene.partforge
    if not settings.auto_rebuild or settings.loading:
        return
    if _rebuild_pending and bpy.app.timers.is_registered(_rebuild_later):
        bpy.app.timers.unregister(_rebuild_later)
    _rebuild_pending = True
    bpy.app.timers.register(_rebuild_later, first_interval=0.4)


# -- settings -------------------------------------------------------------------------------------------------------

def _styles(self, context):
    _style_items.clear()
    _style_items.extend((name, name, "") for name in styles.names())
    return _style_items


def _skins(self, context):
    _skin_items.clear()
    _skin_items.extend((skin["key"], skin["name"], skin.get("description", "")) for skin in skins.SKINS)
    return _skin_items


def _exported_state():
    """The last export's state while its parts still exist (a rebuild replaces them)."""
    if _exported is None:
        return None
    try:
        for _part, obj in _exported.built.values():
            _ = obj.name
    except ReferenceError:
        return None
    return _exported


class PartForgeChassis(PropertyGroup):
    body: EnumProperty(name="Body", items=[("utility", "Utility", "One hull, canopy on top, skids"),
                                          ("attack", "Attack", "Chunky slab core, tandem cockpit, cheek pods, wheels"),
                                          ("assault", "Assault", "Blocks that taper to the top, beastly grille snout"),
                                          ("heavy", "Heavy", "Heavy lifter: cargo hull, sponsons, big engine pods; "
                                                             "single, tandem, coaxial or quad rotors")],
                       update=_changed)
    seed: IntProperty(name="Seed", min=0, update=_changed, description="Details picked by chance (plates, antennas, add-ons)")
    width: FloatProperty(name="Width", min=0.8, max=3.5, step=5, update=_changed)
    height: FloatProperty(name="Height", min=0.8, max=3.5, step=5, update=_changed)
    front: FloatProperty(name="Nose length", min=2.0, max=6.0, step=5, update=_changed,
                         description="Nose tip, metres ahead of the centre of mass")
    boom: FloatProperty(name="Boom length", min=2.0, max=8.0, step=5, update=_changed)
    wings: BoolProperty(name="Stub wings", update=_changed)
    wing_span: FloatProperty(name="Wing span", min=0.6, max=3.0, step=5, update=_changed)
    engines: IntProperty(name="Engines", min=1, max=2, update=_changed)
    rotor_layout: EnumProperty(name="Rotors", update=_changed, items=[
        ("single", "Single", "One main rotor, tail boom with tail rotor"),
        ("tandem", "Tandem", "Front and rear rotor towers, no tail rotor"),
        ("coaxial", "Coaxial", "Two stacked rotors and a pusher prop at the tail"),
        ("quad", "Quad", "Four ducted fans on outrigger arms")])
    sensor: BoolProperty(name="Sensor ball", update=_changed)
    snout: EnumProperty(name="Snout", update=_changed, items=[
        ("sensor", "Sensor turret", "Boxy nose sensor with two lenses"),
        ("grille", "Grille", "Beastly snout with a Space Marine-style grille")])
    taper: FloatProperty(name="Top taper", min=0.6, max=1.0, default=1.0, update=_changed,
                         description="Attack body: every section narrows more towards its top")
    armour: FloatProperty(name="Armour plates", min=0.0, max=1.5, update=_changed)
    skids: BoolProperty(name="Skids", default=True, update=_changed)
    slots: StringProperty()  # game slot order, comma separated (fixed by the game, not edited here)


class PartForgeSettings(PropertyGroup):
    loading: BoolProperty(default=False, options={"HIDDEN"})
    style_name: EnumProperty(name="Style", items=_styles)
    kit: StringProperty(name="Kit", default="Kit02", description="Output folder Assets/Chopper/Art/Kits/<kit>")
    new_style_name: StringProperty(name="Name", default="my_style")
    auto_rebuild: BoolProperty(name="Auto rebuild", default=False,
                               description="Rebuild a moment after each change (a few seconds per rebuild)")
    samples: IntProperty(name="Samples", default=32, min=4, max=512)
    skin: EnumProperty(name="Skin", items=_skins, description="Skin to show (after an export: it bakes the textures)")

    # Shape language
    facet: FloatProperty(name="Facet", min=0.5, max=2.5, update=_changed, description="Chamfer on every section")
    top_taper: FloatProperty(name="Top taper", min=0.4, max=1.0, update=_changed, description="Lower = trapezoid sides")
    nose: EnumProperty(name="Nose", items=[("round", "Round", ""), ("chisel", "Chisel", "")], update=_changed)
    canopy: EnumProperty(name="Canopy", items=[("bubble", "Glass", ""), ("visor", "Armoured visor", "")], update=_changed)
    boom: EnumProperty(name="Boom", items=[("tube", "Tube", ""), ("slab", "Slab", "")], update=_changed)
    # Add-ons
    cage: FloatProperty(name="Cage armour", min=0.0, max=1.0, update=_changed)
    conduits: FloatProperty(name="Pipes", min=0.0, max=1.0, update=_changed)
    asymmetry: FloatProperty(name="Retrofits", min=0.0, max=1.0, update=_changed)
    armour_layers: IntProperty(name="Armour layers", min=1, max=2, update=_changed)
    antennas_min: IntProperty(name="Antennas min", min=0, max=8, update=_changed)
    antennas_max: IntProperty(name="Antennas max", min=0, max=8, update=_changed)
    # Wear and light
    wear: FloatProperty(name="Wear", min=0.0, max=1.0, update=_changed)
    hazard_faded: BoolProperty(name="Faded warning paint", update=_changed)
    neon_strips: BoolProperty(name="Neon side strips", update=_changed)
    light_nose: EnumProperty(name="Nose light", items=GLOWS, update=_changed)
    light_right: EnumProperty(name="Right light", items=GLOWS, update=_changed)
    light_left: EnumProperty(name="Left light", items=GLOWS, update=_changed)
    light_sensor: EnumProperty(name="Sensor light", items=GLOWS, update=_changed)
    accent_green: EnumProperty(name="Green glow becomes", items=GLOWS, update=_changed)
    accent_pink: EnumProperty(name="Pink glow becomes", items=GLOWS, update=_changed)
    # Paint
    hull: FloatVectorProperty(name="Hull", subtype="COLOR_GAMMA", size=3, min=0, max=1, update=_changed)
    hull_dark: FloatVectorProperty(name="Hull dark", subtype="COLOR_GAMMA", size=3, min=0, max=1, update=_changed)
    hull_light: FloatVectorProperty(name="Hull light", subtype="COLOR_GAMMA", size=3, min=0, max=1, update=_changed)
    paint_metallic: FloatProperty(name="Paint metallic", min=0.0, max=1.0, update=_changed)
    paint_smoothness: FloatProperty(name="Paint smoothness", min=0.0, max=1.0, update=_changed)
    other_palette: StringProperty(options={"HIDDEN"})  # the loaded style's other swatch overrides (JSON)
    other_keys: StringProperty(options={"HIDDEN"})  # the loaded style's skins, textures and bump values (JSON)
    # Form: how soft the shapes are (geo.DEFAULT_FORM)
    form_corner_segments: IntProperty(name="Corner rounding", min=1, max=6, update=_changed,
                                      description="Arc segments per section corner: 1 = straight cut, 3+ = round")
    form_loft_smooth: IntProperty(name="Curve smoothing", min=0, max=3, update=_changed,
                                  description="Extra sections between sections: curved silhouettes instead of kinks")
    form_curve_detail: FloatProperty(name="Cylinder detail", min=1.0, max=2.5, update=_changed)
    form_edge_radius: FloatProperty(name="Edge radius", min=0.0, max=0.15, step=0.1, precision=3, update=_changed)
    form_edge_segments: IntProperty(name="Edge segments", min=1, max=4, update=_changed,
                                    description="1 = soft-shaded chamfer (cheap); 2-4 = real fillets (more triangles)")
    form_edge_profile: FloatProperty(name="Edge profile", min=0.2, max=1.0, update=_changed)
    form_box_rounding: FloatProperty(name="Box rounding", min=0.0, max=0.5, update=_changed,
                                     description="Boxes get rounded edges and corners (share of their smallest side)")
    form_sharp_angle: FloatProperty(name="Sharp edges from", min=10.0, max=80.0, update=_changed,
                                    description="Edges sharper than this angle (°) shade hard; flat faces shade flat")
    form_box_min: FloatProperty(name="Round boxes from", min=0.0, max=0.5, step=1, precision=2, update=_changed,
                                description="Boxes thinner than this (m) stay square: slats, vents, bars")
    # Surface detail (Geometry Nodes "PF Surface Detail", chassis only)
    detail_on: BoolProperty(name="Surface detail", default=True, update=_changed)
    detail_panels: FloatProperty(name="Panels", min=0.0, max=1.0, update=_changed,
                                 description="Share of large hull faces that become inset panels")
    detail_min_area: FloatProperty(name="Min face area", min=0.0, max=2.0, update=_changed,
                                   description="Smaller faces (m²) get no panels or greebles")
    detail_inset: FloatProperty(name="Panel size", min=0.5, max=0.98, update=_changed)
    detail_depth: FloatProperty(name="Panel depth", min=0.0, max=0.1, step=0.1, precision=3, update=_changed)
    detail_recolour: FloatProperty(name="Two-tone panels", min=0.0, max=1.0, update=_changed)
    detail_bolt_size: FloatProperty(name="Nut size", min=0.0, max=0.12, step=0.1, precision=3, update=_changed,
                                    description="Hex nuts at panel corners; 0 = none")
    detail_greebles: FloatProperty(name="Greebles", min=0.0, max=4.0, update=_changed,
                                   description="Small boxes per m² on large hull faces")
    detail_greeble_size: FloatProperty(name="Greeble size", min=0.02, max=0.6, update=_changed)
    # Chassis
    chassis: CollectionProperty(type=PartForgeChassis)
    chassis_index: IntProperty(name="Chassis")


# -- style <-> settings ---------------------------------------------------------------------------------------------

def _hex(colour):
    return "#" + "".join(f"{round(max(0.0, min(1.0, c)) * 255):02x}" for c in colour)


def _rgb(value):
    value = value.lstrip("#")
    return tuple(int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def load_style(settings, style, name, kit=None):
    """Copies a style (and its chassis, or the defaults) into the panel."""
    import json
    settings.loading = True
    try:
        settings.style_name = name
        settings.kit = kit or style.get("kit", "Kit02")
        settings.new_style_name = name if name not in styles.BUILT_IN else f"{name}_custom"
        for key in ("facet", "top_taper", "nose", "canopy", "boom", "cage", "conduits", "asymmetry", "wear",
                    "armour_layers", "neon_strips"):
            setattr(settings, key, style[key])
        settings.antennas_min, settings.antennas_max = style["antennas"]
        lights = style["lights"]
        settings.light_nose, settings.light_right = lights["nose"], lights["right"]
        settings.light_left, settings.light_sensor = lights["left"], lights["sensor"]
        settings.hazard_faded = style.get("hazard") == "hazard_faded"
        accents = style.get("accents", {})
        settings.accent_green = accents.get("neon_green", "neon_green")
        settings.accent_pink = accents.get("neon_pink", "neon_pink")

        overrides = dict(style.get("palette", {}))
        base = {name_: (colour, metal, smooth) for name_, colour, _glow, metal, smooth in palette.SWATCHES}
        for key in PAINT:
            entry = list(overrides.pop(key, ()))
            colour, metal, smooth = base[key]
            setattr(settings, key, _rgb(entry[0] if entry else colour))
            if key == "hull":
                settings.paint_metallic = entry[2] if len(entry) > 2 else metal
                settings.paint_smoothness = entry[3] if len(entry) > 3 else smooth
        settings.other_palette = json.dumps(overrides)
        settings.other_keys = json.dumps({key: style[key] for key in ("skins", "textures", "bump") if key in style})

        form = dict(geo.DEFAULT_FORM, **(style.get("form") or {}))
        for key in geo.DEFAULT_FORM:
            setattr(settings, f"form_{key}", form[key])

        values = style.get("detail")
        settings.detail_on = bool(values)
        values = dict(detail.DEFAULTS, **(values or {}))
        for key in detail.DETAIL_KEYS:
            setattr(settings, f"detail_{key}", values[key])

        settings.chassis.clear()
        for spec in build.chassis_specs(style):
            item = settings.chassis.add()
            item.name = spec["name"]
            item.body = spec.get("body", "utility")
            item.seed = spec["seed"]
            item.width, item.height, item.front, item.boom = spec["width"], spec["height"], spec["front"], spec["boom"]
            item.wings = spec.get("wings", False)
            item.wing_span = spec.get("wing_span", 1.4)
            item.engines = spec.get("engines", 2)
            item.rotor_layout = spec.get("rotor_layout", "single")
            item.snout = spec.get("snout", "sensor")
            item.taper = spec.get("taper", 1.0)
            item.sensor = spec.get("sensor", False)
            item.armour = spec.get("armour", 0.5)
            item.skids = spec.get("skids", True)
            item.slots = ",".join(spec["slots"])
        settings.chassis_index = min(settings.chassis_index, max(len(settings.chassis) - 1, 0))
    finally:
        settings.loading = False


def style_from(settings):
    """The panel as a style dict (same keys as styles.py), with its chassis."""
    import json
    paint = {key: (_hex(getattr(settings, key)), None, settings.paint_metallic, settings.paint_smoothness) for key in PAINT}
    style = dict(
        name=settings.new_style_name or settings.style_name, kit=settings.kit,
        facet=settings.facet, top_taper=settings.top_taper, nose=settings.nose, canopy=settings.canopy, boom=settings.boom,
        cage=settings.cage, conduits=settings.conduits, asymmetry=settings.asymmetry, wear=settings.wear,
        armour_layers=settings.armour_layers,
        antennas=[min(settings.antennas_min, settings.antennas_max), max(settings.antennas_min, settings.antennas_max)],
        neon_strips=settings.neon_strips,
        lights={"nose": settings.light_nose, "right": settings.light_right, "left": settings.light_left,
                "sensor": settings.light_sensor},
        hazard="hazard_faded" if settings.hazard_faded else "hazard",
        accents={"neon_green": settings.accent_green, "neon_pink": settings.accent_pink},
        palette=dict(json.loads(settings.other_palette or "{}"), **paint),
        detail={key: getattr(settings, f"detail_{key}") for key in detail.DETAIL_KEYS} if settings.detail_on else {},
        form={key: getattr(settings, f"form_{key}") for key in geo.DEFAULT_FORM},
    )
    style.update(json.loads(settings.other_keys or "{}"))
    style["chassis"] = [dict(
        name=item.name, body=item.body, seed=item.seed, width=item.width, height=item.height, front=item.front,
        boom=item.boom, wings=item.wings, wing_span=item.wing_span, engines=item.engines, rotor_layout=item.rotor_layout, snout=item.snout, taper=item.taper,
        sensor=item.sensor,
        armour=item.armour, skids=item.skids, slots=item.slots.split(",")) for item in settings.chassis]
    return style


def rebuild(settings):
    return build.generate(style_from(settings), settings.kit)


# -- operators ------------------------------------------------------------------------------------------------------

class PARTFORGE_OT_load_style(Operator):
    bl_idname = "partforge.load_style"
    bl_label = "Load style"
    bl_description = "Put the chosen style's values (and its chassis shapes) into the panel, then rebuild"

    def execute(self, context):
        settings = context.scene.partforge
        load_style(settings, styles.get(settings.style_name), settings.style_name)
        rebuild(settings)
        return {"FINISHED"}


class PARTFORGE_OT_rebuild(Operator):
    bl_idname = "partforge.rebuild"
    bl_label = "Rebuild"
    bl_description = "Build every part again from the panel values (in this file only; nothing is exported)"

    def execute(self, context):
        settings = context.scene.partforge
        if len(settings.chassis) == 0:
            load_style(settings, styles.get(settings.style_name), settings.style_name)
        rebuild(settings)
        return {"FINISHED"}


class PARTFORGE_OT_export(Operator):
    bl_idname = "partforge.export"
    bl_label = "Export kit to Unity"
    bl_description = ("Rebuild, bake the texture sets in every skin (a few minutes), then write FBX models, textures "
                      "and the manifest to Assets/Chopper/Art/Kits/<kit>. The file then shows the baked textures")

    def execute(self, context):
        global _exported
        settings = context.scene.partforge
        out_dir = os.path.join(REPO_DIR, "Assets", "Chopper", "Art", "Kits", settings.kit)
        state = rebuild(settings)
        manifest = build.export(state, out_dir)
        _exported = state
        self.report({"INFO"}, f"Exported {len(manifest['parts'])} parts to {out_dir}. In Unity: Chopper > Setup > 5.")
        return {"FINISHED"}


class PARTFORGE_OT_render(Operator):
    bl_idname = "partforge.render"
    bl_label = "Render previews"
    bl_description = ("Render the line-up, close-ups, four-view sheets and module sheet (Blender is busy for some "
                      "minutes). Right after an export: with the baked textures and a skin sheet; else rebuilds first "
                      "and shows the palette look")

    def execute(self, context):
        settings = context.scene.partforge
        preview_dir = os.path.join(TOOL_DIR, "previews", settings.kit)
        build.render_previews(_exported_state() or rebuild(settings), preview_dir, settings.samples)
        self.report({"INFO"}, f"Previews in {preview_dir}")
        return {"FINISHED"}


class PARTFORGE_OT_show_skin(Operator):
    bl_idname = "partforge.show_skin"
    bl_label = "Show skin"
    bl_description = "Put the chosen skin on every part (after an export in this session)"

    def execute(self, context):
        state = _exported_state()
        if state is None:
            self.report({"ERROR"}, "Export first (it bakes the textures); a rebuild drops them.")
            return {"CANCELLED"}
        key = context.scene.partforge.skin
        if key not in [skin["key"] for skin in state.skins]:
            self.report({"ERROR"}, f"This kit's style does not export the skin '{key}' (style key \"skins\").")
            return {"CANCELLED"}
        build.show_skin(state, key)
        return {"FINISHED"}


class PARTFORGE_OT_save_style(Operator):
    bl_idname = "partforge.save_style"
    bl_label = "Save style"
    bl_description = "Write the panel values as Tools/PartForge/styles/<name>.json (a new entry in the Style menu)"

    def execute(self, context):
        settings = context.scene.partforge
        name = settings.new_style_name.strip()
        if not name or name in styles.BUILT_IN:
            self.report({"ERROR"}, "Pick a new name (built-in styles stay as they are).")
            return {"CANCELLED"}
        style = style_from(settings)
        chassis = style.pop("chassis")
        path = styles.save(name, style, chassis)
        settings.style_name = name
        self.report({"INFO"}, f"Saved {path}")
        return {"FINISHED"}


class PARTFORGE_OT_reset_detail(Operator):
    bl_idname = "partforge.reset_detail"
    bl_label = "Reset detail nodes"
    bl_description = "Make the PF Surface Detail node group again from PartForge (drops your node edits), then rebuild"

    def execute(self, context):
        detail.reset()
        rebuild(context.scene.partforge)
        return {"FINISHED"}


class PARTFORGE_OT_reseed(Operator):
    bl_idname = "partforge.reseed"
    bl_label = "New seed"
    bl_description = "New chance details for the selected chassis"

    def execute(self, context):
        settings = context.scene.partforge
        if 0 <= settings.chassis_index < len(settings.chassis):
            settings.chassis[settings.chassis_index].seed = random.randint(0, 9999)
        if not settings.auto_rebuild:
            rebuild(settings)
        return {"FINISHED"}


# -- panels ---------------------------------------------------------------------------------------------------------

class _Base:
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "PartForge"


class PARTFORGE_PT_main(_Base, Panel):
    bl_label = "PartForge"

    def draw(self, context):
        settings = context.scene.partforge
        layout = self.layout
        row = layout.row(align=True)
        row.prop(settings, "style_name", text="")
        row.operator("partforge.load_style", text="Load", icon="IMPORT")
        layout.prop(settings, "kit")
        row = layout.row(align=True)
        row.prop(settings, "auto_rebuild", toggle=True, icon="FILE_REFRESH")
        row.operator("partforge.rebuild", icon="MODIFIER")
        layout.label(text="Material Preview (Z) shows the paint and glow.", icon="INFO")


class PARTFORGE_PT_shape(_Base, Panel):
    bl_label = "Shape"
    bl_parent_id = "PARTFORGE_PT_main"

    def draw(self, context):
        settings = context.scene.partforge
        column = self.layout.column()
        for key in ("facet", "top_taper", "nose", "canopy", "boom"):
            column.prop(settings, key)


class PARTFORGE_PT_addons(_Base, Panel):
    bl_label = "Add-ons"
    bl_parent_id = "PARTFORGE_PT_main"

    def draw(self, context):
        settings = context.scene.partforge
        column = self.layout.column()
        for key in ("cage", "conduits", "asymmetry", "armour_layers"):
            column.prop(settings, key)
        row = column.row(align=True)
        row.prop(settings, "antennas_min", text="Antennas")
        row.prop(settings, "antennas_max", text="to")


class PARTFORGE_PT_wear(_Base, Panel):
    bl_label = "Wear and lights"
    bl_parent_id = "PARTFORGE_PT_main"

    def draw(self, context):
        settings = context.scene.partforge
        column = self.layout.column()
        for key in ("wear", "hazard_faded", "neon_strips", "light_nose", "light_right", "light_left", "light_sensor",
                    "accent_green", "accent_pink"):
            column.prop(settings, key)


class PARTFORGE_PT_paint(_Base, Panel):
    bl_label = "Paint"
    bl_parent_id = "PARTFORGE_PT_main"

    def draw(self, context):
        settings = context.scene.partforge
        column = self.layout.column()
        for key in PAINT + ("paint_metallic", "paint_smoothness"):
            column.prop(settings, key)


class PARTFORGE_PT_form(_Base, Panel):
    bl_label = "Form"
    bl_parent_id = "PARTFORGE_PT_main"

    def draw(self, context):
        settings = context.scene.partforge
        column = self.layout.column()
        for key in geo.DEFAULT_FORM:
            column.prop(settings, f"form_{key}")


class PARTFORGE_PT_detail(_Base, Panel):
    bl_label = "Surface detail"
    bl_parent_id = "PARTFORGE_PT_main"

    def draw_header(self, context):
        self.layout.prop(context.scene.partforge, "detail_on", text="")

    def draw(self, context):
        settings = context.scene.partforge
        column = self.layout.column()
        column.active = settings.detail_on
        for key in detail.DETAIL_KEYS:
            column.prop(settings, f"detail_{key}")
        column.operator("partforge.reset_detail", icon="NODETREE")
        column.label(text="Edit the nodes: Geometry Nodes editor > PF Surface Detail.", icon="INFO")


class PARTFORGE_PT_chassis(_Base, Panel):
    bl_label = "Chassis"
    bl_parent_id = "PARTFORGE_PT_main"

    def draw(self, context):
        settings = context.scene.partforge
        layout = self.layout
        layout.template_list("UI_UL_list", "partforge_chassis", settings, "chassis", settings, "chassis_index", rows=4)
        if not 0 <= settings.chassis_index < len(settings.chassis):
            layout.label(text="Press Load to fill the list.")
            return
        item = settings.chassis[settings.chassis_index]
        column = layout.column()
        column.prop(item, "body")
        row = column.row(align=True)
        row.prop(item, "seed")
        row.operator("partforge.reseed", text="", icon="FILE_REFRESH")
        for key in ("width", "height", "front", "boom", "engines", "armour", "wings"):
            column.prop(item, key)
        if item.wings:
            column.prop(item, "wing_span")
        column.prop(item, "sensor")
        if item.body == "heavy":
            column.prop(item, "rotor_layout")
        if item.body == "attack":
            column.prop(item, "snout")
            column.prop(item, "taper")
        if item.body == "utility":
            column.prop(item, "skids")
        column.label(text=f"Slots: {item.slots.replace(',', ', ')}")


class PARTFORGE_PT_output(_Base, Panel):
    bl_label = "Output"
    bl_parent_id = "PARTFORGE_PT_main"

    def draw(self, context):
        settings = context.scene.partforge
        layout = self.layout
        layout.operator("partforge.export", icon="EXPORT")
        row = layout.row(align=True)
        row.prop(settings, "skin", text="")
        row.operator("partforge.show_skin", icon="MATERIAL")
        row = layout.row(align=True)
        row.operator("partforge.render", icon="RENDER_STILL")
        row.prop(settings, "samples", text="")
        row = layout.row(align=True)
        row.prop(settings, "new_style_name", text="")
        row.operator("partforge.save_style", icon="FILE_TICK")


CLASSES = (
    PartForgeChassis, PartForgeSettings,
    PARTFORGE_OT_load_style, PARTFORGE_OT_rebuild, PARTFORGE_OT_export, PARTFORGE_OT_render, PARTFORGE_OT_show_skin,
    PARTFORGE_OT_save_style,
    PARTFORGE_OT_reseed, PARTFORGE_OT_reset_detail,
    PARTFORGE_PT_main, PARTFORGE_PT_shape, PARTFORGE_PT_addons, PARTFORGE_PT_wear, PARTFORGE_PT_paint,
    PARTFORGE_PT_form, PARTFORGE_PT_detail, PARTFORGE_PT_chassis, PARTFORGE_PT_output,
)


def register():
    """Safe to call again (re-registers)."""
    unregister()
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.types.Scene.partforge = PointerProperty(type=PartForgeSettings)


def unregister():
    if hasattr(bpy.types.Scene, "partforge"):
        del bpy.types.Scene.partforge
    for cls in reversed(CLASSES):
        if getattr(cls, "is_registered", False):
            bpy.utils.unregister_class(cls)
