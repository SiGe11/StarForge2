#!/usr/bin/env python3
"""Make the smoke puff atlas StarForge/Smoke draws smoke, dust and mist with.

    python3 Tools/make_smoke_puffs.py

Four puffs in a 2x2 atlas (Assets/StarForge/Art/Textures/smoke_puffs.png, 1024
px). Each is a cauliflower cluster of billows: a large central ball, a ring of
medium ones and small ones round the edge, the front surface of the cluster
taken as a height field. From it:

  RG  the surface normal (x, y), so the shader can light each billow with the
      sun: lumps catch the light, the folds between them fall into shade;
  B   occlusion of the folds (how far the surface sits below its surroundings);
  A   coverage: thickest in the middle, feathering out into wisps at the edge and
      never reaching opaque -- a cloud is many faint puffs over one another.

The old smoke sheet was a photograph with no alpha and no lighting, so every
puff was the same flat grey sprite whatever the sun did. The first version of
this atlas went the other way: its coverage saturated in the core and its rim
ramp was narrow, so a puff drew as a solid dark shape with a crisp outline.
Needs only numpy.
"""
import math
import os
import struct
import zlib

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "StarForge", "Art", "Textures", "smoke_puffs.png")
CELL = 512


def blur(a, sigma):
    r = int(sigma * 3)
    x = np.arange(-r, r + 1)
    k = np.exp(-(x * x) / (2 * sigma * sigma))
    k /= k.sum()
    a = np.apply_along_axis(lambda m: np.convolve(m, k, mode="same"), 0, a)
    return np.apply_along_axis(lambda m: np.convolve(m, k, mode="same"), 1, a)


