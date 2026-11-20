#!/usr/bin/python3
"""Turn six pictures made for this project with an image model into three game textures:
the marks explosions leave on the ground, the damage a struck hull shows, and the
pattern drawn over ground the player has never seen.

    /usr/bin/python3 Tools/pack_marks.py [--out DIR]

(/usr/bin/python3: it has numpy and Pillow.) Sources, 2048 px, in Art/Source/llm/ (not in
the repository and not fetchable again, so keep them), made by the author with Google
Gemini on 2026-11-20: scorch_1.jpg .. scorch_4.jpg (soot on white), damage_mask.jpg
(white where a steel plate is struck, on black), fog_unknown.jpg (a faint grid and
contour lines on near-black). Written to Assets/StarForge/Art/Textures/ (or --out, to
stage them while a peer session holds the Editor):

  scorch.jpg       1024 px, 2 x 2 cells: one blast mark each, bright where the soot
                   lies (SF_GroundDecal kind 1 reads red as coverage). Each picture is
                   cut to its mark's own square, so the mark fills its cell, and stored
                   sRGB-encoded so that the value the shader reads is the soot itself.
  armor.png        the hull plating's colour as it was (Tools/make_panel_texture.py),
                   with the damage mask in alpha: SF_Unit already reads this texture
                   three times a pixel for the panels, so the damage costs no read of
                   its own. Re-run this after make_panel_texture.py, which writes no alpha.
  fog_unknown.png  2048 px, one channel: the lines alone, 0 where the picture was its
                   dark ground. Its grid is exactly 50 squares across, so it tiles on
                   its own; the contour lines do not quite, and their break lies under
                   a grid line. SF_FogOfWar lays it over unexplored ground.
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Art", "Source", "llm")
TEX = os.path.join(ROOT, "Assets", "StarForge", "Art", "Textures")
LUMA = np.array([0.299, 0.587, 0.114], np.float32)


def luma(name):
    path = os.path.join(SRC, name)
    if not os.path.exists(path):
        sys.exit("missing source " + path)
    rgb = np.asarray(Image.open(path).convert("RGB"), np.float32) / 255.0
    return (rgb * LUMA).sum(axis=2)


def to_srgb(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1.0 / 2.4) - 0.055)


def resize(a, size):
    im = Image.fromarray(np.clip(a * 255.0 + 0.5, 0, 255).astype(np.uint8))
    return np.asarray(im.resize((size, size), Image.LANCZOS), np.float32) / 255.0


def scorch_sheet(out):
    cell = 512
    sheet = np.zeros((cell * 2, cell * 2), np.float32)
    for i in range(4):
        soot = 1.0 - luma("scorch_%d.jpg" % (i + 1))
        ys, xs = np.where(soot > 0.06)
        cx, cy = (xs.min() + xs.max()) * 0.5, (ys.min() + ys.max()) * 0.5
        half = max(xs.max() - xs.min(), ys.max() - ys.min()) * 0.5 * 1.03
        n = soot.shape[0]
        # The mark's own square, white (no soot) wherever it runs past the picture.
        x0, y0, side = int(round(cx - half)), int(round(cy - half)), int(round(half * 2))
        sq = np.zeros((side, side), np.float32)
        sx0, sy0 = max(0, x0), max(0, y0)
        sx1, sy1 = min(n, x0 + side), min(n, y0 + side)
        sq[sy0 - y0:sy1 - y0, sx0 - x0:sx1 - x0] = soot[sy0:sy1, sx0:sx1]
        m = resize(sq, cell)
        # Paper white is not quite 1 after JPEG: take the floor off, keep the blacks.
        m = np.clip((m - 0.02) / 0.96, 0.0, 1.0)
        # Nothing may reach a cell's edge: the neighbour's mark would bleed in under
        # bilinear filtering and the mips.
        yy, xx = np.mgrid[0:cell, 0:cell].astype(np.float32)
        r = np.hypot((xx + 0.5) / cell * 2 - 1, (yy + 0.5) / cell * 2 - 1)
        m *= 1.0 - np.clip((r - 0.9) / 0.1, 0.0, 1.0)
        col, row = i % 2, i // 2
        sheet[row * cell:(row + 1) * cell, col * cell:(col + 1) * cell] = m
        print("scorch_%d: mark %d px across, cell coverage %.3f (mean), %.0f%% over 0.5"
              % (i + 1, side, m.mean(), 100.0 * (m > 0.5).mean()))
    px = np.clip(to_srgb(sheet) * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.merge("RGB", [Image.fromarray(px)] * 3).save(os.path.join(out, "scorch.jpg"), quality=93)


def armour_damage(out):
    plate = Image.open(os.path.join(TEX, "armor.png")).convert("RGB")
    size = plate.size[0]
    dmg = resize(luma("damage_mask.jpg"), size)
    dmg = np.clip((dmg - 0.03) / 0.94, 0.0, 1.0)
    alpha = Image.fromarray(np.clip(dmg * 255.0 + 0.5, 0, 255).astype(np.uint8))
    r, g, b = plate.split()
    Image.merge("RGBA", [r, g, b, alpha]).save(os.path.join(out, "armor.png"))
    print("armor.png: %d px, damage in alpha: mean %.3f, struck (over 0.5) %.1f%% of the plate"
          % (size, dmg.mean(), 100.0 * (dmg > 0.5).mean()))


def fog_pattern(out):
    l = luma("fog_unknown.jpg")
    ground = float(np.percentile(l, 50))
    grid = float(np.percentile(l, 99.5))
    lines = np.clip((l - ground * 1.15) / (grid - ground * 1.15), 0.0, 1.0)
    # The contour lines are a tenth of the grid's brightness: lifted, so they survive
    # the mips and the dark the shader draws them at.
    lines = np.power(lines, 0.7)
    px = np.clip(lines * 255.0 + 0.5, 0, 255).astype(np.uint8)
    Image.fromarray(px).save(os.path.join(out, "fog_unknown.png"), optimize=True)
    # The grid must close on itself: its lines are the columns far brighter than the rest.
    cols = lines.mean(axis=0)
    on = cols > (cols.mean() + cols.max()) * 0.5
    squares = int((on[1:] & ~on[:-1]).sum()) + int(on[0] and not on[-1])
    print("fog_unknown.png: %d px, ground %.3f, grid %.3f, lines mean %.3f, %d grid lines across"
          % (l.shape[0], ground, grid, lines.mean(), squares))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=TEX, help="where to write (default: the game's texture folder)")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    scorch_sheet(a.out)
    armour_damage(a.out)
    fog_pattern(a.out)


if __name__ == "__main__":
    main()
