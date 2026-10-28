"""
PartForge entry point.

    python generate.py [--out DIR] [--previews DIR] [--kit NAME] [--no-render] [--samples N]   (pip package `bpy`)
    blender --background --python generate.py -- [same options]                               (Blender install)

Defaults write the kit into the Unity project (Assets/Chopper/Art/Kits/<kit>) and previews into Tools/PartForge/previews.
"""
import argparse
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from partforge import build  # noqa: E402


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    repo = os.path.abspath(os.path.join(HERE, "..", ".."))
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--kit", default="Kit01")
    parser.add_argument("--out", default=None, help="Output folder (default: Assets/Chopper/Art/Kits/<kit>).")
    parser.add_argument("--previews", default=os.path.join(HERE, "previews"))
    parser.add_argument("--no-render", action="store_true")
    parser.add_argument("--samples", type=int, default=32)
    args = parser.parse_args(argv)

    out = args.out or os.path.join(repo, "Assets", "Chopper", "Art", "Kits", args.kit)
    manifest = build.build(out, args.previews, kit=args.kit, render=not args.no_render, samples=args.samples)
    for part in manifest["parts"]:
        print(f"{part['name']:<24} {part['kind']:<8} {part['triangles']:>6} tris  {len(part['sockets'])} sockets")
    print(f"Wrote {len(manifest['parts'])} parts to {out}")


if __name__ == "__main__":
    main()
