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
  A   coverage: solid in the middle, broken up into wisps at the edge.

The old smoke sheet was a photograph with no alpha and no lighting, so every
puff was the same flat grey sprite whatever the sun did. Needs only numpy.
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


def puff(seed):
    rng = np.random.default_rng(seed)
    s = CELL
    yy, xx = np.mgrid[0:s, 0:s].astype(np.float64)
    c = s / 2.0
    height = np.full((s, s), -1e9)
    balls = []
    # A core of several large billows (one huge ball reads as a dome) ...
    for _ in range(rng.integers(5, 8)):
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(0, 60)
        balls.append((c + math.cos(a) * d, c + math.sin(a) * d + 10, rng.uniform(70, 100)))
    # ... a ring of medium ones round it ...
    for _ in range(rng.integers(9, 13)):
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(80, 135)
        balls.append((c + math.cos(a) * d, c + math.sin(a) * d * 0.9 + 12, rng.uniform(42, 70)))
    # ... and small ones breaking up the outline.
    for _ in range(rng.integers(18, 26)):
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(130, 190)
        balls.append((c + math.cos(a) * d, c + math.sin(a) * d * 0.85 + 16, rng.uniform(16, 38)))
    for (bx, by, r) in balls:
        d2 = (xx - bx) ** 2 + (yy - by) ** 2
        inside = d2 < r * r
        z = np.where(inside, np.sqrt(np.maximum(r * r - d2, 0)) + rng.uniform(-15, 30), -1e9)
        height = np.maximum(height, z)
    covered = height > -1e8
    h = np.where(covered, height, 0.0)
    # Small billows on every ball, then soften the creases between balls a little.
    detail = fbm(s, rng, base=14, octaves=3)
    h = h + (detail - 0.5) * 26.0 * covered
    h = blur(h, 2.8)

    gy, gx = np.gradient(h)
    k = 1.0 / 4.5
    nx, ny = -gx * k, -gy * k
    nz = np.ones_like(nx)
    n = np.sqrt(nx * nx + ny * ny + nz * nz)
    nx, ny = nx / n, ny / n

    # Occlusion: how far a point sits below the blurred surface round it.
    ao = np.clip(1.0 + (h - blur(h, 16)) * 0.03, 0.3, 1.0)

    # Coverage: dense in the core, thinning toward the rim, whose outline is eaten
    # into wisps by noise.
    cov = blur(covered.astype(np.float64), 10.0)
    wisp = fbm(s, rng, base=6, octaves=4)
    thick = np.clip(h / 90.0, 0, 1)
    alpha = np.clip((cov - 0.35 - (wisp - 0.5) * 1.4) / 0.55, 0, 1) * (0.3 + 0.7 * thick ** 0.6)
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
