"""
Erosion for the terrain height field (plan 6c "Plan: terrain"), numpy only, deterministic from the seed.

- Thermal: material slides down where the slope is steeper than the talus angle (scree slopes, no needle peaks).
- Hydraulic: many water droplets run downhill at the same time (vectorised). A droplet with speed and water can carry
  sediment: it cuts where it can carry more, and drops sediment where it slows down or the ground flattens. This makes
  gullies, valley floors and fans. (Droplet model as in Hans Theobald Beyer's 2015 thesis and its many ports.)

Runs coarse to fine: a pass on a coarse grid cuts the big valleys, then the result is resampled and the next pass adds
smaller gullies. Every pass returns the water that ran over each cell (flow) and the sediment left there.

`keep` (0..1 per cell) protects ground: 0 = never changed (city floor, roads, channel, flat landing tops).
"""
import math

import numpy as np

DEFAULTS = dict(
    # (grid step m, droplets per cell, droplet life in steps) per pass, coarse to fine. The last step is the output.
    # Tuned 13 Nov 2026 on the alpine map: median change about 2 m, deepest gullies about 20 m (stronger values cut
    # every slope into needles: median slope 54 degrees).
    passes=((6.0, 0.8, 80), (3.0, 0.25, 50), (1.5, 0.1, 30)),
    inertia=0.3,        # how much a droplet keeps its direction (0..1)
    capacity=3.0,       # sediment a droplet can carry, per unit of speed x water x drop
    min_slope=0.005,    # capacity floor (so flat ground still carries a little)
    erode=0.08,         # share of the free capacity cut per step
    deposit=0.3,        # share of the extra sediment dropped per step
    evaporate=0.025,    # water lost per step
    gravity=8.0,
    max_cut=0.25,       # most a droplet cuts in one step, in metres x (grid step / 6 m)
    talus=38.0,         # thermal: steepest loose slope (degrees)
    cliff=58.0,         # thermal: rock steeper than this sheds material too (no spikes); between talus and cliff it holds
    thermal=(10, 25),   # thermal iterations before and after the hydraulic pass
    smooth=1,           # blur passes on each pass's change (removes droplet speckle, keeps gullies wider than 3 cells)
    strength=1.0,       # scales cut and deposit (0 = off)
)


def settings(values):
    out = dict(DEFAULTS)
    out.update(values or {})
    return out


def _bilinear(h, x, z):
    """Height and gradient (per cell) at fractional cell coordinates (x along columns, z along rows)."""
    n = h.shape[0]
    ix = np.clip(x.astype(np.int64), 0, n - 2)
    iz = np.clip(z.astype(np.int64), 0, n - 2)
    fx, fz = x - ix, z - iz
    a = h[iz, ix]
    b = h[iz, ix + 1]
    c = h[iz + 1, ix]
    d = h[iz + 1, ix + 1]
    height = a * (1 - fx) * (1 - fz) + b * fx * (1 - fz) + c * (1 - fx) * fz + d * fx * fz
    gx = (b - a) * (1 - fz) + (d - c) * fz
    gz = (c - a) * (1 - fx) + (d - b) * fx
    return height, gx, gz, ix, iz, fx, fz


def thermal(h, step, keep, talus, cliff, iterations, rate=0.5):
    """Move material from each cell to lower neighbours (4 directions) where the drop is over the talus slope. Rock
    between `talus` and `cliff` degrees is held where keep_rock says so (here: everywhere the field is steeper than the
    talus angle already before the pass, so cliffs stay; only loose slopes and spikes settle)."""
    if iterations <= 0:
        return h
    loose = math.tan(math.radians(talus)) * step
    hard = math.tan(math.radians(cliff)) * step
    gz, gx = np.gradient(h, step)
    steep = np.degrees(np.arctan(np.hypot(gx, gz)))
    limit = np.where(steep > talus + 4.0, hard, loose)
    h = h.copy()
    for _ in range(iterations):
        padded = np.pad(h, 1, mode="edge")
        moves = []
        for dz, dx in ((-1, 0), (1, 0), (0, -1), (0, 1)):
            nb = padded[1 + dz:1 + dz + h.shape[0], 1 + dx:1 + dx + h.shape[1]]
            moves.append(np.maximum(h - nb - limit, 0.0))
        total = sum(moves)
        biggest = np.maximum.reduce(moves)
        out = rate * 0.5 * biggest * keep
        share = np.where(total > 0, out / np.maximum(total, 1e-9), 0.0)
        delta = -out
        for (dz, dx), move in zip(((-1, 0), (1, 0), (0, -1), (0, 1)), moves):
            give = np.pad(move * share, 1)
            delta += give[1 - dz:1 - dz + h.shape[0], 1 - dx:1 - dx + h.shape[1]] * keep
        h += delta
    return h


