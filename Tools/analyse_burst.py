#!/usr/bin/env python3
"""Measure shimmer in a frame burst recorded by the player's -sfburst option.

    Builds/StarForge.app/Contents/MacOS/StarForge -sfbench 22 -sfquality high \
        -sfburst /tmp/high.raw -sfburstat 8 -sfburstframes 60 [-sfburstzoom 150]
    python3 Tools/analyse_burst.py /tmp/high.raw

The camera is held still during the burst, so any pixel that changes is either
something moving (units, wind, water) or flicker. Two kinds of flicker are told
apart from motion:

  vibrating  pixels that jump up and down on consecutive frames instead of
             drifting (noise re-rolled every frame);
  flashes    a pixel that turns bright for a frame or two and drops back, well
             above everything around it in time (a firefly: one shaded sample
             blowing up, which bloom then spreads into a visible light).

Writes <name>_heat.bmp next to the input: grey is the average change per frame,
cyan marks vibrating pixels, red marks pixels that spike well above their own
median, yellow marks flashes. Needs only numpy.
"""
import struct
import sys

import numpy as np


def write_bmp(path, rgb):
    h, w, _ = rgb.shape
    pad = b"\0" * ((w * 3 + 3) // 4 * 4 - w * 3)
    rows = b"".join(rgb[y, :, ::-1].tobytes() + pad for y in range(h - 1, -1, -1))
    header = b"BM" + struct.pack("<IHHI", 54 + len(rows), 0, 0, 54)
    info = struct.pack("<IiiHHIIiiII", 40, w, h, 1, 24, 0, len(rows), 2835, 2835, 0, 0)
    with open(path, "wb") as f:
        f.write(header + info + rows)


def main(path):
    raw = np.memmap(path, dtype=np.uint8, mode="r")
    w, h, _ = struct.unpack("<iii", raw[:12].tobytes())
    n = (raw.size - 12) // (w * h * 3)
    frames = raw[12:12 + n * w * h * 3].reshape(n, h, w, 3)

    # Luminance as uint8, frame by frame, rows flipped top-down.
    lum = np.empty((n, h, w), np.uint8)
    wts = np.array([0.299, 0.587, 0.114], np.float32)
    for t in range(n):
        lum[t] = np.clip(np.einsum("hwc,c->hw", frames[t, ::-1].astype(np.float32), wts), 0, 255).astype(np.uint8)

    change = np.zeros((h, w), np.float32)
    flip_count = np.zeros((h, w), np.int32)
    flash_count = np.zeros((h, w), np.int32)
    prev_d = None
    for t in range(1, n):
        d = lum[t].astype(np.int16) - lum[t - 1].astype(np.int16)
        change += np.abs(d)
        if prev_d is not None:
            flip_count += ((d * prev_d < 0) & (np.abs(d) > 8) & (np.abs(prev_d) > 8))
        prev_d = d
    change /= 255.0 * max(1, n - 1)
    vibrating = flip_count / max(1, n - 2) > 0.10

    for t in range(2, n - 2):
        around = np.maximum.reduce([lum[t - 2], lum[t - 1], lum[t + 1], lum[t + 2]]).astype(np.int16)
        flash_count += (lum[t].astype(np.int16) - around > 40) & (lum[t] > 140)
    flashes = flash_count > 0

    median = np.median(lum, axis=0)
    spiking = ((lum.astype(np.int16) - median[None].astype(np.int16)) > 64).any(0)

    # Flashes on otherwise still ground: water glints and moving units flash
    # too, but inside a patch that is changing all over. A firefly is alone.
    busy = (change > 0.02).astype(np.float32)
    k = 21
    c = np.pad(busy, k // 2 + 1).cumsum(0).cumsum(1)
    local = (c[k:, k:] - c[:-k, k:] - c[k:, :-k] + c[:-k, :-k])[:h, :w] / (k * k)
    isolated = flash_count * (local < 0.05)

    print(f"{path}: {n} frames {w}x{h} | mean change {change.mean() * 1000:.2f}e-3 | "
          f"changing >2%/frame {100 * (change > 0.02).mean():.2f}% | vibrating {100 * vibrating.mean():.3f}% | "
          f"spiking {int(spiking.sum())} px | flashes {int(flash_count.sum())} events, "
          f"{int(isolated.sum())} on still ground")

    heat = np.clip(change * 2550, 0, 255).astype(np.uint8)
    img = np.stack([heat] * 3, -1)
    img[vibrating] = [40, 200, 255]
    img[spiking] = [255, 40, 40]
    img[flashes] = [255, 230, 0]
    write_bmp(path.rsplit(".", 1)[0] + "_heat.bmp", img)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
