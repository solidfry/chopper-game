"""Checks a generated city: does any building stand in the sky-road loop's way?

Reads <kit>.terrain.json (layout) and <kit>.manifest.json (bounds) from Assets/Chopper/Art/City/<kit> and tests the
loop path (layout.SKY_LOOP round layout.RING, the deck at roads.SKY base +- swing) against each building's box.
Run from the repo root:  python Tools/CityForge/check_sky_road.py City01 City04
"""
import json
import math
import os
import sys

RING = (0.0, -60.0)
LOOP = (340.0, 255.0)
BASE, SWING, CLEAR, HALF_WIDTH = 150.0, 30.0, 14.0, 7.0


def check(kit):
    folder = os.path.join("Assets", "Chopper", "Art", "City", kit)
    terrain = json.load(open(os.path.join(folder, f"{kit}.terrain.json"), encoding="utf8"))
    manifest = json.load(open(os.path.join(folder, f"{kit}.manifest.json"), encoding="utf8"))
    bounds = {b["name"]: b["bounds"] for b in manifest["buildings"]}
    hits = []
    for place in terrain["layout"]:
        b = bounds.get(place["building"])
        if b is None or place["building"].startswith(("City_", "Shack")):
            continue
        px, py, pz = place["position"]
        top = py + b["max"][1]
        reach = math.hypot(max(abs(b["min"][0]), abs(b["max"][0])), max(abs(b["min"][2]), abs(b["max"][2])))
        for k in range(360):
            a = math.tau * k / 360
            x, z = RING[0] + LOOP[0] * math.cos(a), RING[1] + LOOP[1] * math.sin(a)
            deck = BASE + SWING * math.sin(2 * a + 0.6)
            if math.hypot(x - px, z - pz) < reach + HALF_WIDTH and top + CLEAR > deck:
                hits.append((place["building"], round(top), round(deck)))
                break
    print(f"{kit}: {len(hits)} building(s) in the sky road's way" + (f": {hits[:8]}" if hits else ""))
    return len(hits)


if __name__ == "__main__":
    sys.exit(1 if sum(check(kit) for kit in (sys.argv[1:] or ["City01", "City04"])) else 0)
