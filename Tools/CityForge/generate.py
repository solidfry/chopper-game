"""
CityForge entry point.

Command line (full run: build, export to Unity, previews, .blend):
    python generate.py [--style NAME] [--kit NAME] [--out DIR] [--previews DIR] [--only NAME,NAME]
                       [--no-render] [--no-sheet] [--no-blend] [--samples N]
    python generate.py --audit [--style NAME]       building check only (no export); exit 1 on problems
    blender --background --python generate.py -- [same options]

Defaults: style "dystopia" (kit City01); kit into Assets/Chopper/Art/City/<kit>, previews into
Tools/CityForge/previews/<kit>, the .blend into Tools/CityForge/blend/<kit>.blend.
"""
import argparse
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
if not bpy.app.background:
    for name in [name for name in sys.modules if name == "cityforge" or name.startswith("cityforge.")]:
        del sys.modules[name]
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from cityforge import audit, build, styles  # noqa: E402


def main():
    if "--" in sys.argv:
        argv = sys.argv[sys.argv.index("--") + 1:]
    else:
        argv = [] if not bpy.app.background else sys.argv[1:]
    repo = os.path.abspath(os.path.join(HERE, "..", ".."))
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--style", default="dystopia", help=f"Art direction: {', '.join(styles.names())}")
    parser.add_argument("--kit", default=None, help="Kit name (default: the style's kit, e.g. City01).")
    parser.add_argument("--out", default=None, help="Output folder (default: Assets/Chopper/Art/City/<kit>).")
    parser.add_argument("--previews", default=os.path.join(HERE, "previews"))
    parser.add_argument("--only", default=None, help="Comma-separated building names (default: all).")
    parser.add_argument("--no-render", action="store_true")
    parser.add_argument("--no-sheet", action="store_true", help="Skip the per-building sheet (13 renders).")
    parser.add_argument("--no-blend", action="store_true")
    parser.add_argument("--no-terrain", action="store_true", help="Buildings only (no terrain, no layout).")
    parser.add_argument("--samples", type=int, default=32)
    parser.add_argument("--biome", default=None, help="Terrain biome (cityforge/terrain_biomes.py); default: style's.")
    parser.add_argument("--audit", action="store_true", help="Check the buildings only; exit 1 on problems.")
    args = parser.parse_args(argv)

    style = styles.get(args.style)
    kit = args.kit or style["kit"]
    if args.biome:
        styles.STYLES[args.style]["biome"] = args.biome
    names = args.only.split(",") if args.only else None

    if args.audit:
        problems = audit.run(build.make_buildings(style, names, with_variants=not names))
        print(f"{problems} problems")
        sys.exit(1 if problems else 0)

    out = args.out or os.path.join(repo, "Assets", "Chopper", "Art", "City", kit)
    blend = None if args.no_blend else os.path.join(HERE, "blend", f"{kit}.blend")
    state, manifest = build.build(out, os.path.join(args.previews, kit), kit_name=kit, style_name=args.style,
                                  render=not args.no_render, samples=args.samples, blend_path=blend, names=names,
                                  sheet=not args.no_sheet, with_terrain=not args.no_terrain)
    for entry in manifest["buildings"]:
        tris = entry["triangles"]
        flag = f"  {len(entry['audit'])} audit problems" if entry["audit"] else ""
        print(f"{entry['name']:<14} {entry['type']:<10} tris {tris[0]:>6} / {tris[1]:>5} / {tris[2]:>4}  "
              f"colliders {len(entry['colliders']):>2}  markers {len(entry['markers'])}{flag}")
    if "terrain" in manifest:
        summary = manifest["terrain"]
        flag = f"  {len(summary['audit'])} audit problems: {summary['audit']}" if summary["audit"] else "  audit ok"
        print(f"Terrain: {summary['chunks']} chunks{flag}")
    print(f"Wrote {len(manifest['buildings'])} buildings to {out}" + (f"; Blender file {blend}" if blend else ""))


if __name__ == "__main__":
    main()