def hydraulic(h, step, keep, rng, cfg, droplets_per_cell, life, batch=200_000):
    """Droplet erosion on grid h (step m). Returns (new h, water per cell, sediment left per cell)."""
    n = h.shape[0]
    cells = n * n
    h = h.copy()
    flat_h = h.ravel()
    keep_flat = keep.ravel()
    flow = np.zeros(cells)
    left = np.zeros(cells)
    total = int(droplets_per_cell * cells)
    scale = step / 6.0                       # heights per cell scale with the grid step
    max_cut = cfg["max_cut"] * scale
    strength = cfg["strength"]
    # Brush for cutting: 3 x 3, weights sum to 1 (a wider cut than the 4 bilinear corners, fewer pits).
    brush = [(dz, dx, w) for dz, dx, w in [(0, 0, 4), (-1, 0, 2), (1, 0, 2), (0, -1, 2), (0, 1, 2),
                                           (-1, -1, 1), (-1, 1, 1), (1, -1, 1), (1, 1, 1)]]
    brush_sum = sum(w for _a, _b, w in brush)
    done = 0
    while done < total:
        count = min(batch, total - done)
        done += count
        x = rng.uniform(1.0, n - 2.0, count)
        z = rng.uniform(1.0, n - 2.0, count)
        dx = np.zeros(count)
        dz = np.zeros(count)
        speed = np.ones(count)
        water = np.ones(count)
        sediment = np.zeros(count)
        alive = np.ones(count, bool)
        for _ in range(life):
            idx = np.nonzero(alive)[0]
            if idx.size == 0:
                break
            px, pz = x[idx], z[idx]
            height, gx, gz, ix, iz, fx, fz = _bilinear(h, px, pz)
            ndx = dx[idx] * cfg["inertia"] - gx * (1 - cfg["inertia"])
            ndz = dz[idx] * cfg["inertia"] - gz * (1 - cfg["inertia"])
            length = np.hypot(ndx, ndz)
            stuck = length < 1e-9
            length = np.where(stuck, 1.0, length)
            ndx, ndz = ndx / length, ndz / length
            nx, nz = px + ndx, pz + ndz
            out = stuck | (nx < 1) | (nx > n - 2) | (nz < 1) | (nz > n - 2)
            new_height = _bilinear(h, np.clip(nx, 0, n - 1.001), np.clip(nz, 0, n - 1.001))[0]
            drop = new_height - height                     # > 0 = uphill
            sed, spd, wat = sediment[idx], speed[idx], water[idx]
            cap = np.maximum(-drop / scale, cfg["min_slope"]) * spd * wat * cfg["capacity"] * scale
            depositing = (sed > cap) | (drop > 0)
            # Deposit: uphill -> fill the pit (up to the drop), else drop a share of the extra.
            put = np.where(drop > 0, np.minimum(drop, sed), (sed - cap) * cfg["deposit"])
            put = np.where(depositing, np.maximum(put, 0.0), 0.0) * strength
            # Cut: a share of the free capacity, never more than the drop (no holes), at most max_cut.
            cut = np.where(depositing, 0.0, np.minimum(np.minimum((cap - sed) * cfg["erode"], -drop), max_cut))
            cut = np.maximum(cut, 0.0) * strength
            corner = iz * n + ix
            cell = corner + np.where(fz >= 0.5, n, 0) + np.where(fx >= 0.5, 1, 0)
            k_here = keep_flat[cell]
            put = put * k_here
            cut = cut * k_here
            # One bincount per step for all height changes.
            index_parts, weight_parts = [], []
            for off, w in ((0, (1 - fx) * (1 - fz)), (1, fx * (1 - fz)), (n, (1 - fx) * fz), (n + 1, fx * fz)):
                index_parts.append(corner + off)
                weight_parts.append(put * w * keep_flat[corner + off])
            for bz, bx, w in brush:
                target = np.clip(iz + bz, 0, n - 1) * n + np.clip(ix + bx, 0, n - 1)
                index_parts.append(target)
                weight_parts.append(-cut * w / brush_sum * keep_flat[target])
            change = np.bincount(np.concatenate(index_parts), np.concatenate(weight_parts), minlength=cells)
            flat_h += change
            left += np.maximum(change, 0.0)
            flow += np.bincount(cell, wat, minlength=cells)
            sediment[idx] = sed - put + cut
            speed[idx] = np.sqrt(np.maximum(spd * spd - drop / scale * cfg["gravity"], 0.0)) + 1e-3
            water[idx] = wat * (1 - cfg["evaporate"])
            dx[idx], dz[idx] = ndx, ndz
            x[idx], z[idx] = nx, nz
            alive[idx[out]] = False
    return h, flow.reshape(n, n), left.reshape(n, n)


