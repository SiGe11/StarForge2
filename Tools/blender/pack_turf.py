"""Turn the three ground pictures made for this project with an image model into
the detail textures SF_Terrain lays over the meadow and over burnt ground.

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
        --python Tools/blender/pack_turf.py [-- <out dir>]

Blender is only the image library here (the system Python has no imaging module).
Sources (Art/Source/llm/, not in the repository: they cannot be fetched again, so
keep a copy): turf_lush.jpg, turf_dry.jpg, burnt_ground.jpg, 2048 px, made by the
author with Google Gemini on 2026-11-20. Written to Assets/StarForge/Art/Textures/:

  turf_lush.jpg, turf_dry.jpg, ground_burnt.jpg   1024 px, sRGB

Each is a *detail* texture: the picture divided by its own mean colour (in linear
light) and quartered, so 0.25 is "the mean" (a lit blade is three times the mean
and must not clip) and the shader's own tuned colours (SF_Meadow.hlsl's
SFTurfColor, the ash grey) stay the authority on what colour a meadow is; the
picture only says where it is lighter, darker, greener, yellower. Before that each
is

  - cut to one period where it came back as a tile repeated along an axis (the
    burnt one repeats across: shifted half its width it differs from itself by 0.29
    of what two unrelated pictures would);
  - made to tile by laying its last eighth over its first, across then down, with
    a variance-keeping cross-fade (a plain one goes soft and grey along the seam),
    which leaves a tile seven eighths the size with every part of the picture in
    it once. Blending the picture with itself shifted half way -- the first version
    -- showed the middle of it twice in every tile: an echo every half tile;
  - resampled to 1024 px in frequency space (it tiles by now, so that is exact);
  - flattened at large scale (a Gaussian high-pass about a fifth of the tile
    across): a dark blotch or a pale patch in one tile is a dot pattern across a
    meadow once it repeats every few metres.

"Half-shift error" below is how unlike a picture is to itself shifted half its
size along x, along y and along both: 1 for unrelated halves, 0 for an exact
repeat. **A packed texture should read 0.9 or more on all three** unless its source
already repeats (turf_dry's reads about 0.55 across). The means, those errors and
what was done go to Tools/blender/turf_textures.json (beside the textures on a
trial run to another folder, which also writes each tile laid 2 x 2 to look at).
"""
import json
import os
import sys

import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "Art", "Source", "llm")
OUT = os.path.join(ROOT, "Assets", "StarForge", "Art", "Textures")
SIZE = 1024
OVERLAP = 0.125          # the share of the picture laid over its other edge
REPEATS_BELOW = 0.4      # a half-shift error under this along an axis: a repeated tile

# name out, source file, how much of the large-scale light and dark is taken out,
# the high-pass's sigma as a share of the tile
JOBS = [
    ("turf_lush", "turf_lush.jpg", 0.75, 0.10),
    ("turf_dry", "turf_dry.jpg", 0.75, 0.10),
    ("ground_burnt", "burnt_ground.jpg", 0.85, 0.07),
]


def to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1.0 / 2.4) - 0.055)


def load_linear(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float64).reshape(h, w, 4)[..., :3]
    bpy.data.images.remove(img)
    return to_linear(px)


