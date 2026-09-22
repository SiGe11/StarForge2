#!/usr/bin/env python3
"""Make the foliage surface textures StarForge/Tree maps onto every crown.

    python3 Tools/make_leaf_textures.py

Writes two tileable 512 px data textures into Assets/StarForge/Art/Textures/:

  foliage_broad.png   overlapping broad leaves (oak, birch, poplar, bushes, ferns)
  foliage_needle.png  tufts of needles (spruce), and reed blades

Each is a pile of leaves painted back to front with a depth buffer, and stores:

  RG  the surface normal (x, y) of the leaf on top: every leaf tilted its own
      way and cupped, so a crown catches the sun leaf by leaf instead of as a
      smooth ball;
  B   occlusion: the gaps between leaves and the leaves lying deep in the pile
      are dark, the top layer bright;
  A   a brightness per leaf, so neighbouring leaves differ in shade.

The crowns are opaque meshes (no alpha test, see CLAUDE.md), so this texture is
what makes them read as leaves at all. Leaves are sized so they stay a few
pixels across at RTS range: smaller would shimmer. Needs only numpy.
"""
import math
import os
import struct
import zlib

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "StarForge", "Art", "Textures")
S = 512


def write_png(path, rgba):
    h, w, _ = rgba.shape
    data = (np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8)
    raw = b"".join(b"\0" + data[h - 1 - y].tobytes() for y in range(h))   # rows are v, bottom first

    def chunk(tag, payload):
        return struct.pack(">I", len(payload)) + tag + payload + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    open(path, "wb").write(png)


def paint(leaves, rng):
    """leaves: list of (cx, cy, angle, length, width, tilt_x, tilt_y, shade, shape).
    Painted in list order; later leaves lie on top."""
    depth = np.full((S, S), -1.0)
    nx = np.zeros((S, S))
    ny = np.zeros((S, S))
    shade = np.full((S, S), 0.5)
    count = len(leaves)
    for i, (cx, cy, ang, ln, wd, tx, ty, sh, shape) in enumerate(leaves):
        z = i / count
        r = int(ln * 0.6) + 2
        xs = np.arange(int(cx) - r, int(cx) + r + 1)
        ys = np.arange(int(cy) - r, int(cy) + r + 1)
        gx, gy = np.meshgrid(xs, ys)
        ca, sa = math.cos(ang), math.sin(ang)
        # Leaf frame: u along the midrib (0 at the stalk, 1 at the tip), v across.
        u = ((gx - cx) * ca + (gy - cy) * sa) / ln + 0.5
        v = (-(gx - cx) * sa + (gy - cy) * ca) / (wd * 0.5)
        if shape == "needle":
            half = np.clip(1.0 - np.abs(2 * u - 1) ** 4, 0, 1)
        else:
            # A pointed oval, widest a little below the middle.
            half = np.clip(np.sin(np.clip(u, 0, 1) * math.pi) ** 0.8 * (1.15 - 0.3 * u), 0, 1)
        inside = (u > 0) & (u < 1) & (np.abs(v) < half)
        wx, wy = gx % S, gy % S
        cur = depth[wy, wx]
        top = inside & (z > cur)
        if not top.any():
            continue
        # Cupped across the midrib and tilted as a whole; a crease along the midrib.
        across = np.where(half > 1e-3, v / np.maximum(half, 1e-3), 0.0)
        cup = 0.45 if shape != "needle" else 0.25
        lx = tx + (-sa) * across * cup
        ly = ty + ca * across * cup
        sel = (wy[top], wx[top])
        depth[sel] = z
        nx[sel] = lx[top]
        ny[sel] = ly[top]
        vein = 1.0 - 0.18 * np.exp(-(v[top] ** 2) * 60.0) if shape != "needle" else 1.0
        shade[sel] = np.clip(sh * vein * (0.85 + 0.15 * (1 - np.abs(across[top]))), 0, 1)
    covered = depth >= 0
    # Occlusion: deep leaves and the gaps are in shade; soften it a little.
    occ = np.where(covered, 0.45 + 0.55 * np.clip(depth, 0, 1) ** 0.7, 0.18)
    occ = blur(occ, 1.2)
    length = np.sqrt(nx * nx + ny * ny)
    k = np.where(length > 0.85, 0.85 / np.maximum(length, 1e-6), 1.0)
    nx, ny = nx * k, ny * k
    img = np.zeros((S, S, 4))
    img[..., 0] = nx * 0.5 + 0.5
    img[..., 1] = ny * 0.5 + 0.5
    img[..., 2] = occ
    img[..., 3] = np.where(covered, shade, 0.3)
    return img


def blur(a, sigma):
    r = int(sigma * 3) + 1
    x = np.arange(-r, r + 1)
    k = np.exp(-(x * x) / (2 * sigma * sigma))
    k /= k.sum()
    out = np.zeros_like(a)
    for i, w in zip(x, k):
        out += np.roll(a, i, axis=0) * w
    a = out
    out = np.zeros_like(a)
    for i, w in zip(x, k):
        out += np.roll(a, i, axis=1) * w
    return out


def broad(rng):
    leaves = []
    for _ in range(1500):
        ln = rng.uniform(26, 40)
        tilt = rng.uniform(0.15, 0.7)
        ta = rng.uniform(0, 2 * math.pi)
        leaves.append((rng.uniform(0, S), rng.uniform(0, S), rng.uniform(0, 2 * math.pi), ln, ln * rng.uniform(0.42, 0.55),
                       math.cos(ta) * tilt, math.sin(ta) * tilt, rng.uniform(0.6, 1.0), "leaf"))
    return paint(leaves, rng)


def needle(rng):
    leaves = []
    # Tufts: needles fanning out from a twig, the twigs themselves at random.
    for _ in range(620):
        cx, cy = rng.uniform(0, S), rng.uniform(0, S)
        base = rng.uniform(0, 2 * math.pi)
        sh = rng.uniform(0.6, 1.0)
        for j in range(12):
            # Needles leave the twig all along its length, to both sides.
            along = rng.uniform(-18, 18)
            a = base + rng.choice((-1, 1)) * rng.uniform(0.35, 1.1)
            ln = rng.uniform(22, 34)
            px = cx + math.cos(base) * along + math.cos(a) * ln * 0.45
            py = cy + math.sin(base) * along + math.sin(a) * ln * 0.45
            tilt = rng.uniform(0.2, 0.6)
            ta = a + math.pi / 2 * rng.choice((-1, 1))
            leaves.append((px, py, a, ln, rng.uniform(4.5, 6.0), math.cos(ta) * tilt, math.sin(ta) * tilt,
                           sh * rng.uniform(0.85, 1.05), "needle"))
    return paint(leaves, rng)


def main():
    rng = np.random.default_rng(4242)
    write_png(os.path.join(OUT, "foliage_broad.png"), broad(rng))
    print("foliage_broad.png")
    write_png(os.path.join(OUT, "foliage_needle.png"), needle(rng))
    print("foliage_needle.png")


if __name__ == "__main__":
    main()
