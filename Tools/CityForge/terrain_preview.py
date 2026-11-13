"""
Terrain preview images without Blender (plan 6c terrain): shaded relief before and after erosion, 3/4 views (a
voxel-space renderer: rays per screen column over the height field), the detail masks, one chunk at its 3 LODs.

    Tools/CityForge/.venv/bin/python Tools/CityForge/terrain_preview.py alpine [--out DIR]

The city is a flat rectangle of the style's layout size (no buildings, no roads out). About 1 to 2 minutes per style.
"""
import math, os, sys
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cityforge import terrain, terrain_biomes
from cityforge.terrain import resample, smoothstep
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from PIL import Image


def hexrgb(c):
    c = c.lstrip("#")
    return np.array([int(c[i:i + 2], 16) / 255.0 for i in (0, 2, 4)])


def shade(h, step, sun=(315.0, 40.0)):
    gz, gx = np.gradient(h, step)
    az, alt = map(math.radians, sun)
    # light dir in (x, y up, z): from azimuth (x east, z north), image row 0 = +Z after flip
    lx, lz, ly = math.sin(az) * math.cos(alt), math.cos(az) * math.cos(alt), math.sin(alt)
    n = np.stack([-gx, np.ones_like(h), -gz], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    lam = np.clip(n[..., 0] * lx + n[..., 1] * ly + n[..., 2] * lz, 0, 1)
    return 0.28 + 0.85 * lam, n


def colour(t, h, step, detail=None, eroded=True):
    """Biome-like colour per texel: ground / sediment / rock by slope / snow / made ground."""
    b = terrain_biomes.BIOMES[t.style_biome]
    ground, ground2 = hexrgb(b["ground"][0]), hexrgb(b["ground"][1])
    rock, rock2, cliff = hexrgb(b["rock"][0]), hexrgb(b["rock"][2]), hexrgb(b["cliff"])
    sed = hexrgb(b["sediment"]); scree = hexrgb(b["scree"])
    gz, gx = np.gradient(h, step)
    deg = np.degrees(np.arctan(np.hypot(gx, gz)))
    n = h.shape[0]
    noise = terrain.fbm(5, *np.meshgrid(np.linspace(0, 3072, n), np.linspace(0, 3072, n)), 90.0, 3)
    c = ground[None, None] * (1 - (0.5 + 0.5 * noise))[..., None] + ground2[None, None] * (0.5 + 0.5 * noise)[..., None]
    bands = 0.5 + 0.08 * np.sin(h / b["band"] * math.pi) + 0.35 * noise
    rockc = rock * (1 - bands)[..., None] + rock2 * bands[..., None]
    r = smoothstep(24.0, 40.0, deg)[..., None]
    c = c * (1 - r) + rockc * r
    k = smoothstep(55.0, 70.0, deg)[..., None]
    c = c * (1 - k) + cliff * k
    if detail is not None:
        slope, sediment, snow, flow = (detail[..., i] for i in range(4))
        s = np.clip(sediment * 1.4, 0, 1)[..., None] * (1 - r)
        c = c * (1 - s * 0.8) + sed * s * 0.8
        f = smoothstep(0.45, 0.9, flow)[..., None]
        c = c * (1 - 0.35 * f)
        sn = snow[..., None]
        c = c * (1 - sn) + hexrgb("#eef3f6") * sn
    return np.clip(c, 0, 1)


def made_mask(t, n):
    return np.clip(t.surface()[0][..., 2], 0, 1) if n == t.values["surface"]["resolution"] else None


def texel_axis(t, res):
    size = t.values["size"]
    step = size / res
    return (np.arange(res) + 0.5) * step - size / 2, step


def fields(t, res=2048):
    axis, step = texel_axis(t, res)
    before = resample(t.h_before, t.step, t.values["size"], axis)
    after = resample(t.fine["height"], t.fine["step"], t.values["size"], axis) if t.fine is not None else before
    return before, after, step


def image_of(t, h, step, detail):
    lam, _n = shade(h, step)
    c = colour(t, h, step, detail) * lam[..., None]
    made = t.surface()[0][..., 2][..., None]
    concrete = hexrgb("#6d6c68") * lam[..., None]
    c = c * (1 - made) + concrete * made
    return np.clip(c, 0, 1)


def relief(t, name, path):
    before, after, step = fields(t)
    d = t.detail_masks()
    nod = d.copy(); nod[..., 1] = 0; nod[..., 3] = 0
    a = image_of(t, before, step, nod)
    b = image_of(t, after, step, d)
    pool = lambda i: i.reshape(i.shape[0] // 2, 2, i.shape[1] // 2, 2, 3).mean(axis=(1, 3))
    a, b = pool(a), pool(b)
    fig, ax = plt.subplots(1, 2, figsize=(16, 8.4), dpi=110)
    for axis, img, title in zip(ax, (a, b), ("before erosion", "after erosion")):
        axis.imshow(img[::-1], extent=(-1536, 1536, -1536, 1536), interpolation="antialiased")
        axis.set_title(f"{name}: {title}", fontsize=13)
        axis.set_xlabel("x (m)"); axis.set_ylabel("z (m)")
    fig.tight_layout(); fig.savefig(path); plt.close(fig)


def crop_compare(t, name, path, centre, half=300.0):
    before, after, step = fields(t)
    d = t.detail_masks()
    nod = d.copy(); nod[..., 1] = 0; nod[..., 3] = 0
    res = before.shape[0]
    def crop(a):
        i0 = int((centre[1] - half + 1536) / step); i1 = int((centre[1] + half + 1536) / step)
        j0 = int((centre[0] - half + 1536) / step); j1 = int((centre[0] + half + 1536) / step)
        return a[i0:i1, j0:j1]
    a = crop(image_of(t, before, step, nod)); b = crop(image_of(t, after, step, d))
    fig, ax = plt.subplots(1, 2, figsize=(16, 8.4), dpi=110)
    ext = (centre[0] - half, centre[0] + half, centre[1] - half, centre[1] + half)
    for axis, img, title in zip(ax, (a, b), ("before erosion", "after erosion")):
        axis.imshow(img[::-1], extent=ext)
        axis.set_title(f"{name}: {title} (600 m close-up, 1.5 m texels)", fontsize=12)
    fig.tight_layout(); fig.savefig(path); plt.close(fig)


def voxel_view(t, path, cam, yaw_deg, pitch=0.42, width=1600, height=900, far=4200.0, fov=70.0, title=None,
               fog=(300.0, 3000.0)):
    """Voxel-space render: march rays per screen column front to back with an occlusion buffer."""
    before, h, step = fields(t)
    d = t.detail_masks()
    img = image_of(t, h, step, d)
    size = t.values["size"]
    res = h.shape[0]
    # Far scenery beyond the map: the backdrop heights (coarse), plain rock colour
    sky_top, sky_low = hexrgb("#7f9bb5"), hexrgb("#cfd8de")
    haze = hexrgb("#b9c4cc")
    out = np.zeros((height, width, 3))
    rows = np.arange(height)[:, None]
    out[:] = sky_top * (1 - rows / height)[..., None] + sky_low * (rows / height)[..., None]
    ybuf = np.full(width, height, dtype=np.int64)
    cx, cy, cz = cam
    yaw = math.radians(yaw_deg)
    fx, fz = math.sin(yaw), math.cos(yaw)
    rx, rz = math.cos(yaw), -math.sin(yaw)
    half = math.tan(math.radians(fov / 2))
    cols = (np.arange(width) - width / 2 + 0.5) / (width / 2) * half
    focal = (width / 2) / half
    horizon = height * pitch
    dist = 1.0
    dz = 0.5
    while dist < far:
        px = cx + fx * dist + rx * cols * dist
        pz = cz + fz * dist + rz * cols * dist
        inside = (np.abs(px) < size / 2 - 1) & (np.abs(pz) < size / 2 - 1)
        j = np.clip(np.round((px + size / 2) / step - 0.5).astype(np.int64), 0, res - 1)
        i = np.clip(np.round((pz + size / 2) / step - 0.5).astype(np.int64), 0, res - 1)
        hh = np.where(inside, h[i, j], -1e4)
        col = img[i, j]
        y = ((cy - hh) / dist * focal + horizon).astype(np.int64)
        y = np.clip(y, 0, height)
        draw = (y < ybuf) & inside
        if draw.any():
            f = 1 - np.exp(-max(dist - fog[0], 0.0) / fog[1])
            col = col * (1 - f) + haze * f
            idx = np.nonzero(draw)[0]
            top, bot = y[idx], ybuf[idx]
            mask = (rows >= top[None, :]) & (rows < bot[None, :])
            sub = out[:, idx]
            sub[mask] = np.broadcast_to(col[idx][None], sub.shape)[mask]
            out[:, idx] = sub
            ybuf[idx] = top
        dist += dz
        dz = max(0.5, dist * 0.004)
    Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8)).save(path)
    if title:
        fig = plt.figure(figsize=(width / 100, height / 100), dpi=100)
        plt.imshow(np.asarray(Image.open(path))); plt.axis("off"); plt.title(title, fontsize=14)
        fig.tight_layout(); fig.savefig(path); plt.close(fig)