def _blur(a, passes):
    for _ in range(passes):
        p = np.pad(a, 1, mode="edge")
        a = (4 * a + 2 * (p[:-2, 1:-1] + p[2:, 1:-1] + p[1:-1, :-2] + p[1:-1, 2:])
             + p[:-2, :-2] + p[:-2, 2:] + p[2:, :-2] + p[2:, 2:]) / 16
    return a


def _grid_resample(a, size, step):
    """Node grid (centred square of side size) at a new step: Catmull-Rom (terrain.resample)."""
    from .terrain import resample

    count = int(round(size / step)) + 1
    axis = np.linspace(-size / 2, size / 2, count)
    old = size / (a.shape[0] - 1)
    return resample(a, old, size, axis)


def erode(h, step, size, keep, seed, values=None, log=None):
    """Erode field h (node grid, step m) coarse to fine. Returns dict(height=fine field at the last pass step,
    step=that step, flow=water per cell (normalised 0..1, log scale), sediment=0..1, before=h resampled to the fine
    step). The changes are worked out per pass and added back on top, so the input shapes stay (only cut and filled)."""
    cfg = settings(values)
    rng = np.random.default_rng(seed)
    before_fine = None
    current = h
    current_step = step
    flow = sediment = None
    for k, (pass_step, density, life) in enumerate(cfg["passes"]):
        work = _grid_resample(current, size, pass_step) if abs(pass_step - current_step) > 1e-6 else current.copy()
        keep_here = np.clip(_grid_resample(keep, size, pass_step) if keep.shape != work.shape else keep, 0.0, 1.0)
        if k == len(cfg["passes"]) - 1:
            before_fine = _grid_resample(h, size, pass_step) if h.shape != work.shape else h.copy()
        start = work
        work = thermal(work, pass_step, keep_here, cfg["talus"], cfg["cliff"], cfg["thermal"][0])
        work, water, left = hydraulic(work, pass_step, keep_here, rng, cfg, density, life)
        if cfg["smooth"]:
            work = start + _blur(work - start, cfg["smooth"]) * keep_here
        work = thermal(work, pass_step, keep_here, cfg["talus"], cfg["cliff"], cfg["thermal"][1])
        # Flow and sediment build up over the passes (coarse ones resampled up), per unit area.
        water = water / max(density, 1e-6)
        if flow is not None:
            flow = np.maximum(_grid_resample(flow, size, pass_step), 0.0) + water
            sediment = np.maximum(_grid_resample(sediment, size, pass_step), 0.0) + left
        else:
            flow, sediment = water, left
        current, current_step = work, pass_step
        if log:
            log(f"erosion pass {k + 1}: {pass_step} m grid, {work.shape[0]}^2 cells, {int(density * work.size)} droplets")
    flow_n = np.clip(np.log1p(flow) / math.log1p(np.percentile(flow, 99.7)), 0.0, 1.0)
    sed_n = np.clip(sediment / max(np.percentile(sediment, 99.5), 1e-6), 0.0, 1.0)
    return dict(height=current, step=current_step, flow=flow_n, sediment=sed_n, before=before_fine)
