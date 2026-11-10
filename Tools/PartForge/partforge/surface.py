"""
Fine surface detail, worked out per texel in numpy from the baked position and normal (model space, Unity metres):

- panel lines: an airframe's seams (frames along the hull, stringers up the sides, ribs across the top), with some
  segments left out so the grid is not regular, and a row of rivets on both sides of every seam;
- markings: a stencilled tail code, a unit number under the cockpit, a danger band before the tail.

Panel lines and rivets go into the normal map (the gradient of a height field, turned into tangent space with the baked
UV tangent) and into the dirt and wear masks (grime sits in the seams, rivet heads rub bare). Markings are not paint
in the colour map: they change the texel's palette swatch (light armour tone or warning paint), so every skin, baked or
painted by the runtime shader, shows them in its own colours with its own wear on top.
"""
import numpy as np

# Seam spacing (m) across x (ribs), up y (stringers), along z (frames); the ribs are offset, so none runs on the
# centre line.
RIB, STRINGER, FRAME = 0.5, 0.41, 0.62
GROOVE = 0.0055         # seam half width (m)
GROOVE_DEPTH = 0.0012
RIVET_PITCH, RIVET_OFFSET, RIVET_RADIUS, RIVET_HEIGHT = 0.07, 0.024, 0.0075, 0.0009
KEEP = 0.74             # share of seam segments that exist
STRENGTH = 2.2          # normal-map exaggeration of the height field (reads at game distance)
PAINTED = ("hull", "hull_dark", "hull_light", "primer", "concrete", "rust")


def _smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def _hash(ix, iy, iz, seed):
    from . import skins
    return skins._hash(ix, iy, iz, seed)


def _family(p, n, axis, spacing, offset, seed):
    """One seam family (planes of constant p[axis]): distance to the nearest kept seam (m, large where the face looks
    along the axis or the segment is left out) and the rivet dome height (0..1) beside it."""
    others = [k for k in range(3) if k != axis]
    spacings = (RIB, STRINGER, FRAME)
    t = (p[:, axis] + offset) / spacing
    line = np.round(t)
    distance = np.abs(t - line) * spacing
    cells = [np.floor(p[:, k] / (spacings[k] * 1.0)).astype(np.int64) for k in others]
    keep = _hash(line.astype(np.int64) + 91 * axis, cells[0], cells[1], seed) < KEEP
    facing = np.abs(n[:, axis]) > 0.6
    distance = np.where(keep & ~facing, distance, 9.0)
    # Rivets: along the seam, on the in-plane axis (the one of the other two the face looks along least).
    a, c = others
    along = np.where(np.abs(n[:, a]) < np.abs(n[:, c]), p[:, a], p[:, c])
    s = along / RIVET_PITCH
    ds = (s - np.round(s)) * RIVET_PITCH
    dr = distance - RIVET_OFFSET
    r2 = (ds * ds + dr * dr) / (RIVET_RADIUS * RIVET_RADIUS)
    dome = np.sqrt(np.clip(1.0 - r2, 0.0, 1.0))
    return distance, dome


def panel_fields(p, n, seed=7):
    """groove (0..1 in a seam), rivet (0..1 dome) at points p with face normals n."""
    distance = np.full(len(p), 9.0, dtype=np.float32)
    rivet = np.zeros(len(p), dtype=np.float32)
    for axis, spacing, offset in ((0, RIB, RIB / 2), (1, STRINGER, 0.11), (2, FRAME, 0.0)):
        d, dome = _family(p, n, axis, spacing, offset, seed)
        distance = np.minimum(distance, d)
        rivet = np.maximum(rivet, dome)
    groove = 1.0 - _smoothstep(GROOVE * 0.5, GROOVE * 1.6, distance)
    return groove.astype(np.float32), (rivet * (groove < 0.01)).astype(np.float32)


def _height(p, n, seed):
    groove, rivet = panel_fields(p, n, seed)
    return rivet * RIVET_HEIGHT - groove * GROOVE_DEPTH


def apply(surface, names, tangent_map, valid, normal_b, tangent_b, texel, seed=7, painted_names=PAINTED):
    """Adds the seam and rivet masks to the surface (surface["seam"], surface["rivet"]: skins.prepare uses them) and
    bends the baked tangent-space normal map (flat (n, 4) array over all texels, changed in place at `valid`).

    normal_b, tangent_b: the baked normal and UV tangent over the valid texels in Blender space; texel: size of one
    texel on the surface (m), the step of the height gradient."""
    p, n = surface["position"], surface["normal"]
    index = {name: i for i, name in enumerate(names)}
    painted = np.isin(surface["swatch"], [index[name] for name in painted_names])
    # Seams only where the surface is wide enough to be a skin panel, not on small fittings: the edge mask is 0 there.
    groove, rivet = panel_fields(p, n, seed)
    groove, rivet = groove * painted, rivet * painted
    surface["seam"], surface["rivet"] = groove, rivet

    step = max(texel * 1.5, 0.003)  # wider than a texel: seams at a slant in the map do not stair-step
    gradient = np.zeros_like(p)
    for axis in range(3):
        offset = np.zeros(3, dtype=np.float32)
        offset[axis] = step
        gradient[:, axis] = (_height(p + offset, n, seed) - _height(p - offset, n, seed)) / (2 * step)
    gradient *= painted[:, None] * STRENGTH
    # Unity -> Blender (the inverse of texture._unity), then into the tangent frame of the bake.
    g = np.stack([-gradient[:, 0], -gradient[:, 2], gradient[:, 1]], axis=1)
    t = tangent_b - normal_b * np.sum(tangent_b * normal_b, axis=1, keepdims=True)
    t /= np.maximum(np.linalg.norm(t, axis=1, keepdims=True), 1e-6)
    bitangent = np.cross(normal_b, t)
    dx, dy = -np.sum(g * t, axis=1), -np.sum(g * bitangent, axis=1)
    base = tangent_map[valid, :3] * 2.0 - 1.0
    bent = np.stack([base[:, 0] + dx, base[:, 1] + dy, base[:, 2]], axis=1)
    bent /= np.maximum(np.linalg.norm(bent, axis=1, keepdims=True), 1e-6)
    tangent_map[valid, :3] = bent * 0.5 + 0.5


