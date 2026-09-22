#!/usr/bin/env python3
"""Make the hull-plating detail texture every unit and structure is surfaced with.

    python3 Tools/make_panel_texture.py [out_dir]

Writes armor.png (the detail albedo SF_Unit multiplies in, grey around its own
average) and armor_n.png (the plating's height; the importer turns it into a
normal map, treating brightness as height) into Assets/StarForge/Art/Textures/
unless another folder is given. 1024 px, tileable.

The plating is laid out the way hull panels are: the tile split recursively
into large rectangular plates, some of them split again, each plate a shade of
its own; recessed seams between plates, with a highlight on one lip; rivets in
rows along the seams; a few hatches and vents; and wear -- grime gathered in the
seams and faint streaks. The photograph it replaces came from the original and,
at the scale the units use it, read as brickwork.
"""
import os
import struct
import sys
import zlib

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
S = 1024


def write_png(path, grey):
    data = (np.clip(grey, 0, 1) * 255 + 0.5).astype(np.uint8)
    rgb = np.repeat(data[..., None], 3, axis=2)
    raw = b"".join(b"\0" + rgb[y].tobytes() for y in range(S))

    def chunk(tag, payload):
        return struct.pack(">I", len(payload)) + tag + payload + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", S, S, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    open(path, "wb").write(png)


def blur(a, r):
    k = np.ones(2 * r + 1) / (2 * r + 1)
    for axis in (0, 1):
        a = sum(np.roll(a, i, axis=axis) * k[i + r] for i in range(-r, r + 1))
    return a


def value_noise(rng, cells):
    g = rng.random((cells, cells))
    t = np.arange(S) / S * cells
    i = t.astype(int)
    f = t - i
    f = f * f * (3 - 2 * f)
    a, b = g[i % cells][:, i % cells], g[i % cells][:, (i + 1) % cells]
    c, d = g[(i + 1) % cells][:, i % cells], g[(i + 1) % cells][:, (i + 1) % cells]
    fx, fy = f[None, :], f[:, None]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Assets", "StarForge", "Art", "Textures")
    rng = np.random.default_rng(31337)
    plates = []

    def split(x0, y0, x1, y1, depth):
        w, h = x1 - x0, y1 - y0
        if depth >= 6 or (depth >= 3 and rng.random() < 0.3) or min(w, h) < 110:
            plates.append((x0, y0, x1, y1))
            return
        if w > h * 1.3 or (w >= h * 0.77 and rng.random() < 0.5):
            cut = int(x0 + w * rng.uniform(0.3, 0.7))
            split(x0, y0, cut, y1, depth + 1)
            split(cut, y0, x1, y1, depth + 1)
        else:
            cut = int(y0 + h * rng.uniform(0.3, 0.7))
            split(x0, y0, x1, cut, depth + 1)
            split(x0, cut, x1, y1, depth + 1)

    split(0, 0, S, S, 0)

    height = np.zeros((S, S))
    albedo = np.full((S, S), 0.55)
    seam = np.zeros((S, S))
    yy, xx = np.mgrid[0:S, 0:S]
    for (x0, y0, x1, y1) in plates:
        shade = rng.uniform(0.47, 0.63)
        albedo[y0:y1, x0:x1] = shade
        height[y0:y1, x0:x1] = rng.uniform(0.0, 0.08)     # plates stand a little proud of each other
        # Seam: a 5 px groove round the plate.
        g = 5
        seam[y0:y0 + g, x0:x1] = 1
        seam[y1 - g:y1, x0:x1] = 1
        seam[y0:y1, x0:x0 + g] = 1
        seam[y0:y1, x1 - g:x1] = 1
        # Rivets along the long edges.
        w, h = x1 - x0, y1 - y0
        step = 28
        for side in range(2):
            if w >= h:
                ys = y0 + 11 if side == 0 else y1 - 12
                for x in range(x0 + 18, x1 - 14, step):
                    height[ys - 2:ys + 3, x - 2:x + 3] += 0.35
            else:
                xs = x0 + 11 if side == 0 else x1 - 12
                for y in range(y0 + 18, y1 - 14, step):
                    height[y - 2:y + 3, xs - 2:xs + 3] += 0.35
        # Now and then a hatch (an inset rectangle) or a vent (a row of slots).
        r = rng.random()
        if r < 0.22 and w > 160 and h > 160:
            hx0, hy0 = x0 + int(w * 0.25), y0 + int(h * 0.25)
            hx1, hy1 = x1 - int(w * 0.25), y1 - int(h * 0.25)
            height[hy0:hy1, hx0:hx1] -= 0.12
            seam[hy0:hy0 + 3, hx0:hx1] = 0.7
            seam[hy1 - 3:hy1, hx0:hx1] = 0.7
            seam[hy0:hy1, hx0:hx0 + 3] = 0.7
            seam[hy0:hy1, hx1 - 3:hx1] = 0.7
        elif r < 0.36 and w > 140 and h > 90:
            vy = y0 + h // 2
            for k in range(6):
                vx = x0 + w // 2 - 60 + k * 22
                height[vy - 22:vy + 22, vx:vx + 9] -= 0.25
                seam[vy - 22:vy + 22, vx:vx + 9] = 0.6
    seam_soft = blur(seam, 2)
    height -= seam_soft * 0.6
    height = blur(height, 1)

    # Grime in the seams and round the rivets, faint streaks running down, and a
    # little large-scale mottling so no two plates look freshly painted.
    grime = np.clip(blur(seam, 7) * 1.4, 0, 1)
    streak = value_noise(rng, 64)[:, :1].repeat(S, axis=1).T      # vertical streaks (v is down the hull)
    streak = np.clip((value_noise(rng, 8) - 0.35) * 1.6, 0, 1) * (streak - 0.5) * 0.12
    mottle = (value_noise(rng, 6) - 0.5) * 0.08 + (value_noise(rng, 24) - 0.5) * 0.05
    a = albedo + mottle + streak
    a = a * (1 - grime * 0.35) - seam_soft * 0.22
    # A lit lip on the upper edge of every seam, as rolled plate edges catch the light.
    lip = np.clip(np.roll(seam_soft, -3, axis=0) - seam_soft, 0, 1)
    a += lip * 0.12
    a = np.clip(a, 0.05, 0.95)

    # The normal map is made from brightness on import, so the height goes into
    # its own image (the albedo would bring the paint shading into the normals).
    h = (height - height.min()) / max(1e-6, height.max() - height.min())
    write_png(os.path.join(out, "armor.png"), a)
    write_png(os.path.join(out, "armor_n.png"), 0.2 + 0.6 * h)
    print(f"{len(plates)} plates -> {out}")


if __name__ == "__main__":
    main()
