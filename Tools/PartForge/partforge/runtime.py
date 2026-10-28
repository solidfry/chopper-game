"""
Runtime skins: the skin shader's data, and a numpy copy of the shader (Assets/Chopper/Art/Shaders/ChopperSkin.hlsl).

The baked skins (skins.compose) paint one texture set per skin. The runtime path paints in the shader instead, so a new
skin is data only. Per texture set it needs only skin-independent maps (written by texture.write):

  base maps    the first skin's maps (colour, metallic/smoothness, emission) + normal + occlusion: everything that is
               not skin paint (glass, lights, metal, rubber, the kit's own wear) comes from them.
  SkinMask     R paint (armour that takes the skin), G replacement panels, B warning paint, A armour tone
               (0 = dark panel, 0.5 = hull, 1 = light panel).
  SkinWear     R dirt (crevices + rain streaks, / 3), G soot (behind exhausts, style factor in), B paint blotches,
               A wear threshold / 2 (the paint is bare metal where the skin's wear is above it).

A skin is one row of the skin table texture (TABLE_WIDTH texels of float4, layout in encode()). The shader reads the row
of the chopper's skin index; Unity builds the table from SkinLibrary (C# SkinTable.Encode, same layout).

shade() is the shader, line for line, in numpy: the export uses it to check that the runtime look matches the baked
skins, and to render the runtime skin sheet. Change both together.
"""
import numpy as np

from . import skins

TABLE_WIDTH = 20
PATTERNS = ("solid", "camo", "digital", "splinter", "tiger", "hex", "synthwave", "livery")
# fbm (4 octaves) quantiles at 0, 0.05 ... 1: the threshold that leaves `cover` of the surface above it is
# COVER_TABLE at 1 - cover. Measured on 400k random points; C# SkinTable has the same numbers.
COVER_TABLE = (0.0681, 0.3138, 0.352, 0.379, 0.4009, 0.4202, 0.438, 0.4543, 0.4702, 0.4854, 0.5005, 0.5154, 0.5308,
               0.5467, 0.5631, 0.5806, 0.5999, 0.6217, 0.6483, 0.6862, 0.9283)
BARE = "#8f8d86"
DIRT = "#2b261f"
SOOT = "#0f0e0d"


def cover_threshold(cover):
    x = (1.0 - min(max(cover, 0.0), 1.0)) * 20.0
    i = min(int(x), 19)
    return COVER_TABLE[i] + (COVER_TABLE[i + 1] - COVER_TABLE[i]) * (x - i)


def style_wear(style):
    return max(style.get("wear", 0.0), 0.15)


# -- skin -> table row ----------------------------------------------------------------------------------------------

def params(skin, rows, names, wear_of_style):
    """The skin with every default resolved: what the manifest carries and C# (SkinTable.Encode) encodes. Colours are
    linear RGB, flat lists of 3 floats per colour (Unity's JsonUtility cannot read nested lists); hazard is [] when
    the skin keeps the warning paint."""
    index = {name: i for i, name in enumerate(names)}
    hull = rows[index["hull"]][0]
    pattern = dict(skin.get("pattern") or {"type": "solid"})
    colours = [skins.hex_linear(c).tolist() for c in pattern.get("colours") or []] or [hull.tolist()]
    flat = lambda values: [float(v) for colour in values for v in colour]  # noqa: E731

    def tone(key, name):
        value = skin.get(key)
        if value is None:
            return (rows[index[name]][0] / np.maximum(hull, 1e-4)).tolist()
        return [float(value)] * 3

    glow = skin.get("glow") or {}
    finish = skin.get("finish")
    return dict(
        pattern=pattern.get("type", "solid"), colours=flat(colours), scale=float(pattern.get("scale", 1.0)),
        seed=int(pattern.get("seed", 1)), warp=float(pattern.get("warp", 0.0)),
        cover=[float(c) for c in pattern.get("cover", [])], weights=[float(w) for w in pattern.get("weights", [])],
        size=float(pattern.get("pixel", pattern.get("period", pattern.get("cell", pattern.get("grid", 0.0))))),
        width=float(pattern.get("width", 0.3)), stretch=float(pattern.get("stretch", 2.0)),
        line=skins.hex_linear(pattern.get("line", "#000000")).tolist(),
        sun=flat(skins.hex_linear(c).tolist() for c in pattern.get("sun", [])),
        band=list(pattern.get("band", (0.15, 0.5))) if pattern.get("type") != "livery" else [0.15, 0.5],
        stripe=list(pattern.get("stripe", (0.05, 0.13))),
        livery_band=list(pattern.get("band", (0.42, 0.06))) if pattern.get("type") == "livery" else [0.42, 0.06],
        metallic=float(finish[0]) if finish else -1.0, smoothness=float(finish[1]) if finish else -1.0,
        wear=float(skin.get("wear", 1.0)) * wear_of_style, grime=float(skin.get("grime", 1.0)) * (0.4 + 0.6 * wear_of_style),
        panels=bool(skin.get("panels")), hazard=skins.hex_linear(skin["hazard"]).tolist() if skin.get("hazard") else [],
        glow=flat(skins.hex_linear(c).tolist() for c in glow.get("colours", [])), glow_share=float(glow.get("share", 0.15)),
        glow_width=float(glow.get("width", 0.012)), dark=tone("dark", "hull_dark"), light=tone("light", "hull_light"),
    )


