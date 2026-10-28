"""
Skins: paint schemes laid over the baked surface (texture.py). A skin is data: a pattern, its colours, a paint finish,
optional glow lines, and how much wear and grime show. The composer paints only the painted armour (hull swatches, and
replacement panels or warning paint when the skin says so); glass, lights, bare metal, rubber and weapons keep their
palette look. Wear (bare edges, chips), grime in the crevices and soot behind the exhausts go on top of every skin, so
each skin sits on the same worn machine.

Patterns are worked out in 3D, in model space (Unity metres), not in UV space: they run across UV seams and across
separate parts without a break, and the same skin lines up on every chassis and module.

Add a skin: a new entry in SKINS (or "skins" in a saved style JSON), then export. Ids are saved in player data: never
reuse or change one. See ART_DIRECTION.md, "Skins".
"""
import numpy as np

# Swatch groups (palette.SWATCHES names).
PAINT = ("hull", "hull_dark", "hull_light")
PANELS = ("primer", "concrete", "rust")
HAZARD = ("hazard", "hazard_faded")
WEARS = PAINT + PANELS + HAZARD + ("warhead", "missile")  # painted swatches that chip to bare metal

SKINS = [
    dict(id=1, key="factory", name="Factory", description="The style's own paint: worn two-tone armour.",
         pattern=dict(type="solid")),
    dict(id=2, key="woodland", name="Woodland", description="Four-colour woodland camouflage.",
         pattern=dict(type="camo", scale=1.5, colours=["#4f573a", "#2f3626", "#5e4d36", "#1c1d19"],
                      cover=[0.32, 0.26, 0.16], warp=0.6, seed=3),
         finish=(0.08, 0.22)),
    dict(id=3, key="desert", name="Desert", description="Three-colour desert camouflage, sun-bleached.",
         pattern=dict(type="camo", scale=2.0, colours=["#b19a73", "#8b7454", "#c9b892"], cover=[0.35, 0.22],
                      warp=0.5, seed=7),
         finish=(0.06, 0.2), grime=1.2),
    dict(id=4, key="arctic_digital", name="Arctic Digital", description="Pixel camouflage in snow greys.",
         pattern=dict(type="digital", scale=1.1, pixel=0.085, colours=["#c8cbc7", "#999fa2", "#6b7276", "#3c4246"],
                      cover=[0.3, 0.22, 0.12], warp=0.4, seed=11),
         finish=(0.08, 0.3)),
    dict(id=5, key="urban_splinter", name="Urban Splinter", description="Angular splinter shards in concrete greys.",
         pattern=dict(type="splinter", scale=0.9, colours=["#6f7377", "#4a4e52", "#2b2e32", "#9a9da0"],
                      weights=[0.35, 0.3, 0.2, 0.15], stretch=2.6, seed=5),
         finish=(0.1, 0.28)),
    dict(id=6, key="tiger", name="Jungle Tiger", description="Tiger stripes across the hull.",
         pattern=dict(type="tiger", period=0.75, colours=["#66703f", "#1d2016", "#3d4629"], width=0.34, warp=0.55,
                      seed=9),
         finish=(0.08, 0.24)),
    dict(id=7, key="neon_hex", name="Night City", description="Black hex armour cells, neon pink and cyan seams.",
         pattern=dict(type="hex", cell=0.34, colours=["#15161c", "#1d1f2a", "#262838"], line="#08080b", seed=13),
         finish=(0.35, 0.62), glow=dict(colours=["#ff2fd0", "#00e8ff"], share=0.16, width=0.012),
         wear=0.5, panels=True, hazard="#ff2fd0"),
    dict(id=8, key="synthwave", name="Outrun", description="Deep purple, a striped sunset band, a glowing grid.",
         pattern=dict(type="synthwave", colours=["#1b0c30", "#2a1050"], sun=["#ff2f8e", "#ff7b3d", "#ffd23f"],
                      band=(0.16, 0.4), grid=0.7),
         finish=(0.3, 0.7), glow=dict(colours=["#ff2fd0"], width=0.008),
         wear=0.4, grime=0.4, panels=True, hazard="#00e8ff"),
    dict(id=9, key="interceptor", name="Interceptor", description="Gloss white corporate livery, red and black stripes.",
         pattern=dict(type="livery", colours=["#d9dad5", "#c8102e", "#121315"], stripe=(0.05, 0.13), band=(0.42, 0.06)),
         finish=(0.15, 0.68), wear=0.35, grime=0.5, panels=True, hazard="#c8102e"),
    dict(id=10, key="night_stalker", name="Night Stalker", description="Matte black, red markings, almost no wear.",
         pattern=dict(type="solid", colours=["#17181a"], dark=0.75, light=1.35),
         finish=(0.12, 0.18), wear=0.25, grime=0.6, panels=True, hazard="#8f1424"),
]


