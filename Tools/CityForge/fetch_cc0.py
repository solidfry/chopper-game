"""
Downloads the CC0 detail textures CityForge's shaders use into Assets/Chopper/Art/ThirdParty/CC0 and writes
LICENSES.md there. Run again to fetch them anew (needs network access to api.polyhaven.com and dl.polyhaven.org;
ambientCG also needs ambientcg.com and acg-download.struffelproductions.com).

    python Tools/CityForge/fetch_cc0.py

Only CC0 sources (public domain, safe to ship): Poly Haven, ambientCG. 1k JPG colour and OpenGL normal maps; the
shaders use them as detail (tinted by the biome / palette), so the resolution can stay low.
"""
import io
import json
import os
import urllib.request
import zipfile

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Chopper", "Art", "ThirdParty", "CC0")

# name: (source, asset id, use)
TEXTURES = {
    "RockFace": ("polyhaven", "rock_face_03", "terrain cliffs (triplanar detail)"),
    "GravellySand": ("polyhaven", "gravelly_sand", "terrain flat ground (top-down detail)"),
    "DryMud": ("polyhaven", "brown_mud_dry", "terrain hollows and sediment (top-down detail)"),
    "ConcreteWall": ("polyhaven", "concrete_wall_003", "buildings, rough surfaces (triplanar detail)"),
    "WindSnow": ("ambientcg", "Snow009A", "terrain cover layer: snow, moss, ash (top-down detail)"),
}


def _get(url):
    request = urllib.request.Request(url, headers={"User-Agent": "CityForge texture fetch"})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


def polyhaven(asset, folder, name):
    files = json.loads(_get(f"https://api.polyhaven.com/files/{asset}"))
    info = json.loads(_get(f"https://api.polyhaven.com/info/{asset}"))
    out = []
    for key, suffix in (("Diffuse", "Colour"), ("nor_gl", "Normal")):
        url = files[key]["1k"]["jpg"]["url"]
        path = os.path.join(folder, f"{name}_{suffix}.jpg")
        with open(path, "wb") as handle:
            handle.write(_get(url))
        out.append(os.path.basename(path))
    authors = ", ".join(info.get("authors", {}).keys())
    return out, f"https://polyhaven.com/a/{asset}", authors


def ambientcg(asset, folder, name):
    data = _get(f"https://ambientcg.com/get?file={asset}_1K-JPG.zip")
    out = []
    if not data.startswith(b"PK"):
        raise SystemExit(f"ambientCG {asset}: no zip came back (network access?)")
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for member in archive.namelist():
            for tag, suffix in (("_Color.", "Colour"), ("_NormalGL.", "Normal")):
                if tag in member:
                    path = os.path.join(folder, f"{name}_{suffix}.jpg")
                    with open(path, "wb") as handle:
                        handle.write(archive.read(member))
                    out.append(os.path.basename(path))
    return out, f"https://ambientcg.com/view?id={asset}", "ambientCG (Lennart Demes)"


def main():
    os.makedirs(OUT, exist_ok=True)
    rows = []
    for name, (source, asset, use) in TEXTURES.items():
        folder = os.path.join(OUT, name)
        os.makedirs(folder, exist_ok=True)
        files, page, authors = (polyhaven if source == "polyhaven" else ambientcg)(asset, folder, name)
        rows.append((name, source, asset, page, authors, use, files))
        print(f"{name:<14} {source:<10} {asset:<20} {', '.join(files)}")
    lines = ["# Third-party assets: CC0", "",
             "All files in this folder are CC0 1.0 (public domain dedication): free to use, change and ship in a "
             "commercial game, no attribution needed. Credits below anyway. Fetched by `Tools/CityForge/fetch_cc0.py`.",
             "", "| Folder | Source | Asset | Page | Authors | Used for | Files |", "|---|---|---|---|---|---|---|"]
    for name, source, asset, page, authors, use, files in rows:
        lines.append(f"| {name} | {source} | {asset} | {page} | {authors} | {use} | {', '.join(files)} |")
    lines += ["", "Licence: https://creativecommons.org/publicdomain/zero/1.0/ (Poly Haven: https://polyhaven.com/license, "
              "ambientCG: https://docs.ambientcg.com/license/)."]
    with open(os.path.join(OUT, "LICENSES.md"), "w") as handle:
        handle.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
