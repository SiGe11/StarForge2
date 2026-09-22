"""Compose the leaf-card textures the trees and bushes are clothed in.

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
        --python Tools/blender/make_leaf_cards.py

Blender is only the image library here. Reads ambientCG's CC0 leaf atlases
(photographed single leaves with opacity and normal maps, fetched into
Art/Source/ambientcg by Tools/fetch_assets.py), cuts every leaf out, and lays
them along twigs into sprays: four different sprays per texture (a 2x2 atlas,
512 px a cell), each a curving twig with leaves on alternate sides, larger near
the base, the last one continuing the twig. The conifer spray is a fan of
needle sprigs. Writes into Assets/StarForge/Art/Textures/Leaves/:

  <kind>_col.png  colour (sRGB) and coverage (alpha); the colour of the gaps is
                  bled out from the leaves, so mipmaps do not darken the edges
  <kind>_nrm.png  the leaves' own normal maps (OpenGL), turned with each leaf

Kinds: oak (LeafSet016), birch (LeafSet014), beech (LeafSet024, for bushes and
poplars) and conifer (LeafSet019).
"""
import math
import os
import random
from collections import deque

import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "Art", "Source", "ambientcg")
OUT = os.path.join(ROOT, "Assets", "StarForge", "Art", "Textures", "Leaves")
CELL = 512

KINDS = {
    "oak": ("LeafSet016", "leaf"),
    "birch": ("LeafSet014", "leaf"),
    "beech": ("LeafSet024", "leaf"),
    "conifer": ("LeafSet019", "sprig"),
}


def load(path, data=False):
    img = bpy.data.images.load(path)
    if data:
        img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    bpy.data.images.remove(img)
    return px   # rows bottom-up (Blender), so row 0 is the image's bottom


def save(path, rgba, data=False):
    h, w, _ = rgba.shape
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    if data:
        img.colorspace_settings.name = "Non-Color"
    img.alpha_mode = "STRAIGHT"
    img.pixels[:] = np.clip(rgba, 0, 1).astype(np.float32).ravel()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def pieces(alpha, min_area=400):
    """Connected blobs of the opacity map (on a quarter-size grid), as full-size
    bounding boxes (x0, y0, x1, y1)."""
    h, w = alpha.shape
    s = 4
    small = alpha[::s, ::s] > 0.5
    sh, sw = small.shape
    seen = np.zeros_like(small, dtype=bool)
    boxes = []
    for y in range(sh):
        for x in range(sw):
            if not small[y, x] or seen[y, x]:
                continue
            q = deque([(y, x)])
            seen[y, x] = True
            ys, xs = [], []
            while q:
                cy, cx = q.popleft()
                ys.append(cy)
                xs.append(cx)
                for ny, nx in ((cy + 1, cx), (cy - 1, cx), (cy, cx + 1), (cy, cx - 1)):
                    if 0 <= ny < sh and 0 <= nx < sw and small[ny, nx] and not seen[ny, nx]:
                        seen[ny, nx] = True
                        q.append((ny, nx))
            if len(ys) * s * s < min_area:
                continue
            boxes.append((max(0, min(xs) * s - 4), max(0, min(ys) * s - 4), min(w, (max(xs) + 1) * s + 4), min(h, (max(ys) + 1) * s + 4)))
    return boxes