def every(style):
    """The style's skins (style key "skins": a list of skin dicts, or of keys from SKINS) or all of SKINS."""
    chosen = style.get("skins")
    if not chosen:
        return [dict(skin) for skin in SKINS]
    by_key = {skin["key"]: skin for skin in SKINS}
    return [dict(by_key[item]) if isinstance(item, str) else dict(item) for item in chosen]


# -- colour helpers -------------------------------------------------------------------------------------------------

def srgb_to_linear(c):
    c = np.asarray(c, dtype=np.float32)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)


def hex_linear(value):
    value = value.lstrip("#")
    return srgb_to_linear([int(value[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def _smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


# -- noise (vectorised; same input = same output on every machine) --------------------------------------------------

def _hash(ix, iy, iz, seed):
    """Integer lattice -> [0, 1) (a 32-bit mix)."""
    h = (ix.astype(np.uint32) * np.uint32(0x8DA6B343)) ^ (iy.astype(np.uint32) * np.uint32(0xD8163841)) \
        ^ (iz.astype(np.uint32) * np.uint32(0xCB1AB31F)) ^ np.uint32((seed * 0x165667B1) & 0xFFFFFFFF)
    h ^= h >> np.uint32(13)
    h *= np.uint32(0x85EBCA6B)
    h ^= h >> np.uint32(16)
    h *= np.uint32(0xC2B2AE35)
    h ^= h >> np.uint32(16)
    return h.astype(np.float32) / np.float32(4294967296.0)


def value_noise(p, seed=0):
    """Smooth 3D value noise in [0, 1]; p is (n, 3)."""
    cell = np.floor(p)
    f = (p - cell).astype(np.float32)
    i = cell.astype(np.int64)
    u = f * f * (3 - 2 * f)
    result = np.zeros(len(p), dtype=np.float32)
    for dx in (0, 1):
        wx = u[:, 0] if dx else 1 - u[:, 0]
        for dy in (0, 1):
            wy = u[:, 1] if dy else 1 - u[:, 1]
            for dz in (0, 1):
                wz = u[:, 2] if dz else 1 - u[:, 2]
                result += _hash(i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz, seed) * wx * wy * wz
    return result


def fbm(p, octaves=4, seed=0, gain=0.5):
    """Fractal noise in about [0, 1]."""
    total, amplitude, norm = np.zeros(len(p), dtype=np.float32), 1.0, 0.0
    for octave in range(octaves):
        total += amplitude * value_noise(p * (2.0 ** octave), seed + octave * 101)
        norm += amplitude
        amplitude *= gain
    return total / norm


def _warp(p, amount, seed):
    if amount <= 0:
        return p
    offset = np.stack([fbm(p * 0.5, 3, seed + 17 * k) for k in range(3)], axis=1) - 0.5
    return p + offset * amount * 2.0


def _layers(p, colours, cover, seed, warp, valid_count):
    """Layered blobs: colour 0 is the base, each next colour covers about `cover[k]` of the surface (independent
    noise per layer, thresholds from quantiles so the shares hold on every model). Returns colour indices."""
    q = _warp(p, warp, seed)
    index = np.zeros(len(p), dtype=np.int32)
    for k, share in enumerate(cover, start=1):
        n = fbm(q + k * 13.7, 4, seed + k * 31)
        threshold = np.quantile(n, 1.0 - share) if valid_count else 1.0
        index[n > threshold] = k
    return index


def _dominant_plane(p, normal):
    """Two coordinates on the plane facing the normal's main axis (triplanar, no blending) and that axis."""
    axis = np.argmax(np.abs(normal), axis=1)
    u = np.where(axis == 0, p[:, 2], p[:, 0])
    v = np.where(axis == 1, p[:, 2], p[:, 1])
    return u, v, axis


# -- patterns: each returns (linear colour (n, 3), glow mask (n,) or None) ------------------------------------------

def _pattern(pattern, p, normal, height, base_colours, glow):
    kind = pattern.get("type", "solid")
    colours = np.array([hex_linear(c) for c in pattern["colours"]]) if pattern.get("colours") else base_colours
    seed = pattern.get("seed", 1)
    n = len(p)
    glow_mask = None

    if kind == "solid":
        index = np.zeros(n, dtype=np.int32)
    elif kind == "camo":
        index = _layers(p / pattern.get("scale", 1.5), colours, pattern["cover"], seed, pattern.get("warp", 0.5), n)
    elif kind == "digital":
        pixel = pattern.get("pixel", 0.08)
        u, v, axis = _dominant_plane(p, normal)
        depth = np.choose(axis, [p[:, 0], p[:, 1], p[:, 2]])
        snapped = np.stack([np.floor(u / pixel), np.floor(v / pixel), np.floor(depth / (pixel * 4))], axis=1) * pixel
        index = _layers(snapped / pattern.get("scale", 1.0), colours, pattern["cover"], seed, pattern.get("warp", 0.4), n)
    elif kind == "splinter":
        scale = pattern.get("scale", 1.0)
        stretch = pattern.get("stretch", 2.0)
        q = p / scale * np.array([1.0, 1.0, 1.0 / stretch])
        q = q + (fbm(q * 0.7, 2, seed)[:, None] - 0.5) * 0.25
        cell = _voronoi_cell(q, seed)
        weights = np.cumsum(pattern.get("weights") or [1.0 / len(colours)] * len(colours))
        index = np.searchsorted(weights / weights[-1], cell, side="right").clip(0, len(colours) - 1).astype(np.int32)
    elif kind == "tiger":
        period = pattern.get("period", 0.8)
        warped = p[:, 2] / period + (fbm(p * 0.8, 3, seed) - 0.5) * pattern.get("warp", 0.5) * 2 + p[:, 1] * 0.35
        phase = warped - np.floor(warped)
        width = pattern.get("width", 0.3) * (0.6 + 0.8 * fbm(p * 1.7 + 5.0, 2, seed + 3))
        index = np.zeros(n, dtype=np.int32)
        index[phase < width * 1.45] = 2 if len(colours) > 2 else 1
        index[phase < width] = 1
    elif kind == "hex":
        cell = pattern.get("cell", 0.3)
        u, v, axis = _dominant_plane(p, normal)
        cell_id, edge = _hex_grid(u / cell, v / cell)
        tint = _hash(cell_id[0], cell_id[1], axis.astype(np.int64), seed)
        index = (tint * len(colours)).astype(np.int32).clip(0, len(colours) - 1)
        line = edge < 0.06
        colour = colours[index]
        colour[line] = hex_linear(pattern.get("line", "#000000"))
        if glow:
            # Some cell outlines glow (two colours by cell); the rest are dark seams.
            lit = _hash(cell_id[0] + 7, cell_id[1] - 3, axis.astype(np.int64), seed + 5) < glow.get("share", 0.15)
            glow_mask = (edge < glow.get("width", 0.012) / cell * 2.5) & lit
            pick = (_hash(cell_id[0], cell_id[1], axis.astype(np.int64) + 9, seed) * len(glow["colours"])).astype(int)
            glow_mask = np.where(glow_mask, pick.clip(0, len(glow["colours"]) - 1) + 1, 0)
        return colour, glow_mask
    elif kind == "synthwave":
        index = (fbm(p * 0.6, 2, seed) > 0.62).astype(np.int32)
        colour = colours[index]
        low, high = pattern.get("band", (0.15, 0.5))
        sun = np.array([hex_linear(c) for c in pattern["sun"]])
        # Sunset band: horizontal stripes that get thicker towards the bottom, colour from top to bottom.
        t = np.clip((height - low) / (high - low), 0.0, 1.0)
        stripe_phase = (height * 22.0) % 1.0
        in_band = (height > low) & (height < high) & (stripe_phase > 0.15 + 0.55 * (1 - t))
        tc = (1 - t) * (len(sun) - 1)
        k = np.floor(tc).astype(int).clip(0, len(sun) - 2)
        frac = (tc - k)[:, None]
        band_colour = sun[k] * (1 - frac) + sun[k + 1] * frac
        colour = np.where(in_band[:, None], band_colour, colour)
        if glow:
            grid = pattern.get("grid", 0.5)
            u, v, _axis = _dominant_plane(p, normal)
            width = glow.get("width", 0.014) / grid
            du = np.abs((u / grid) - np.round(u / grid))
            dv = np.abs((v / grid) - np.round(v / grid))
            glow_mask = (((du < width) | (dv < width)) & ~in_band & (height < 0.75)).astype(np.int32)
        return colour, glow_mask
    elif kind == "livery":
        index = np.zeros(n, dtype=np.int32)
        inner, outer = pattern.get("stripe", (0.05, 0.13))
        # Twin stripes along the top centre line, a side band at a height share, a black lower belly.
        top = normal[:, 1] > 0.45
        ax = np.abs(p[:, 0])
        index[top & (ax > inner) & (ax < outer)] = 1
        centre, half = pattern.get("band", (0.42, 0.06))
        side = np.abs(normal[:, 0]) > 0.4
        index[side & (np.abs(height - centre) < half)] = 1
        index[side & (np.abs(height - centre - half * 1.7) < half * 0.35)] = 2
        index[(normal[:, 1] < -0.55)] = 2
    else:
        raise ValueError(f"Unknown skin pattern '{kind}'")
    return colours[index], glow_mask


def _voronoi_cell(q, seed):
    """Random value of the nearest Voronoi cell (3D, one feature point per unit cell)."""
    base = np.floor(q).astype(np.int64)
    best = np.full(len(q), np.inf, dtype=np.float32)
    value = np.zeros(len(q), dtype=np.float32)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                c = base + np.array([dx, dy, dz])
                feature = c + np.stack([_hash(c[:, 0], c[:, 1], c[:, 2], seed + k) for k in range(3)], axis=1)
                d = np.sum((q - feature) ** 2, axis=1)
                closer = d < best
                best[closer] = d[closer]
                value[closer] = _hash(c[closer, 0], c[closer, 1], c[closer, 2], seed + 99)
    return value


def _hex_grid(u, v):
    """Pointy hex grid with unit spacing: cell ids (two int arrays) and the distance to the nearest cell edge."""
    s = np.sqrt(3.0)
    q = (u * s / 3 - v / 3)
    r = v * 2 / 3
    x, z = q, r
    y = -x - z
    rx, ry, rz = np.round(x), np.round(y), np.round(z)
    dx, dy, dz = np.abs(rx - x), np.abs(ry - y), np.abs(rz - z)
    fix_x = (dx > dy) & (dx > dz)
    fix_y = ~fix_x & (dy > dz)
    rx = np.where(fix_x, -ry - rz, rx)
    ry = np.where(fix_y, -rx - rz, ry)
    rz = np.where(~fix_x & ~fix_y, -rx - ry, rz)
    # Centre of the cell, distance to the edge of a unit hexagon (0 at the edge, 0.5 at the centre).
    cu = s * (rx + rz / 2)
    cv = 1.5 * rz
    lu, lv = np.abs(u - cu), np.abs(v - cv)
    edge = 1.0 - np.maximum(lu * 2 / s, lu / s + lv)  # 0 on the rim, 1 in the middle (radius 1 hex)
    return (rx.astype(np.int64), rz.astype(np.int64)), edge * 0.5


# -- compose ---------------------------------------------------------------------------------------------------------

def prepare(surface):
    """Skin-independent noise fields, worked out once per texture set: paint blotches, chips, rain streaks."""
    p = surface["position"]
    surface["blotch"] = fbm(p * 0.9, 3, 77)
    surface["chips"] = fbm(p * 9.0, 3, 41)
    surface["streak"] = fbm(p * np.array([14.0, 1.2, 14.0], dtype=np.float32), 3, 23)


def compose(skin, surface, palette_rows, names, style_wear=1.0):
    """Paints one skin onto a baked surface.

    surface: dict of 1D arrays over the valid texels: position (n, 3) and normal (n, 3) in Unity model space, ao,
      edge, cavity (0..1), swatch (int), height (0..1 over the texture set), soot (0..1).
    palette_rows: per swatch (linear colour (3), linear emission (3), metallic, smoothness) after the style's
      overrides. names: swatch names by index.
    Returns linear colour (n, 3), emission (n, 3), metallic (n,), smoothness (n,)."""
    swatch = surface["swatch"]
    colour_table = np.array([row[0] for row in palette_rows], dtype=np.float32)
    glow_table = np.array([row[1] for row in palette_rows], dtype=np.float32)
    metal_table = np.array([row[2] for row in palette_rows], dtype=np.float32)
    smooth_table = np.array([row[3] for row in palette_rows], dtype=np.float32)
    colour = colour_table[swatch].copy()
    emission = glow_table[swatch].copy()
    metallic = metal_table[swatch].copy()
    smoothness = smooth_table[swatch].copy()
    index = {name: i for i, name in enumerate(names)}

    def where(group):
        return np.isin(swatch, [index[name] for name in group if name in index])

    hull = colour_table[index["hull"]]
    painted = where(PAINT + (PANELS if skin.get("panels") else ()))
    pattern = skin.get("pattern", {"type": "solid"})
    glow = skin.get("glow")
    rows = np.nonzero(painted)[0]
    if len(rows):
        base, glow_mask = _pattern(pattern, surface["position"][rows], surface["normal"][rows],
                                   surface["height"][rows], hull[None, :], glow)
        # Dark and light armour panels keep their contrast: a value shift (skin) or the style's own ratios.
        tone = np.ones(len(rows), dtype=np.float32)[:, None] * np.ones(3, dtype=np.float32)
        for name, key in (("hull_dark", "dark"), ("hull_light", "light")):
            ratio = skin.get(key)
            if ratio is None:
                ratio = colour_table[index[name]] / np.maximum(hull, 1e-4)
            tone[swatch[rows] == index[name]] = ratio
        colour[rows] = base * tone
        if skin.get("finish"):
            metallic[rows], smoothness[rows] = skin["finish"]
        if glow_mask is not None and glow:
            lit = glow_mask > 0
            glows = np.array([hex_linear(c) for c in glow["colours"]], dtype=np.float32)
            picked = glows[(glow_mask[lit] - 1).clip(0, len(glows) - 1)]
            colour[rows[lit]] = picked
            emission[rows[lit]] = picked
            smoothness[rows[lit]] = 0.8
    if skin.get("hazard"):
        marks = where(HAZARD)
        colour[marks] = hex_linear(skin["hazard"])
        emission[marks] = 0.0

    # Paint variation: large soft blotches (faded and touched-up paint).
    p = surface["position"]
    paint_zone = where(WEARS)
    colour[paint_zone] *= (0.92 + 0.16 * surface["blotch"][paint_zone])[:, None]

    # Wear: bare metal on edges and in chips near them (painted swatches only).
    wear = skin.get("wear", 1.0) * style_wear
    if wear > 0:
        chips = surface["chips"]
        edge = surface["edge"]
        bare_mask = paint_zone & ((edge * (0.6 + chips) > 1.15 - 0.45 * wear) |
                                  ((chips > 0.86 - 0.08 * wear) & (edge > 0.25)))
        colour[bare_mask] = hex_linear("#8f8d86") * (0.8 + 0.4 * chips[bare_mask])[:, None]
        metallic[bare_mask] = 0.85
        smoothness[bare_mask] = 0.42
        emission[bare_mask] = 0.0

    # Grime: dirt in the crevices, rain streaks running down, soot behind the exhausts.
    grime = skin.get("grime", 1.0) * (0.4 + 0.6 * style_wear)
    if grime > 0:
        dirt = hex_linear("#2b261f")
        streak = surface["streak"]
        amount = np.clip((1 - surface["cavity"]) * 1.4 * grime + np.clip(streak - 0.62, 0, 1) * 1.2 * grime, 0, 0.85)
        amount = np.where(emission.max(axis=1) > 0.05, 0.0, amount)
        colour = colour * (1 - amount[:, None]) + dirt * amount[:, None] * 0.6
        smoothness = smoothness * (1 - amount * 0.6)
    soot = surface["soot"] * (0.4 + 0.6 * style_wear)
    if soot.max() > 0:
        soot = np.where(emission.max(axis=1) > 0.05, 0.0, soot)
        colour = colour * (1 - soot[:, None] * 0.85) + hex_linear("#0f0e0d") * soot[:, None] * 0.85
        smoothness = smoothness * (1 - soot * 0.7)
        metallic = metallic * (1 - soot * 0.8)

    # A little of the occlusion in the colour (URP uses the occlusion map for ambient light only).
    colour *= (0.72 + 0.28 * surface["ao"])[:, None]
    return colour, emission, metallic, smoothness
