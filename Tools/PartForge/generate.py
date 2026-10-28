"""
PartForge entry point.

Command line (full run: build, export to Unity, previews, .blend):
    python generate.py [--style NAME] [--kit NAME] [--out DIR] [--previews DIR] [--no-render] [--no-blend] [--samples N]
    blender --background --python generate.py -- [same options]

Blender window (the PartForge panel, N > PartForge; build in place, export and render from the panel):
    blender --python generate.py [-- --style NAME]
    or open Tools/PartForge/blend/<kit>.blend and allow its scripts.

Defaults: style "dystopia" (Kit02); kit into Assets/Chopper/Art/Kits/<kit>, previews into Tools/PartForge/previews/<kit>,
the .blend into Tools/PartForge/blend/<kit>.blend.
"""
import argparse
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
IN_WINDOW = not bpy.app.background
if IN_WINDOW:
    # Running again in the same Blender: use the edited code, not the cached modules.
    for name in [name for name in sys.modules if name == "partforge" or name.startswith("partforge.")]:
        del sys.modules[name]
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from partforge import build, styles, ui  # noqa: E402


def main():
    if "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1:]
    else:
        argv = [] if IN_WINDOW else sys.argv[1:]
    repo = os.path.abspath(os.path.join(HERE, "..", ".."))
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--style", default="dystopia", help=f"Art direction: {', '.join(styles.names())}")
    parser.add_argument("--kit", default=None, help="Kit name (default: the style's kit, e.g. Kit02).")
    parser.add_argument("--out", default=None, help="Output folder (default: Assets/Chopper/Art/Kits/<kit>).")
    parser.add_argument("--previews", default=os.path.join(HERE, "previews"))
    parser.add_argument("--no-render", action="store_true")
    parser.add_argument("--no-blend", action="store_true")
    parser.add_argument("--samples", type=int, default=32)
    args = parser.parse_args(argv)

    style = styles.get(args.style)
    kit = args.kit or style["kit"]

    if IN_WINDOW:
        ui.register()
        settings = bpy.context.scene.partforge
        ui.load_style(settings, style, args.style, kit)
        ui.rebuild(settings)
        print("PartForge: panel ready in the 3D view sidebar (N > PartForge).")
        return

    out = args.out or os.path.join(repo, "Assets", "Chopper", "Art", "Kits", kit)
    blend = None if args.no_blend else os.path.join(HERE, "blend", f"{kit}.blend")
    manifest = build.build(out, os.path.join(args.previews, kit), kit=kit, style_name=args.style,
                           render=not args.no_render, samples=args.samples, blend_path=blend)
    for part in manifest["parts"]:
        print(f"{part['name']:<24} {part['kind']:<8} {part['triangles']:>6} tris  {len(part['sockets'])} sockets")
    print(f"Wrote {len(manifest['parts'])} parts to {out}" + (f"; Blender file {blend}" if blend else ""))


if __name__ == "__main__":
    main()