def paste(dst_col, dst_nrm, leaf_col, leaf_nrm, leaf_a, cx, cy, angle, length, shade):
    """Draw a leaf whose stem end is at (cx, cy), pointing along `angle` (radians,
    0 = up the image), `length` px long, into the cell buffers (alpha over)."""
    lh, lw = leaf_a.shape
    scale = length / lh
    ca, sa = math.cos(angle), math.sin(angle)
    # Leaf frame: u across, v along from the stem (row 0 = stem end).
    half = max(lw, lh) * scale
    x0, x1 = int(cx - half) - 1, int(cx + half) + 1
    y0, y1 = int(cy - half) - 1, int(cy + half) + 1
    H, W = dst_col.shape[:2]
    x0, y0, x1, y1 = max(0, x0), max(0, y0), min(W, x1), min(H, y1)
    if x1 <= x0 or y1 <= y0:
        return
    gy, gx = np.mgrid[y0:y1, x0:x1].astype(np.float32)
    dx, dy = gx - cx, gy - cy
    # Rotate into the leaf's frame: 'along' is the leaf's up direction.
    along = (dx * -sa + dy * ca) / scale
    across = (dx * ca + dy * sa) / scale
    sx = across + lw * 0.5
    sy = along
    inside = (sx >= 0) & (sx < lw - 1) & (sy >= 0) & (sy < lh - 1)
    if not inside.any():
        return
    ix = np.clip(sx.astype(int), 0, lw - 1)
    iy = np.clip(sy.astype(int), 0, lh - 1)
    a = np.where(inside, leaf_a[iy, ix], 0.0)
    col = leaf_col[iy, ix, :3] * shade
    n = leaf_nrm[iy, ix, :3] * 2.0 - 1.0
    # The normal map's x/y turn with the leaf.
    nx = n[..., 0] * ca - n[..., 1] * sa
    ny = n[..., 0] * sa + n[..., 1] * ca
    n2 = np.stack([nx, ny, n[..., 2]], -1) * 0.5 + 0.5
    region_c = dst_col[y0:y1, x0:x1]
    region_n = dst_nrm[y0:y1, x0:x1]
    a3 = a[..., None]
    region_c[..., :3] = col * a3 + region_c[..., :3] * (1 - a3)
    region_c[..., 3] = a + region_c[..., 3] * (1 - a)
    region_n[..., :3] = n2 * a3 + region_n[..., :3] * (1 - a3)


def twig(dst_col, cx0, cy0, cx1, cy1, bend, width, rng):
    """A thin brown twig from (cx0, cy0) to (cx1, cy1), bowed sideways."""
    pts = []
    for k in range(41):
        t = k / 40
        x = cx0 + (cx1 - cx0) * t + bend * math.sin(t * math.pi)
        y = cy0 + (cy1 - cy0) * t
        pts.append((x, y, width * (1 - 0.7 * t)))
    H, W = dst_col.shape[:2]
    # Drawn densely (the 41 points above are where leaves attach).
    dense = []
    for k in range(400):
        t = k / 399
        dense.append((cx0 + (cx1 - cx0) * t + bend * math.sin(t * math.pi), cy0 + (cy1 - cy0) * t, width * (1 - 0.7 * t)))
    for x, y, r in dense:
        xa, xb = int(x - r - 1), int(x + r + 2)
        ya, yb = int(y - r - 1), int(y + r + 2)
        for yy in range(max(0, ya), min(H, yb)):
            for xx in range(max(0, xa), min(W, xb)):
                if (xx - x) ** 2 + (yy - y) ** 2 <= r * r:
                    dst_col[yy, xx] = (0.20, 0.14, 0.09, 1.0)
    return pts


def spray_leaf(cells, leaves, rng, oy, ox):
    col = np.zeros((CELL, CELL, 4), np.float32)
    nrm = np.zeros((CELL, CELL, 4), np.float32)
    nrm[..., :3] = (0.5, 0.5, 1.0)
    nrm[..., 3] = 1.0
    base = (CELL * 0.5 + rng.uniform(-40, 40), 18)
    tip = (CELL * 0.5 + rng.uniform(-60, 60), CELL * rng.uniform(0.62, 0.72))
    bend = rng.uniform(-50, 50)
    pts = twig(col, base[0], base[1], tip[0], tip[1], bend, 3.5, rng)
    # Leaves on alternate sides, the lower ones larger, drawn tip first so the
    # lower leaves overlap the upper ones as they would hanging from a twig.
    n = rng.randint(9, 13)
    order = []
    for k in range(n):
        t = 0.18 + 0.8 * k / (n - 1)
        side = 1 if k % 2 else -1
        order.append((t, side))
    order.append((1.0, 0))
    for t, side in reversed(order):
        i = min(40, int(t * 40))
        x, y, _ = pts[i]
        x2, y2, _ = pts[max(0, i - 1)]
        tang = math.atan2(-(x - x2), (y - y2)) if i > 0 else 0.0
        ang = tang + side * rng.uniform(0.55, 1.1)
        length = CELL * rng.uniform(0.26, 0.36) * (1.15 - 0.35 * t)
        lc, ln, la = leaves[rng.randrange(len(leaves))]
        paste(col, nrm, lc, ln, la, x, y, ang, length, rng.uniform(0.78, 1.05))
    cells.append((col, nrm, oy, ox))


