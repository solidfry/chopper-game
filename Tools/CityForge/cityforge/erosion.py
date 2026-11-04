"""
Terrain erosion on the height field (numpy, no Blender): water droplets that carry sediment down the slopes (hydraulic
erosion after Hans Theobald Beyer 2015, "Implementation of a method for hydraulic erosion"), run as batches of
droplets at once. Cuts gullies into slopes and cliffs and leaves fans of sediment at their feet.

A weight mask (0..1) keeps designed places as they are (city floor, mesa tops); terrain.py carves the city, canyon,
lava, roads and channel after the erosion, so those stay exact.
"""
import numpy as np

DEFAULTS = dict(droplets=1.0, steps=48, inertia=0.05, capacity=4.0, min_slope=0.01, erode=0.3, deposit=0.3,
                evaporate=0.02, gravity=4.0, batch=16384, strength=1.0, max_speed=8.0, max_take=0.3)
# max_speed (cells per step) and max_take (grid units of height per step) keep droplets on cliffs stable: a 100 m cliff is
# 16 grid units in one cell, and taking all of it at once digs pits that blow up.


def _bilinear(h, ix, iz, fx, fz):
    return (h[iz, ix] * (1 - fx) * (1 - fz) + h[iz, ix + 1] * fx * (1 - fz)
            + h[iz + 1, ix] * (1 - fx) * fz + h[iz + 1, ix + 1] * fx * fz)


def _scatter(h, ix, iz, fx, fz, amount):
    np.add.at(h, (iz, ix), amount * (1 - fx) * (1 - fz))
    np.add.at(h, (iz, ix + 1), amount * fx * (1 - fz))
    np.add.at(h, (iz + 1, ix), amount * (1 - fx) * fz)
    np.add.at(h, (iz + 1, ix + 1), amount * fx * fz)


def hydraulic(height, step, weight=None, seed=0, **settings):
    """Eroded copy of `height` (metres, grid step `step` m). droplets: droplets per grid cell. weight: 0 = no change
    there (erosion and deposits both scaled by it)."""
    v = dict(DEFAULTS, **settings)
    n = height.shape[0]
    h = height / step                      # work in grid units: slopes are rise per cell
    w = np.ones_like(h) if weight is None else np.clip(weight, 0.0, 1.0)
    rng = np.random.default_rng(seed)
    total = int(v["droplets"] * (n - 1) * (n - 1))
    for start in range(0, total, v["batch"]):
        count = min(v["batch"], total - start)
        px = rng.uniform(0, n - 1.001, count)
        pz = rng.uniform(0, n - 1.001, count)
        dx = np.zeros(count)
        dz = np.zeros(count)
        speed = np.ones(count)
        water = np.ones(count)
        sediment = np.zeros(count)
        alive = np.ones(count, bool)
        for _ in range(v["steps"]):
            idx = np.nonzero(alive)[0]
            if idx.size == 0:
                break
            x, z = px[idx], pz[idx]
            ix, iz = x.astype(np.int64), z.astype(np.int64)
            fx, fz = x - ix, z - iz
            h00, h10, h01, h11 = h[iz, ix], h[iz, ix + 1], h[iz + 1, ix], h[iz + 1, ix + 1]
            gx = (h10 - h00) * (1 - fz) + (h11 - h01) * fz
            gz = (h01 - h00) * (1 - fx) + (h11 - h10) * fx
            old = h00 * (1 - fx) * (1 - fz) + h10 * fx * (1 - fz) + h01 * (1 - fx) * fz + h11 * fx * fz
            ndx = dx[idx] * v["inertia"] - gx * (1 - v["inertia"])
            ndz = dz[idx] * v["inertia"] - gz * (1 - v["inertia"])
            length = np.hypot(ndx, ndz)
            flat = length < 1e-9
            ndx = np.where(flat, rng.uniform(-1, 1, idx.size), ndx)
            ndz = np.where(flat, rng.uniform(-1, 1, idx.size), ndz)
            length = np.maximum(np.hypot(ndx, ndz), 1e-9)
            ndx, ndz = ndx / length, ndz / length
            nx, nz = x + ndx, z + ndz
            inside = (nx >= 0) & (nx < n - 1.001) & (nz >= 0) & (nz < n - 1.001)
            nxc, nzc = np.clip(nx, 0, n - 1.001), np.clip(nz, 0, n - 1.001)
            jx, jz = nxc.astype(np.int64), nzc.astype(np.int64)
            new = _bilinear(h, jx, jz, nxc - jx, nzc - jz)
            dh = np.where(inside, new - old, 0.0)
            sed = sediment[idx]
            cap = np.maximum(-dh, v["min_slope"]) * speed[idx] * water[idx] * v["capacity"]
            keep = w[iz, ix] * (1 - fx) * (1 - fz) + w[iz, ix + 1] * fx * (1 - fz) \
                + w[iz + 1, ix] * (1 - fx) * fz + w[iz + 1, ix + 1] * fx * fz
            drop = (sed > cap) | (dh > 0)
            put = np.where(dh > 0, np.minimum(dh, sed), (sed - cap) * v["deposit"])
            put = np.where(drop, put, 0.0)
            take = np.where(drop, 0.0, np.minimum(np.minimum((cap - sed) * v["erode"], -dh), v["max_take"]))
            take = take * v["strength"]
            _scatter(h, ix, iz, fx, fz, (put - take) * keep)
            sediment[idx] = sed - put + take
            speed[idx] = np.minimum(np.sqrt(np.maximum(0.0, speed[idx] ** 2 - dh * v["gravity"])), v["max_speed"])
            water[idx] *= 1 - v["evaporate"]
            dx[idx], dz[idx] = ndx, ndz
            px[idx], pz[idx] = nxc, nzc
            alive[idx] = inside
    return h * step