def encode(p):
    """One table row (TABLE_WIDTH, 4) from params(): the layout the shader reads.

    0-3   colours A-D (linear RGB); A = pattern id, B = scale, C = seed, D = warp
    4     camo/digital: layer thresholds 1-3 | splinter: cumulative weights 1-3 | tiger: x = stripe width;  w = size
          (digital pixel, tiger period, hex cell, synthwave grid)
    5     metallic (< 0 = keep the base maps), smoothness, wear, grime
    6     panels (0/1), warning paint recolour (0/1), glow (0/1), colour count
    7     warning paint colour; w = splinter stretch
    8, 9  glow colours A, B; w = glow share, glow width
    10    synthwave band low, high; livery stripe inner, outer
    11    livery band centre, half height; glow colour count
    12-13 dark and light armour tone (RGB multipliers)
    14-16 hex line colour (14) | synthwave sun colours (14-16)"""
    row = np.zeros((TABLE_WIDTH, 4), dtype=np.float32)
    triples = lambda values: [values[i:i + 3] for i in range(0, len(values), 3)]  # noqa: E731
    colours = triples(p["colours"])
    glow, sun = triples(p["glow"]), triples(p["sun"])
    count = len(colours)
    colours = (colours + [colours[-1]] * 4)[:4]
    for k in range(4):
        row[k, :3] = colours[k]
    row[0, 3] = PATTERNS.index(p["pattern"])
    row[1, 3] = p["scale"]
    row[2, 3] = p["seed"]
    row[3, 3] = p["warp"]
    if p["pattern"] == "splinter":
        weights = p["weights"] or [1.0] * count
        cumulative = np.cumsum(weights) / np.sum(weights)
        row[4, :3] = (list(cumulative[:-1]) + [2.0, 2.0, 2.0])[:3]
    elif p["pattern"] == "tiger":
        row[4, 0] = p["width"]
    else:
        row[4, :3] = ([cover_threshold(c) for c in p["cover"]] + [2.0, 2.0, 2.0])[:3]
    row[4, 3] = p["size"]
    row[5] = (p["metallic"], p["smoothness"], p["wear"], p["grime"])
    row[6] = (float(p["panels"]), float(bool(p["hazard"])), float(bool(glow)), count)
    row[7, :3] = p["hazard"] or (0.0, 0.0, 0.0)
    row[7, 3] = p["stretch"]
    glows = (glow + glow[-1:] * 2)[:2] if glow else [[0, 0, 0], [0, 0, 0]]
    row[8, :3], row[8, 3] = glows[0], p["glow_share"]
    row[9, :3], row[9, 3] = glows[1], p["glow_width"]
    row[10] = (p["band"][0], p["band"][1], p["stripe"][0], p["stripe"][1])
    row[11] = (p["livery_band"][0], p["livery_band"][1], len(glow), 0.0)
    row[12, :3] = p["dark"]
    row[13, :3] = p["light"]
    if p["pattern"] == "hex":
        row[14, :3] = p["line"]
    for k, colour in enumerate(sun[:3]):
        row[14 + k, :3] = colour
    return row


# -- skin-independent maps ------------------------------------------------------------------------------------------

def maps(surface, names, wear_of_style):
    """SkinMask and SkinWear values over the valid texels: two (n, 4) arrays in 0..1."""
    swatch = surface["swatch"]
    index = {name: i for i, name in enumerate(names)}

    def where(group):
        return np.isin(swatch, [index[name] for name in group if name in index]).astype(np.float32)

    tone = np.full(len(swatch), 0.5, dtype=np.float32)
    tone[swatch == index["hull_dark"]] = 0.0
    tone[swatch == index["hull_light"]] = 1.0
    mask = np.stack([where(skins.PAINT), where(skins.PANELS), where(skins.HAZARD), tone], axis=1)

    dirt = (1 - surface["cavity"]) * 1.4 + np.clip(surface["streak"] - 0.62, 0, 1) * 1.2
    soot = surface["soot"] * (0.4 + 0.6 * wear_of_style)
    edge, chips = surface["edge"], surface["chips"]
    # Lowest wear at which the paint here is bare (skins.compose: edges first, then chips near edges).
    first = (1.15 - edge * (0.6 + chips)) / 0.45
    second = np.where(edge > 0.25, (0.86 - chips) / 0.08, 9.0)
    threshold = np.clip(np.minimum(first, second), 0.0, 2.0)
    wear = np.stack([np.clip(dirt / 3.0, 0, 1), np.clip(soot, 0, 1), surface["blotch"], threshold / 2.0], axis=1)
    return mask, wear