def half_shift_errors(a):
    """(x, y, both): the picture against itself shifted half its size, as a share
    of what two unrelated pictures of its contrast would differ by."""
    lum = a.mean(axis=2)
    h, w = lum.shape
    scale = lum.std() * np.sqrt(2.0) + 1e-9
    out = []
    for dy, dx in ((0, w // 2), (h // 2, 0), (h // 2, w // 2)):
        d = lum - np.roll(lum, (dy, dx), axis=(0, 1))
        out.append(float(np.sqrt((d * d).mean()) / scale))
    return out


def lap_axis(a, axis):
    """Seamless along one axis without showing anything twice: the last OVERLAP
    of the picture is cross-faded over its first, and the result is that much
    shorter. Its first column is then the one that followed its new last."""
    n = a.shape[axis]
    o = int(round(n * OVERLAP))
    a = np.moveaxis(a, axis, 0)
    w = (np.arange(o) + 0.5) / o
    w = (w * w * (3.0 - 2.0 * w)).reshape(o, 1, 1)
    mean = a.mean(axis=(0, 1), keepdims=True)
    head, tail = a[:o] - mean, a[n - o:] - mean
    out = a[: n - o].copy()
    out[:o] = mean + (head * w + tail * (1.0 - w)) / np.sqrt(w * w + (1.0 - w) * (1.0 - w))
    return np.moveaxis(out, 0, axis)


def resize_periodic(a, m):
    """A tiling picture resampled to m x m by cutting (or padding) its spectrum,
    with a cosine roll-off over the top sixth of what is kept so edges do not ring."""
    h, w = a.shape[:2]
    out = np.empty((m, m, 3), dtype=np.float64)
    keep_y, keep_x = min(h, m), min(w, m)

    def taper(k):
        f = np.abs(np.fft.fftshift(np.fft.fftfreq(k))) * 2.0          # 0 .. 1 at Nyquist
        return np.where(f < 5.0 / 6.0, 1.0, 0.5 * (1.0 + np.cos((f - 5.0 / 6.0) * 6.0 * np.pi)))

    win = taper(keep_y)[:, None] * taper(keep_x)[None, :]
    for c in range(3):
        f = np.fft.fftshift(np.fft.fft2(a[..., c]))
        y0, x0 = (h - keep_y) // 2, (w - keep_x) // 2
        core = f[y0:y0 + keep_y, x0:x0 + keep_x] * win
        g = np.zeros((m, m), dtype=complex)
        y1, x1 = (m - keep_y) // 2, (m - keep_x) // 2
        g[y1:y1 + keep_y, x1:x1 + keep_x] = core
        out[..., c] = np.fft.ifft2(np.fft.ifftshift(g)).real * (m * m) / (h * w)
    return out


def flatten(a, strength, sigma_share):
    """Takes `strength` of everything broader than the Gaussian out (the picture
    tiles by now, so the blur is a product in frequency space)."""
    h, w = a.shape[:2]
    fy = np.fft.fftfreq(h)[:, None]
    fx = np.fft.fftfreq(w)[None, :]
    sigma = sigma_share * h
    g = np.exp(-2.0 * (np.pi * sigma) ** 2 * (fx * fx + fy * fy))
    out = np.empty_like(a)
    for c in range(3):
        ch = np.maximum(a[..., c], 0.0)          # the resampling rings a little below 0
        mean = ch.mean()
        low = np.fft.ifft2(np.fft.fft2(ch) * g).real
        # As a gain, not an offset: taken off as an offset the darkest gaps between
        # blades went below zero and 6-8% of the texture clipped to black.
        out[..., c] = ch / np.maximum(1.0 + strength * (low / mean - 1.0), 0.25)
    return out


def to_detail(a):
    """0.25 x the picture over its own mean, clipped to what 8 bits hold, with the
    mean put back on 0.25 after the clip (clipping the bright end alone pulls it
    down). Returns it and the share of values clipped at either end."""
    d = 0.25 * a / a.mean(axis=(0, 1))
    for _ in range(4):
        d = np.clip(d, 0.0, 1.0)
        d *= 0.25 / d.mean(axis=(0, 1))
    clipped = float(((d <= 0.0) | (d >= 1.0)).mean())
    return np.clip(d, 0.0, 1.0), clipped


def wrap_step(a):
    """The step across the wrap against the step between neighbours inside, both
    as mean absolute differences of luma: about 1 for a texture that tiles."""
    lum = a.mean(axis=2)
    inside = np.abs(np.diff(lum, axis=1)).mean()
    return float(np.abs(lum[:, 0] - lum[:, -1]).mean() / inside), float(np.abs(lum[0, :] - lum[-1, :]).mean() / inside)


def save(a_srgb, path):
    h, w = a_srgb.shape[:2]
    out = np.ones((h, w, 4), dtype=np.float32)
    out[..., :3] = a_srgb
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=False)
    img.pixels[:] = out.ravel()
    img.file_format = "JPEG"
    img.save(filepath=path, quality=93)
    bpy.data.images.remove(img)


def main():
    out_dir = OUT
    if "--" in sys.argv:
        rest = sys.argv[sys.argv.index("--") + 1:]
        if rest:
            out_dir = os.path.abspath(rest[0])
    os.makedirs(out_dir, exist_ok=True)
    info = {}
    for name, src, strength, sigma in JOBS:
        a = load_linear(os.path.join(SRC, src))
        note = []
        source_err = half_shift_errors(a)
        if min(source_err[0], source_err[1]) < REPEATS_BELOW:
            # A tile repeated along an axis: one period is the whole picture. Cut
            # square, so a blade is as long across as down.
            h, w = a.shape[:2]
            a = a[: h // 2, : w // 2]
            note.append("one period of a repeated tile (a quarter of the picture)")
        a = lap_axis(a, 1)
        a = lap_axis(a, 0)
        lapped = a.shape[0]
        # Resampled as the eye's values, not linear light: in linear light a lit
        # blade is many times the dark between blades, and the resampling's ripple
        # round each one took 7% of the texture below zero.
        a = to_linear(np.clip(resize_periodic(to_srgb(a), SIZE), 0.0, 1.0))
        a = flatten(a, strength, sigma)
        mean = a.mean(axis=(0, 1))
        detail, clipped = to_detail(a)
        err = half_shift_errors(detail)
        step = wrap_step(detail)
        save(to_srgb(detail).astype(np.float32), os.path.join(out_dir, name + ".jpg"))
        if out_dir != OUT:
            # A trial run outside the project: the tile laid 2 x 2, brightened to be read.
            save(np.tile(to_srgb(np.clip(detail * 2.0, 0, 1))[::2, ::2], (2, 2, 1)).astype(np.float32),
                 os.path.join(out_dir, name + "_tiled_preview.jpg"))
        info[name] = {
            "source": "Art/Source/llm/" + src,
            "made_with": "Google Gemini, by the author, 2026-11-20",
            "mean_linear": [round(float(v), 4) for v in mean],
            "mean_srgb": [round(float(v), 3) for v in to_srgb(mean)],
            "detail_mean_x4": [round(float(v) * 4.0, 4) for v in detail.mean(axis=(0, 1))],
            "half_shift_error_source_x_y_xy": [round(v, 2) for v in source_err],
            "half_shift_error_packed_x_y_xy": [round(v, 2) for v in err],
            "wrap_step_x_y": [round(v, 2) for v in step],
            "lapped_px_before_resize": lapped,
            "share_clipped": round(clipped, 5),
            "notes": note,
        }
        print(f"{name:13s} {lapped}->{SIZE}  mean srgb {info[name]['mean_srgb']}  half-shift x/y/xy source "
              f"{source_err[0]:.2f}/{source_err[1]:.2f}/{source_err[2]:.2f} packed {err[0]:.2f}/{err[1]:.2f}/{err[2]:.2f}"
              f"  wrap {step[0]:.2f}/{step[1]:.2f}  clipped {clipped * 100:.2f}%  {'; '.join(note)}")
    # Nothing in the game reads the record, so it stays out of Assets/.
    record = os.path.dirname(os.path.abspath(__file__)) if out_dir == OUT else out_dir
    with open(os.path.join(record, "turf_textures.json"), "w") as f:
        json.dump(info, f, indent=2)


main()