def mask_grid(t, name, path):
    d = t.detail_masks()
    fig, ax = plt.subplots(2, 2, figsize=(14, 14), dpi=100)
    titles = ("slope (0..60 deg)", "sediment left by erosion", "snow", "water flow (eroded)")
    cmaps = ("magma", "copper", "Blues_r", "viridis")
    for k, axis in enumerate(ax.ravel()):
        axis.imshow(d[::-1, :, k], cmap=cmaps[k], vmin=0, vmax=1, extent=(-1536, 1536, -1536, 1536))
        axis.set_title(f"{name}: {titles[k]}", fontsize=13)
    fig.tight_layout(); fig.savefig(path); plt.close(fig)


def chunk_wire(t, name, path, chunk=None):
    """Top-down close-up of one chunk at its three LODs: shaded height with the mesh grid drawn on it."""
    plan = terrain.chunk_plan(t)
    if chunk is None:
        fine = [p for p in plan if p[7][0] == min(q[7][0] for q in plan)]
        chunk = max(fine, key=lambda p: float(np.ptp(t.h[p[3]:p[4] + 1, p[5]:p[6] + 1])))
    nm, _ci, _cj, z0, z1, x0, x1, factors = chunk
    fig, axes = plt.subplots(1, 3, figsize=(21, 7.6), dpi=100)
    ext = (t.x[0, x0], t.x[0, x1], t.z[z0, 0], t.z[z1, 0])
    for k, (ax, f) in enumerate(zip(axes, factors)):
        iz, ix = terrain._steps(z0, z1, f), terrain._steps(x0, x1, f)
        H = t.h[np.ix_(iz, ix)]
        # Shade the LOD mesh itself (bilinear up to the field grid), so each LOD shows what it really keeps.
        rows = np.stack([np.interp(np.arange(x0, x1 + 1), ix, H[r]) for r in range(len(iz))])
        full = np.stack([np.interp(np.arange(z0, z1 + 1), iz, rows[:, c]) for c in range(rows.shape[1])], axis=1)
        lam, _n = shade(full, t.step)
        ax.imshow(lam[::-1], cmap="gray", vmin=0, vmax=1.1, extent=ext, interpolation="bilinear")
        for v in t.x[0, ix]:
            ax.axvline(v, color=(0.9, 0.35, 0.1), lw=0.35 if f == factors[0] else 0.6, alpha=0.8)
        for v in t.z[iz, 0]:
            ax.axhline(v, color=(0.9, 0.35, 0.1), lw=0.35 if f == factors[0] else 0.6, alpha=0.8)
        tris = terrain.chunk_triangles(z0, z1, x0, x1, f)
        err = terrain._coarse_error(t.h, z0, z1, x0, x1, f)
        ax.set_title(f"{nm} LOD{k}: {t.step * f:.0f} m grid, {tris:,} tris, mean error {err:.2f} m", fontsize=12)
        ax.set_xlabel("x (m)")
    fig.suptitle(f"{name}: one chunk ({terrain.CHUNK_SIZE:.0f} m) at its three LODs (top view, height {np.ptp(t.h[z0:z1 + 1, x0:x1 + 1]):.0f} m range)", fontsize=14)
    fig.tight_layout(); fig.savefig(path); plt.close(fig)
    return chunk