# -- the shader in numpy --------------------------------------------------------------------------------------------

def _hash(ix, iy, iz, seed):
    return skins._hash(ix, iy, iz, seed)


def _layers(q, row, seed, warp, count):
    q = skins._warp(q, warp, seed)
    index = np.zeros(len(q), dtype=np.int32)
    for k in range(1, min(count, 4)):
        n = skins.fbm(q + k * 13.7, 4, seed + k * 31)
        index[n > row[4, k - 1]] = k
    return index


def pattern(row, p, normal, height):
    """Paint colour (n, 3) and glow colour index (n,) (0 = none, 1 = glow A, 2 = glow B)."""
    kind = PATTERNS[int(round(row[0, 3]))]
    colours = row[0:4, :3]
    scale, seed, warp = row[1, 3], int(round(row[2, 3])), row[3, 3]
    count = int(round(row[6, 3]))
    size = row[4, 3]
    n = len(p)
    glow = np.zeros(n, dtype=np.int32)
    glow_on = row[6, 2] > 0.5
    if kind == "solid":
        index = np.zeros(n, dtype=np.int32)
    elif kind == "camo":
        index = _layers(p / scale, row, seed, warp, count)
    elif kind == "digital":
        u, v, axis = skins._dominant_plane(p, normal)
        depth = np.choose(axis, [p[:, 0], p[:, 1], p[:, 2]])
        snapped = np.stack([np.floor(u / size), np.floor(v / size), np.floor(depth / (size * 4))], axis=1) * size
        index = _layers(snapped / scale, row, seed, warp, count)
    elif kind == "splinter":
        q = p / scale * np.array([1.0, 1.0, 1.0 / row[7, 3]], dtype=np.float32)
        q = q + (skins.fbm(q * 0.7, 2, seed)[:, None] - 0.5) * 0.25
        cell = skins._voronoi_cell(q, seed)
        index = ((cell >= row[4, 0]).astype(np.int32) + (cell >= row[4, 1]) + (cell >= row[4, 2])).clip(0, count - 1)
    elif kind == "tiger":
        warped = p[:, 2] / size + (skins.fbm(p * 0.8, 3, seed) - 0.5) * warp * 2 + p[:, 1] * 0.35
        phase = warped - np.floor(warped)
        width = row[4, 0] * (0.6 + 0.8 * skins.fbm(p * 1.7 + 5.0, 2, seed + 3))
        index = np.where(phase < width, 1, np.where(phase < width * 1.45, 2 if count > 2 else 1, 0)).astype(np.int32)
    elif kind == "hex":
        u, v, axis = skins._dominant_plane(p, normal)
        (cx, cz), edge = skins._hex_grid(u / size, v / size)
        ax = axis.astype(np.int64)
        index = (_hash(cx, cz, ax, seed) * count).astype(np.int32).clip(0, count - 1)
        colour = colours[index]
        colour[edge < 0.06] = row[14, :3]
        if glow_on:
            lit = _hash(cx + 7, cz - 3, ax, seed + 5) < row[8, 3]
            near = edge < row[9, 3] / size * 2.5
            pick = (_hash(cx, cz, ax + 9, seed) * row[11, 2]).astype(np.int32).clip(0, 1)
            glow = np.where(near & lit, pick + 1, 0)
        return colour, glow
    elif kind == "synthwave":
        index = (skins.fbm(p * 0.6, 2, seed) > 0.62).astype(np.int32)
        colour = colours[index]
        low, high = row[10, 0], row[10, 1]
        t = np.clip((height - low) / (high - low), 0.0, 1.0)
        stripe = (height * 22.0) % 1.0
        in_band = (height > low) & (height < high) & (stripe > 0.15 + 0.55 * (1 - t))
        sun = row[14:17, :3]
        tc = (1 - t) * 2.0
        k = np.floor(tc).astype(int).clip(0, 1)
        frac = (tc - k)[:, None]
        colour = np.where(in_band[:, None], sun[k] * (1 - frac) + sun[k + 1] * frac, colour)
        if glow_on:
            u, v, _axis = skins._dominant_plane(p, normal)
            width = row[9, 3] / size
            du = np.abs(u / size - np.round(u / size))
            dv = np.abs(v / size - np.round(v / size))
            glow = (((du < width) | (dv < width)) & ~in_band & (height < 0.75)).astype(np.int32)
        return colour, glow
    elif kind == "livery":
        index = np.zeros(n, dtype=np.int32)
        inner, outer = row[10, 2], row[10, 3]
        ax = np.abs(p[:, 0])
        index[(normal[:, 1] > 0.45) & (ax > inner) & (ax < outer)] = 1
        centre, half = row[11, 0], row[11, 1]
        side = np.abs(normal[:, 0]) > 0.4
        index[side & (np.abs(height - centre) < half)] = 1
        index[side & (np.abs(height - centre - half * 1.7) < half * 0.35)] = 2
        index[normal[:, 1] < -0.55] = 2
    return colours[index], glow