# -- markings -------------------------------------------------------------------------------------------------------

# 5 x 7 stencil glyphs, rows from the top; the gaps of a real stencil plate are the missing dots.
FONT = {
    "0": ("01110", "10001", "10011", "10101", "11001", "10001", "01110"),
    "1": ("00100", "01100", "00100", "00100", "00100", "00100", "01110"),
    "2": ("01110", "10001", "00001", "00110", "01000", "10000", "11111"),
    "3": ("11110", "00001", "00001", "01110", "00001", "00001", "11110"),
    "4": ("00010", "00110", "01010", "10010", "11111", "00010", "00010"),
    "5": ("11111", "10000", "11110", "00001", "00001", "10001", "01110"),
    "6": ("01110", "10000", "10000", "11110", "10001", "10001", "01110"),
    "7": ("11111", "00001", "00010", "00100", "01000", "01000", "01000"),
    "8": ("01110", "10001", "10001", "01110", "10001", "10001", "01110"),
    "9": ("01110", "10001", "10001", "01111", "00001", "00001", "01110"),
    "A": ("01110", "10001", "10001", "11111", "10001", "10001", "10001"),
    "D": ("11110", "10001", "10001", "10001", "10001", "10001", "11110"),
    "G": ("01110", "10001", "10000", "10111", "10001", "10001", "01110"),
    "K": ("10001", "10010", "10100", "11000", "10100", "10010", "10001"),
    "L": ("10000", "10000", "10000", "10000", "10000", "10000", "11111"),
    "N": ("10001", "11001", "10101", "10011", "10001", "10001", "10001"),
    "R": ("11110", "10001", "10001", "11110", "10100", "10010", "10001"),
    "S": ("01111", "10000", "10000", "01110", "00001", "00001", "11110"),
    "T": ("11111", "00100", "00100", "00100", "00100", "00100", "00100"),
    "V": ("10001", "10001", "10001", "10001", "10001", "01010", "00100"),
    "X": ("10001", "10001", "01010", "00100", "01010", "10001", "10001"),
    "-": ("00000", "00000", "00000", "01110", "00000", "00000", "00000"),
    " ": ("00000",) * 7,
}


def _text_mask(u, v, text, height):
    """True where (u, v) is inside the stencilled text: u runs along the text from its start, v up from its foot."""
    cell = height / 7.0
    column, row = np.floor(u / cell).astype(np.int64), np.floor(v / cell).astype(np.int64)
    glyph, inside = column // 6, column % 6
    table = np.zeros((len(text), 7, 6), dtype=bool)
    for k, char in enumerate(text):
        for r, line in enumerate(FONT.get(char, FONT[" "])):
            table[k, 6 - r, :5] = [c == "1" for c in line]
    ok = (glyph >= 0) & (glyph < len(text)) & (row >= 0) & (row < 7)
    mask = np.zeros(len(u), dtype=bool)
    mask[ok] = table[glyph[ok], row[ok], inside[ok]]
    return mask


def markings(surface, names, hazard, code, number):
    """Stencils on a chassis (changes surface["swatch"]): the tail code on both sides of the boom, the unit number
    on both cheeks under the cockpit (light armour tone), a danger band of slanted stripes before the tail (warning
    paint). Each is placed from the surface itself (where the hull sides are), so it follows a new chassis shape."""
    p, n, swatch = surface["position"], surface["normal"], surface["swatch"]
    hull, light, warn = names.index("hull"), names.index("hull_light"), names.index(hazard)
    z_min, z_max = float(p[:, 2].min()), float(p[:, 2].max())
    flat = surface["edge"] < 0.15

    def side(sign):
        return (n[:, 0] * sign > 0.82) & (swatch == hull) & flat

    def stencil(text, height, z0, z1, target, lift=0.0):
        width = len(text) * 6 * height / 7.0
        for sign in (1.0, -1.0):
            zone = side(sign) & (p[:, 2] > z0) & (p[:, 2] < z1)
            if zone.sum() < 200:
                continue
            centre_z = (z0 + z1) / 2
            y = float(np.median(p[zone, 1])) + lift
            # Seen from outside, the nose is on the right on the right side: the text runs forward there, back on
            # the left side.
            u = (p[:, 2] - centre_z) * sign + width / 2
            v = p[:, 1] - (y - height / 2)
            swatch[zone & _text_mask(u, v, text, height)] = target

    stencil(code, 0.2, z_min + 1.9, min(z_min + 4.3, -2.2), light)
    stencil(number, 0.3, 0.5, 2.2, light, lift=-0.12)
    for sign in (1.0, -1.0):
        zone = side(sign) & (p[:, 2] > z_min + 1.15) & (p[:, 2] < z_min + 1.6)
        stripe = ((p[:, 2] + p[:, 1]) / 0.13) % 1.0 < 0.5
        swatch[zone & stripe] = warn
