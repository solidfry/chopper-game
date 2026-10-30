"""
Renders the cockpit of one chassis from the pilot's eye (front, head look right and left) and one view from outside, to
previews/<kit>/cockpit_<name>_*.jpg.
    blender --background --python cockpit_preview.py -- [--style dystopia] [--chassis Gunship] [--samples 24]
"""
import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from partforge import build, styles  # noqa: E402
from partforge.geo import to_blender  # noqa: E402


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    options = dict(zip(argv[::2], argv[1::2]))
    style = styles.get(options.get("--style", "dystopia"))
    name = options.get("--chassis", "Gunship")
    samples = int(options.get("--samples", 24))

    bpy.ops.wm.read_factory_settings(use_empty=True)
    state = build.generate(style)
    scene = bpy.context.scene
    scene.cycles.samples = samples
    scene.render.resolution_x, scene.render.resolution_y = 1280, 720
    out_dir = os.path.join(HERE, "previews", state.kit)
    os.makedirs(out_dir, exist_ok=True)

    # Show the chassis and its cockpit at the origin, nothing else.
    export = bpy.data.collections[build.EXPORT_COLLECTION]
    export.hide_render = False
    export.hide_viewport = False
    wanted = {f"Chassis_{name}", f"Cockpit_{name}"}
    # Cycles draws the back faces of the canopy shell, which Unity culls: judge the inside with the cockpit alone.
    inside = {f"Cockpit_{name}"}
    for part, obj in state.built.values():
        obj.hide_render = part.name not in wanted
        for child in obj.children:
            child.hide_render = True
    for collection in (build.CHOPPERS_COLLECTION, build.MODULES_COLLECTION):
        for obj in bpy.data.collections[collection].objects:
            obj.hide_render = True

    camera = bpy.data.objects["PF Camera"]
    aim = bpy.data.objects["PF Aim"]
    eye = to_blender(state.built[f"Cockpit_{name}"][0].sockets[0][2])
    print("triangles", name, build._triangles(state.built[f"Cockpit_{name}"][1]))
    for constraint in list(camera.constraints):
        camera.constraints.remove(constraint)
    scene.camera = camera

    def show(names):
        for part, obj in state.built.values():
            obj.hide_render = part.name not in names

    light = bpy.data.objects.new("PF Cabin Light", bpy.data.lights.new("PF Cabin Light", "POINT"))
    bpy.context.scene.collection.objects.link(light)
    light.data.energy = 60
    light.data.shadow_soft_size = 0.3
    light.location = eye + Vector((0, 0.1, 0.45))
    show(inside)

    # Inside: from the eye, looking forward (Unity +Z = Blender -Y), a little down like the game camera.
    camera.location = eye
    camera.rotation_euler = (math.radians(89), 0, math.radians(180))
    camera.data.sensor_fit = "HORIZONTAL"
    camera.data.lens = 14.6        # the game's cockpit view: 70 degrees tall at 16:9 (102 across, 36 mm sensor)
    camera.data.clip_start = 0.03
    build._render(scene, os.path.join(out_dir, f"cockpit_{name.lower()}_eye.jpg"))

    # Looking down at the dash.
    camera.rotation_euler = (math.radians(89 - 30), 0, math.radians(180))
    build._render(scene, os.path.join(out_dir, f"cockpit_{name.lower()}_down.jpg"))

    # Head look right and left.
    for label, yaw in (("right", -75), ("left", 75)):
        camera.rotation_euler = (math.radians(90), 0, math.radians(180 + yaw))
        build._render(scene, os.path.join(out_dir, f"cockpit_{name.lower()}_look_{label}.jpg"))

    # The seat: from in front of the pilot, looking back and down.
    camera.location = eye + Vector((0, -0.32, 0.06))
    camera.rotation_euler = (math.radians(68), 0, 0)
    build._render(scene, os.path.join(out_dir, f"cockpit_{name.lower()}_seat.jpg"))
    camera.location = eye

    # Outside, front left: where the eye sits.
    light.hide_render = True
    show(wanted)
    camera.location = Vector((-5.5, -7.5, 3.0))
    track = camera.constraints.new("TRACK_TO")
    track.target = aim
    track.track_axis, track.up_axis = "TRACK_NEGATIVE_Z", "UP_Y"
    aim.location = eye
    camera.data.lens = 40
    build._render(scene, os.path.join(out_dir, f"cockpit_{name.lower()}_outside.jpg"))
    print("done", out_dir)


main()
