#!/usr/bin/env python3
"""Make the macOS app icon from the square artwork in Assets/icon.png.

    python3 Tools/make_app_icon.py

Assets/icon.png is a painting with its own rounded corners drawn on a white
background, so macOS showed it as a white-cornered square inside a plate.
This crops inside those corners, scales the picture into Apple's icon grid
(an 824 px square with 185.4 px corners, centred on a 1024 px canvas) and
makes everything outside that shape transparent. The shape matters on macOS
26: an icon that does not match the template closely (a superellipse did not)
is shrunk onto a grey plate. Writes
Assets/StarForge/Art/AppIcon.png, which BuildMac sets as the player icon.
Needs only numpy and macOS's sips (to read the PNG).
"""
import os
import struct
import subprocess
import tempfile
import zlib

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Assets", "icon.png")
OUT = os.path.join(ROOT, "Assets", "StarForge", "Art", "AppIcon.png")

CANVAS, CONTENT = 1024, 824
CROP = 84          # inside the artwork's own rounded corners (content starts ~18 px in)
RADIUS = 185.4     # corner radius of Apple's macOS icon template at 824 px


def read_rgb(path):
    with tempfile.TemporaryDirectory() as tmp:
        bmp = os.path.join(tmp, "src.bmp")
        subprocess.run(["sips", "-s", "format", "bmp", path, "--out", bmp], check=True, capture_output=True)
        b = open(bmp, "rb").read()
    off = struct.unpack_from("<I", b, 10)[0]
    w, h = struct.unpack_from("<ii", b, 18)
    bpp = struct.unpack_from("<H", b, 28)[0] // 8
    row = (w * bpp + 3) // 4 * 4
    a = np.frombuffer(b, np.uint8, offset=off)[: row * abs(h)].reshape(abs(h), row)[:, : w * bpp]
    a = a.reshape(abs(h), w, bpp)[:, :, 2::-1]          # BGR(A) -> RGB
    return a[::-1] if h > 0 else a


def resample(img, size):
    """Area-weighted downscale: 3x3 bilinear taps per output pixel."""
    src = img.astype(np.float32)
    h, w, _ = src.shape
    out = np.zeros((size, size, 3), np.float32)
    for oy in (-1 / 3, 0, 1 / 3):
        for ox in (-1 / 3, 0, 1 / 3):
            ys = ((np.arange(size) + 0.5 + oy) * h / size - 0.5).clip(0, h - 1.001)
            xs = ((np.arange(size) + 0.5 + ox) * w / size - 0.5).clip(0, w - 1.001)
            y0, x0 = ys.astype(int), xs.astype(int)
            fy, fx = (ys - y0)[:, None, None], (xs - x0)[None, :, None]
            a = src[y0][:, x0]
            b = src[y0][:, x0 + 1]
            c = src[y0 + 1][:, x0]
            d = src[y0 + 1][:, x0 + 1]
            out += (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy
    return out / 9.0


def write_png(path, rgba):
    h, w, _ = rgba.shape
    raw = b"".join(b"\0" + rgba[y].tobytes() for y in range(h))

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    open(path, "wb").write(png)


def main():
    art = read_rgb(SRC)
    art = art[CROP: art.shape[0] - CROP, CROP: art.shape[1] - CROP]
    rgb = resample(art, CONTENT)

    # Rounded-square mask from a signed distance, antialiased over one pixel.
    c = np.abs(np.arange(CONTENT) + 0.5 - CONTENT / 2) - (CONTENT / 2 - RADIUS)
    dx, dy = np.maximum(c, 0)[None, :], np.maximum(c, 0)[:, None]
    alpha = np.clip(0.5 - (np.sqrt(dx * dx + dy * dy) - RADIUS), 0, 1)

    icon = np.zeros((CANVAS, CANVAS, 4), np.uint8)
    o = (CANVAS - CONTENT) // 2
    icon[o:o + CONTENT, o:o + CONTENT, :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    icon[o:o + CONTENT, o:o + CONTENT, 3] = (alpha * 255 + 0.5).astype(np.uint8)
    write_png(OUT, icon)
    print("wrote", OUT)


if __name__ == "__main__":
    main()
