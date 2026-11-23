#!/usr/bin/python3
"""pack_standards.py -- the eight knightly orders' standards, from the author's sheet to the game's atlas.

The sheet (docs/lore/prompts.md, prompt 10 and touch-up 11; made by the author with Google Gemini,
kept outside the repository in Art/Source/llm/lore/) shows eight cloth banners in two rows of four on
plain green. This finds each banner, trims the green, and packs them 4 x 2 into one 1024 x 512 PNG
in the lore's order (docs/lore/starforge-lore.md, section 7, which is MechParts.Orders' order):

    top row     Ashen Covenant, Iron Lantern, Ninth Vigil, Sable Oath
    bottom row  Broken Crown, Emberguard, Wardens of the Long Night, Brazen Host

Layout, which View/OrderStandards.cs mirrors (keep the two alike): a cell is 256 x 224; the banner is
squeezed into its middle 240 x 208, and the 8 px round it hold the banner's own edge, so a filtered or
mip-mapped sample at a panel's edge never takes the neighbouring order's colour. The bottom 64 rows are
plain white: the banner's own cloth, drawn with the same material, samples there and keeps its vertex
colours. OrderStandards.SourceAspect is the banner's real height over width (the "aspect" this prints).

    /usr/bin/python3 Tools/pack_standards.py                 # writes Assets/StarForge/Resources/MechStandards.png
    /usr/bin/python3 Tools/pack_standards.py --out DIR       # stages it in DIR instead
    /usr/bin/python3 Tools/pack_standards.py --sheet FILE    # another sheet, all eight from it (default: the
                                                             # corrected sheet, KEEP_FIRST's banners from the first)

Needs numpy and Pillow (/usr/bin/python3 has them; the Homebrew python3 does not).
"""
import argparse
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Art", "Source", "llm", "lore")
SHEETS = ["order_standards_v2.jpg", "order_standards.jpg"]
# Banners taken from the FIRST sheet although the corrected one exists: touch-up 11 mended the Emberguard's ember
# and the Broken Crown's field, and scattered stray crosses and a star over the Wardens' horizon (docs/lore/prompts.md).
KEEP_FIRST = {6}
DEST = os.path.join(ROOT, "Assets", "StarForge", "Resources", "MechStandards.png")
NAMES = ["Ashen Covenant", "Iron Lantern", "Ninth Vigil", "Sable Oath",
         "Broken Crown", "Emberguard", "Wardens of the Long Night", "Brazen Host"]
CELL_W, CELL_H, GUTTER = 256, 224, 8      # OrderStandards.CellW / CellH / Gutter
SHEET_W, SHEET_H = 1024, 512              # the rows under the two rows of cells stay white
INSET = 4          # px taken off each side of a found banner: the JPG's green fringe and any soft edge


def runs(on, least):
    """Stretches of True at least `least` long, as (start, end) pairs."""
    out, start = [], None
    for i, v in enumerate(list(on) + [False]):
        if v and start is None:
            start = i
        elif not v and start is not None:
            if i - start >= least:
                out.append((start, i))
            start = None
    return out


def banners(sheet):
    """The sheet's picture and its eight banners' boxes (x0, y0, x1, y1), row by row from the top left."""
    img = Image.open(sheet).convert("RGB")
    px = np.asarray(img).astype(np.int32)
    h, w = px.shape[:2]
    r, g, b = px[..., 0], px[..., 1], px[..., 2]
    green = (g > r + 30) & (g > b + 30)          # the background; no banner has a green field
    cloth = ~green
    rows = runs(cloth.mean(axis=1) > 0.25, h // 8)
    if len(rows) != 2:
        sys.exit("expected 2 rows of banners, found %d: %s" % (len(rows), rows))
    boxes = []
    for (y0, y1) in rows:
        cols = runs(cloth[y0:y1].mean(axis=0) > 0.5, w // 16)
        if len(cols) != 4:
            sys.exit("expected 4 banners in the row at y %d-%d, found %d: %s" % (y0, y1, len(cols), cols))
        for (x0, x1) in cols:
            ys = runs(cloth[y0:y1, x0:x1].mean(axis=1) > 0.5, h // 8)   # this banner's own top and bottom
            if len(ys) != 1:
                sys.exit("banner at x %d-%d: no single block of cloth" % (x0, x1))
            boxes.append((x0 + INSET, y0 + ys[0][0] + INSET, x1 - INSET, y0 + ys[0][1] - INSET))
    return img, boxes


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sheet")
    ap.add_argument("--out")
    a = ap.parse_args()
    sheet = a.sheet or next((os.path.join(SRC, n) for n in SHEETS if os.path.exists(os.path.join(SRC, n))), None)
    if not sheet or not os.path.exists(sheet):
        sys.exit("no sheet: expected one of %s in %s" % (", ".join(SHEETS), SRC))
    img, boxes = banners(sheet)
    w, h = img.size
    first = os.path.join(SRC, SHEETS[-1])
    kept = banners(first) if not a.sheet and sheet != first and KEEP_FIRST and os.path.exists(first) else None
    sources = [img] * len(boxes)
    if kept:
        for i in KEEP_FIRST:
            sources[i], boxes[i] = kept[0], kept[1][i]
    ws = [x1 - x0 for x0, _, x1, _ in boxes]
    hs = [y1 - y0 for _, y0, _, y1 in boxes]
    if max(ws) > min(ws) * 1.06 or max(hs) > min(hs) * 1.06:
        print("WARNING: the banners differ in size by more than 6%%: widths %s heights %s" % (ws, hs))
    atlas = Image.new("RGB", (SHEET_W, SHEET_H), (255, 255, 255))
    print("sheet %s, %d x %d" % (os.path.relpath(sheet, ROOT), w, h))
    for i, (x0, y0, x1, y1) in enumerate(boxes):
        cell = sources[i].crop((x0, y0, x1, y1))
        left = np.asarray(cell).astype(np.int32)
        fringe = ((left[..., 1] > left[..., 0] + 30) & (left[..., 1] > left[..., 2] + 30)).mean()
        mean = left.reshape(-1, 3).mean(axis=0)
        cx, cy = (i % 4) * CELL_W, (i // 4) * CELL_H
        atlas.paste(cell.resize((CELL_W, CELL_H), Image.LANCZOS), (cx, cy))   # under the gutter: its own edge
        atlas.paste(cell.resize((CELL_W - 2 * GUTTER, CELL_H - 2 * GUTTER), Image.LANCZOS), (cx + GUTTER, cy + GUTTER))
        print("  %d %-26s %4d x %4d at (%4d, %4d)  aspect %.3f  mean rgb %3d %3d %3d  green left %.2f%%%s"
              % (i, NAMES[i], x1 - x0, y1 - y0, x0, y0, (y1 - y0) / (x1 - x0), mean[0], mean[1], mean[2], fringe * 100,
                 "  (from the first sheet)" if sources[i] is not img else ""))
    aspect = float(np.mean(hs)) / float(np.mean(ws))
    dest = os.path.join(a.out, os.path.basename(DEST)) if a.out else DEST
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    atlas.save(dest, optimize=True)
    print("aspect (height / width) %.3f: View/OrderStandards.SourceAspect should say the same" % aspect)
    print("wrote %s, %d x %d, %d bytes" % (os.path.relpath(dest, ROOT), atlas.width, atlas.height, os.path.getsize(dest)))


if __name__ == "__main__":
    main()
