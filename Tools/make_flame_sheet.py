#!/usr/bin/env python3
"""Make the flame flipbook burning trees and bushes are drawn with.

    python3 Tools/make_flame_sheet.py

Writes Assets/StarForge/Art/Textures/flames.png: 16 frames (4x4, 256 px cells)
of a flame tongue, bright on black like the other additive sheets
(StarForge/Particle takes coverage from luminance). Each frame is a column of
fire -- wide and white-hot at the foot, licking up into orange tongues that
break into dark red wisps at the top -- whose turbulence scrolls upward and
loops seamlessly over the 16 frames, so a particle playing the sheet over its
life flickers the way a flame does rather than pulsing like a photographed
fireball. Needs only numpy.
"""
import math
import os
import struct
import zlib

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "StarForge", "Art", "Textures", "flames.png")
CELL = 256
FRAMES = 16


def write_png(path, rgb):
    h, w, _ = rgb.shape
    data = (np.clip(rgb, 0, 1) * 255 + 0.5).astype(np.uint8)
    raw = b"".join(b"\0" + data[y].tobytes() for y in range(h))

    def chunk(tag, payload):
        return struct.pack(">I", len(payload)) + tag + payload + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    open(path, "wb").write(png)


def periodic_noise(rng, shape, cells):
    """Smooth value noise, periodic in both axes (cells x cells lattice)."""
    g = rng.random((cells, cells))
    h, w = shape
    y = np.linspace(0, cells, h, endpoint=False)
    x = np.linspace(0, cells, w, endpoint=False)
    yi, xi = y.astype(int), x.astype(int)
    fy, fx = (y - yi)[:, None], (x - xi)[None, :]
    fy, fx = fy * fy * (3 - 2 * fy), fx * fx * (3 - 2 * fx)
    y0, y1 = yi % cells, (yi + 1) % cells
    x0, x1 = xi % cells, (xi + 1) % cells
    a, b = g[y0][:, x0], g[y0][:, x1]
    c, d = g[y1][:, x0], g[y1][:, x1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def main():
    rng = np.random.default_rng(77)
    # One tall periodic turbulence field; each frame reads a window of it
    # shifted up by 1/16 of its height, so frame 16 is frame 0 again.
    H = CELL * 2
    fields = [periodic_noise(rng, (H, CELL), c) for c in (4, 8, 16, 32)]
    turb = sum(f * a for f, a in zip(fields, (0.5, 0.28, 0.15, 0.07)))
    warpx = periodic_noise(rng, (H, CELL), 6) - 0.5
    sheet = np.zeros((CELL * 4, CELL * 4, 3))
    yy, xx = np.mgrid[0:CELL, 0:CELL].astype(np.float64)
    v = 1.0 - yy / CELL          # 0 at the foot, 1 at the top of the cell
    u = xx / CELL - 0.5
    for f in range(FRAMES):
        shift = int(round(f * H / FRAMES))
        rows = (np.arange(CELL) * H // CELL + shift) % H
        t = turb[rows]
        wx = warpx[rows]
        # The column sways, narrows toward the top and is torn sideways by the
        # turbulence there, which splits it into tongues.
        sway = wx * 0.3 * v + (t - 0.5) * 0.45 * v * v
        width = 0.26 * (1.0 - v) ** 0.55 + 0.02
        d = np.abs(u - sway) / width
        body = np.clip(1.0 - d, 0.0, 1.0) ** 0.8
        # Turbulence eats into it from the top down.
        heat = body * np.clip(1.35 - v * 1.05, 0, 1) - (t - 0.5) * 1.5 * v - v * 0.22
        heat = np.clip(heat * 1.5, 0.0, 1.0)
        # Soft foot, so the column rises out of the fuel rather than sitting on a line.
        heat *= np.clip(v * 6.0, 0.0, 1.0) ** 0.6
        # Colour by heat: a small white-yellow core, orange body, dark red wisps.
        r = np.clip(heat * 1.9, 0, 1)
        g = np.clip(heat * 1.35 - 0.42, 0, 1) ** 1.3
        b = np.clip(heat * 1.6 - 1.15, 0, 1) ** 1.6 * 0.7
        glow = np.stack([r, g * 0.95, b * 0.7], -1) * np.clip(heat * 1.4, 0, 1)[..., None]
        # Nothing may reach the cell border.
        edge = np.minimum(np.minimum(xx, CELL - 1 - xx), np.minimum(yy, CELL - 1 - yy))
        glow *= np.clip(edge / 10.0, 0, 1)[..., None]
        cx, cy = f % 4, f // 4
        sheet[cy * CELL:(cy + 1) * CELL, cx * CELL:(cx + 1) * CELL] = glow
    write_png(OUT, sheet)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