def shade(row, data):
    """The skin shader on (n,) texels. data: base colour (linear), base metallic, smoothness, base emission (linear),
    mask (n, 4), wear (n, 4), occlusion, position, normal, height. Returns linear colour, metallic, smoothness,
    emission."""
    mask, wear = data["mask"], data["wear"]
    base, base_metal, base_smooth, base_glow = data["colour"], data["metallic"], data["smoothness"], data["emission"]
    ao = np.clip((data["occlusion"] - 0.35) / 0.65, 0, 1)
    zone = np.clip(mask[:, 0] + row[6, 0] * mask[:, 1], 0, 1)
    hazard_zone = row[6, 1] * mask[:, 2]
    a = mask[:, 3:4]
    tone = (row[12, :3] + (1 - row[12, :3]) * np.clip(a * 2, 0, 1)) * (1 - np.clip(a * 2 - 1, 0, 1)) \
        + row[13, :3] * np.clip(a * 2 - 1, 0, 1)

    colour, glow_index = pattern(row, data["position"], data["normal"], data["height"])
    paint = colour * tone
    glow_colour = np.where((glow_index == 2)[:, None], row[9, :3], row[8, :3])
    glowing = glow_index > 0
    paint = np.where(glowing[:, None], glow_colour, paint)
    keep = row[5, 0] < 0
    metal = np.where(keep, base_metal, row[5, 0])
    smooth = np.where(keep, base_smooth, row[5, 1])
    smooth = np.where(glowing, 0.8, smooth)
    emission = np.where(glowing[:, None], glow_colour, 0.0)

    def finish(c, metal, smooth, emission, emissive):
        blotch = wear[:, 2]
        c = c * (0.92 + 0.16 * blotch)[:, None]
        bare = (row[5, 2] > 0) & (row[5, 2] > wear[:, 3] * 2.0)
        c = np.where(bare[:, None], skins.hex_linear(BARE) * (0.8 + 0.4 * blotch)[:, None], c)
        metal = np.where(bare, 0.85, metal)
        smooth = np.where(bare, 0.42, smooth)
        emission = np.where(bare[:, None], 0.0, emission)
        emissive = emissive & ~bare
        amount = np.where(emissive, 0.0, np.clip(wear[:, 0] * 3.0 * row[5, 3], 0, 0.85))
        c = c * (1 - amount[:, None]) + skins.hex_linear(DIRT) * (amount * 0.6)[:, None]
        smooth = smooth * (1 - amount * 0.6)
        soot = np.where(emissive, 0.0, wear[:, 1])
        c = c * (1 - soot[:, None] * 0.85) + skins.hex_linear(SOOT) * (soot * 0.85)[:, None]
        smooth = smooth * (1 - soot * 0.7)
        metal = metal * (1 - soot * 0.8)
        c = c * (0.72 + 0.28 * ao)[:, None]
        return c, metal, smooth, emission

    paint, metal, smooth, emission = finish(paint, metal, smooth, emission, glowing)
    lit = base_glow.max(axis=1) > 0.05
    out_colour = base * (1 - zone[:, None]) + paint * zone[:, None]
    out_metal = base_metal * (1 - zone) + metal * zone
    out_smooth = base_smooth * (1 - zone) + smooth * zone
    out_glow = base_glow * (1 - zone[:, None]) + emission * zone[:, None]
    if row[6, 1] > 0.5:
        marks, m_metal, m_smooth, _ = finish(np.broadcast_to(row[7, :3], base.shape).copy(), base_metal, base_smooth,
                                             np.zeros_like(base), lit)
        out_colour = out_colour * (1 - hazard_zone[:, None]) + marks * hazard_zone[:, None]
        out_metal = out_metal * (1 - hazard_zone) + m_metal * hazard_zone
        out_smooth = out_smooth * (1 - hazard_zone) + m_smooth * hazard_zone
        out_glow = out_glow * (1 - hazard_zone[:, None])
    return out_colour, out_metal, out_smooth, out_glow