def spray_sprig(cells, sprigs, rng, oy, ox):
    """A conifer bough: sprigs fanning from a base near the bottom of the cell."""
    col = np.zeros((CELL, CELL, 4), np.float32)
    nrm = np.zeros((CELL, CELL, 4), np.float32)
    nrm[..., :3] = (0.5, 0.5, 1.0)
    nrm[..., 3] = 1.0
    base = (CELL * 0.5, 14)
    twig(col, base[0], base[1], CELL * 0.5 + rng.uniform(-20, 20), CELL * 0.8, rng.uniform(-20, 20), 4.0, rng)
    n = rng.randint(6, 8)
    for k in range(n):
        t = k / (n - 1)
        y = base[1] + CELL * 0.72 * t
        side = 1 if k % 2 else -1
        ang = side * rng.uniform(0.35, 0.8) * (1.0 - 0.5 * t)
        length = CELL * rng.uniform(0.42, 0.55) * (1.1 - 0.4 * t)
        sc, sn, sa = sprigs[rng.randrange(len(sprigs))]
        paste(col, nrm, sc, sn, sa, base[0], y, ang, length, rng.uniform(0.8, 1.05))
    # One along the twig to close the tip.
    sc, sn, sa = sprigs[rng.randrange(len(sprigs))]
    paste(col, nrm, sc, sn, sa, base[0], CELL * 0.45, 0.0, CELL * 0.5, 1.0)
    cells.append((col, nrm, oy, ox))


def bleed(col):
    """Fill transparent texels with the colour of the nearest leaves (blurred), so
    mip levels do not pull in a black fringe."""
    a = col[..., 3:4]
    c = col[..., :3] * a
    w = a.copy()
    for _ in range(6):
        c = (c + np.roll(c, 1, 0) + np.roll(c, -1, 0) + np.roll(c, 1, 1) + np.roll(c, -1, 1)) / 5
        w = (w + np.roll(w, 1, 0) + np.roll(w, -1, 0) + np.roll(w, 1, 1) + np.roll(w, -1, 1)) / 5
    fill = c / np.maximum(w, 1e-4)
    col[..., :3] = np.where(a > 0.5, col[..., :3], fill)
    return col


def main():
    os.makedirs(OUT, exist_ok=True)
    for kind, (aid, shape) in KINDS.items():
        d = os.path.join(SRC, aid)
        color = load(os.path.join(d, f"{aid}_1K-JPG_Color.jpg"))
        alpha = load(os.path.join(d, f"{aid}_1K-JPG_Opacity.jpg"), True)[..., 0]
        normal = load(os.path.join(d, f"{aid}_1K-JPG_NormalGL.jpg"), True)
        parts = []
        for (x0, y0, x1, y1) in pieces(alpha):
            c = color[y0:y1, x0:x1]
            n = normal[y0:y1, x0:x1]
            a = alpha[y0:y1, x0:x1]
            if shape == "sprig":
                # Sprigs lie across the atlas, stem at the left: stand them up.
                c, n, a = np.rot90(c, 1).copy(), np.rot90(n, 1).copy(), np.rot90(a, 1).copy()
                # rot90 turns the normal map's axes too.
                n = n.copy()
                nx, ny = n[..., 0].copy(), n[..., 1].copy()
                n[..., 0], n[..., 1] = 1.0 - ny, nx
            parts.append((c, n, a))
        # Leaves stand stem-down in the atlas (row 0 is the bottom): nothing to turn.
        rng = random.Random(hash(kind) & 0xFFFF)
        cells = []
        for k in range(4):
            oy, ox = (k // 2) * CELL, (k % 2) * CELL
            if shape == "sprig":
                spray_sprig(cells, parts, rng, oy, ox)
            else:
                spray_leaf(cells, parts, rng, oy, ox)
        col = np.zeros((CELL * 2, CELL * 2, 4), np.float32)
        nrm = np.zeros((CELL * 2, CELL * 2, 4), np.float32)
        for c, n, oy, ox in cells:
            col[oy:oy + CELL, ox:ox + CELL] = c
            nrm[oy:oy + CELL, ox:ox + CELL] = n
        col = bleed(col)
        nrm[..., 3] = 1.0
        save(os.path.join(OUT, f"{kind}_col.png"), col)
        save(os.path.join(OUT, f"{kind}_nrm.png"), nrm, True)
        cover = float((col[..., 3] > 0.5).mean())
        print(f"{kind:8s} {aid}: {len(parts)} leaves cut, coverage {cover:.2f}")


main()