def value_noise(size, cells, rng):
    g = rng.random((cells + 1, cells + 1))
    t = np.linspace(0, cells, size, endpoint=False)
    i = t.astype(int)
    f = t - i
    f = f * f * (3 - 2 * f)
    a = g[i][:, i]
    b = g[i][:, i + 1]
    c = g[i + 1][:, i]
    d = g[i + 1][:, i + 1]
    fx, fy = f[None, :], f[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(size, rng, base=4, octaves=4):
    out = np.zeros((size, size))
    amp, norm = 0.5, 0.0
    for o in range(octaves):
        out += value_noise(size, base << o, rng) * amp
        norm += amp
        amp *= 0.5
    return out / norm


def warp(size, rng, amount, base=3, octaves=3):
    """A pair of low-frequency offsets to push sample positions around with.

    Value noise is built on an axis-aligned grid, and at these amplitudes its
    grid shows through as a faint star in the middle of the puff. Warping the
    sample positions before anything is drawn breaks that up, and at the same
    time it stops every billow being a clean ellipse."""
    return ((fbm(size, rng, base, octaves) - 0.5) * amount,
            (fbm(size, rng, base, octaves) - 0.5) * amount)


def puff(seed):
    rng = np.random.default_rng(seed)
    s = CELL
    yy, xx = np.mgrid[0:s, 0:s].astype(np.float64)
    c = s / 2.0

    # Warp the cell before a single billow is drawn. The first version of this
    # atlas put down perfect hemispheres, and you could count them: a puff read
    # as a pile of billiard balls, each with its own smooth shaded gradient.
    wx, wy = warp(s, rng, 64.0)
    sx, sy = xx + wx, yy + wy

    height = np.full((s, s), -1e9)

    def billow(bx, by, r):
        """One billow: an ellipse at its own angle, not a circle, raised as a
        dome over its footprint."""
        ang = rng.uniform(0, math.pi)
        ca, sa = math.cos(ang), math.sin(ang)
        u = (sx - bx) * ca + (sy - by) * sa
        v = -(sx - bx) * sa + (sy - by) * ca
        k = rng.uniform(0.62, 1.45)
        d2 = u * u + (v * v) / (k * k)
        inside = d2 < r * r
        return np.where(inside, np.sqrt(np.maximum(r * r - d2, 0)) * rng.uniform(0.8, 1.05)
                        + rng.uniform(-12, 22), -1e9)

    def tier(n, dmin, dmax, rmin, rmax, lift):
        nonlocal height
        for _ in range(n):
            a = rng.uniform(0, 2 * math.pi)
            d = rng.uniform(dmin, dmax)
            height = np.maximum(height, billow(c + math.cos(a) * d,
                                               c + math.sin(a) * d * 0.9 + lift,
                                               rng.uniform(rmin, rmax)))

    # Four tiers rather than three, more of each and each smaller than the last,
    # so no one billow carries a whole quarter of the outline and the rim is a
    # fine fray instead of a few lobes sticking out.
    tier(rng.integers(7, 10), 0, 50, 58, 86, 10)
    tier(rng.integers(14, 19), 58, 112, 32, 56, 12)
    tier(rng.integers(24, 32), 100, 150, 16, 34, 15)
    tier(rng.integers(30, 42), 132, 176, 7, 18, 17)

    covered = height > -1e8
    h = np.where(covered, height, 0.0)
    # Detail at two scales, warped as well, and strong enough against the billow
    # radii to actually break their domes up rather than dimple them.
    dx, dy = warp(s, rng, 26.0, base=6)
    coarse = fbm(s, rng, base=7, octaves=4)
    fine = fbm(s, rng, base=20, octaves=3)
    h = h + ((coarse - 0.5) * 54.0 + (fine - 0.5) * 22.0) * covered
    h = h + (dx + dy) * 0.25 * covered
    # Light blur only: the old 2.8 smoothed the detail back off again.
    h = blur(h, 1.6)

    # Normals come off a softer copy: the fine detail belongs in the silhouette,
    # where it frays the outline, not in per-pixel shading speckle.
    hn = blur(h, 3.0)
    gy, gx = np.gradient(hn)
    k = 1.0 / 5.5
    nx, ny = -gx * k, -gy * k
    nz = np.ones_like(nx)
    n = np.sqrt(nx * nx + ny * ny + nz * nz)
    nx, ny = nx / n, ny / n

    # Occlusion: how far a point sits below the blurred surface round it.
    ao = np.clip(1.0 + (hn - blur(hn, 16)) * 0.03, 0.3, 1.0)

    # Coverage. The old ramp was narrow and the core saturated, so a puff drew as
    # a solid shape with a crisp outline -- a dark lump of rock rather than smoke.
    # Now the silhouette is soft and eaten at two scales (big bites out of the rim,
    # a fine fray over the whole of it, so it stays ragged close up and far away),
    # the ramp is a smoothstep with no shoulder, and the core stops well short of
    # opaque: a cloud is built from many faint overlapping puffs, not from one
    # solid sprite.
    cov = blur(covered.astype(np.float64), 18.0)
    wisp = fbm(s, rng, base=5, octaves=5)
    fray = fbm(s, rng, base=18, octaves=3)
    thick = np.clip(h / 95.0, 0, 1)
    a = np.clip((cov - 0.30 - (wisp - 0.5) * 1.15 - (fray - 0.5) * 0.45) / 0.75, 0, 1)
    a = a * a * (3.0 - 2.0 * a)
    alpha = a * (0.22 + 0.78 * thick ** 0.75) * 0.80
    # Thin the outskirts whatever the billows did there, so a stray one out near
    # the rim reads as a wisp torn off the cloud rather than as a lump beside it.
    rad = np.sqrt((xx - c) ** 2 + ((yy - c) * 1.05) ** 2) / (s * 0.5)
    alpha *= np.clip(1.30 - rad * 1.20, 0, 1)
    # Nothing may reach the cell's border.
    edge = np.minimum(np.minimum(xx, s - 1 - xx), np.minimum(yy, s - 1 - yy))
    alpha *= np.clip(edge / 24.0, 0, 1)

    img = np.zeros((s, s, 4))
    img[..., 0] = nx * 0.5 + 0.5
    img[..., 1] = ny * 0.5 + 0.5       # rows here run with texture v (up)
    img[..., 2] = ao
    img[..., 3] = alpha
    return img


def write_png(path, rgba):
    h, w, _ = rgba.shape
    data = (np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8)
    raw = b"".join(b"\0" + data[h - 1 - y].tobytes() for y in range(h))   # flip: v up

    def chunk(tag, payload):
        return struct.pack(">I", len(payload)) + tag + payload + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    open(path, "wb").write(png)


def main():
    atlas = np.zeros((CELL * 2, CELL * 2, 4))
    for i in range(4):
        x, y = i % 2, i // 2
        atlas[y * CELL:(y + 1) * CELL, x * CELL:(x + 1) * CELL] = puff(1000 + i * 7)
        print("puff", i)
    # Rows are texture v (cell 0 bottom-left); write_png puts the top row first.
    write_png(OUT, atlas)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