# (x, z, height above ground, yaw degrees (0 = +Z), horizon share) per style; other styles look from the south-west.
CAMERAS = {"alpine": [(-150.0, -900.0, 330.0, 8.0, 0.3), (1450.0, 150.0, 240.0, 262.0, 0.32)],
           "ravine": [(-1450.0, -450.0, 200.0, 112.0, 0.3), (1100.0, -1350.0, 150.0, 318.0, 0.33)]}


def main(argv=None):
    import argparse
    from cityforge import layout, styles

    parser = argparse.ArgumentParser()
    parser.add_argument("style")
    parser.add_argument("--out", default=os.path.join(os.path.dirname(os.path.abspath(__file__)), "previews", "terrain"))
    args = parser.parse_args(argv)
    style = styles.get(args.style)
    w, d = layout.settings(style)["city"]
    t = terrain.Terrain(style, (-w / 2, w / 2, -d / 2, d / 2), -d / 2 + d * layout.CHANNEL_SHARE)
    t.make_markers()
    t.style_biome = style.get("biome", "desert")
    print(args.style, {k: round(v, 1) for k, v in t.timings.items()}, "audit", t.audit())
    os.makedirs(args.out, exist_ok=True)
    out = lambda suffix: os.path.join(args.out, f"{args.style}_{suffix}.png")
    relief(t, args.style, out("relief"))
    for k, (x, z, up, yaw, pitch) in enumerate(CAMERAS.get(args.style, [(-1300.0, -1300.0, 300.0, 45.0, 0.3)])):
        voxel_view(t, out(f"view{k + 1}"), (x, float(t.sample(x, z)) + up, z), yaw, pitch=pitch)
    mask_grid(t, args.style, out("masks"))
    chunk_wire(t, args.style, out("chunk"))


if __name__ == "__main__":
    main()
