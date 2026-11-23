#!/usr/bin/env python3
"""Compares two folders of PNG shots of the same frames, by name.

    python3 Tools/diff_shots.py <dir_a> <dir_b> [--group] [--top N] [--match TEXT]

For each shot: the mean absolute difference of the 8x box-filtered pictures, in 0-255 steps a
channel (so a pixel-level shimmer or a shifted edge averages out, and a surface that changed colour
does not), and the worst 8x8 block. Run it on two runs of the same build first: that is the noise
the comparison has to be read against.

--group sums a model sheet (Editor/ModelSheet.cs) by model, team and state (names that are not
<model>_<team>_<state> go under "other"). Shots in one folder only, or of different sizes, are
counted in the last line and make the exit status 1. Needs numpy; uses Pillow when it is there
(/usr/bin/python3 has both: much faster, and it reads palette and grey PNGs), else the reader here.
"""
import argparse
import os
import struct
import sys
import zlib

import numpy as np

try:
    from PIL import Image
except ImportError:
    Image = None


def read_png(path):
    if Image is not None:
        with open(path, 'rb') as fh:
            head = fh.read(29)
        # Pillow would quietly take 8 of a 16-bit channel's bits: refused, as the reader below does.
        if head[:8] == b'\x89PNG\r\n\x1a\n' and head[12:16] == b'IHDR' and head[24] != 8 and head[25] != 3:
            sys.exit('%s: %d bits a channel' % (path, head[24]))
        with Image.open(path) as im:
            return np.asarray(im.convert('RGB'), dtype=np.float32)
    with open(path, 'rb') as fh:
        raw = fh.read()
    if raw[:8] != b'\x89PNG\r\n\x1a\n':
        sys.exit('%s is not a PNG' % path)
    at, idat = 8, []
    w = h = depth = kind = 0
    while at < len(raw):
        n, tag = struct.unpack('>I4s', raw[at:at + 8])
        body = raw[at + 8:at + 8 + n]
        if tag == b'IHDR':
            w, h, depth, kind = struct.unpack('>IIBB', body[:10])
            if body[12] != 0:
                sys.exit('%s: interlaced' % path)
        elif tag == b'IDAT':
            idat.append(body)
        elif tag == b'IEND':
            break
        at += 12 + n
    if kind == 3:
        sys.exit('%s: palette PNG; run with /usr/bin/python3 (Pillow) to read it' % path)
    channels = {0: 1, 2: 3, 4: 2, 6: 4}[kind]
    if depth != 8:
        sys.exit('%s: %d bits a channel' % (path, depth))
    stride = w * channels
    data = np.frombuffer(zlib.decompress(b''.join(idat)), dtype=np.uint8)
    out = np.zeros((h, stride), dtype=np.uint8)
    prev = np.zeros(stride, dtype=np.int32)
    for y in range(h):
        kind_row = data[y * (stride + 1)]
        row = data[y * (stride + 1) + 1:(y + 1) * (stride + 1)].astype(np.int32)
        if kind_row == 1:
            for c in range(channels):
                row[c::channels] = np.add.accumulate(row[c::channels]) & 255
        elif kind_row == 2:
            row = (row + prev) & 255
        elif kind_row == 3:
            line = row.tolist()
            up = prev.tolist()
            for i in range(stride):
                left = line[i - channels] if i >= channels else 0
                line[i] = (line[i] + ((left + up[i]) >> 1)) & 255
            row = np.array(line, dtype=np.int32)
        elif kind_row == 4:
            line = row.tolist()
            up = prev.tolist()
            for i in range(stride):
                a = line[i - channels] if i >= channels else 0
                b = up[i]
                c = up[i - channels] if i >= channels else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
            row = np.array(line, dtype=np.int32)
        out[y] = row
        prev = row
    img = out.reshape(h, w, channels)
    img = np.repeat(img[..., :1], 3, axis=2) if channels < 3 else img[..., :3]   # grey: as RGB
    return img.astype(np.float32)


def box8(img):
    h, w = img.shape[0] // 8 * 8, img.shape[1] // 8 * 8
    return img[:h, :w].reshape(h // 8, 8, w // 8, 8, 3).mean(axis=(1, 3))


def compare(a, b):
    if a.shape != b.shape:
        return None
    d = np.abs(box8(a) - box8(b)).mean(axis=2)
    # A block can hide one pixel far off: the same two pictures with no filter.
    raw = np.abs(a - b).max(axis=2)
    return float(d.mean()), float(d.max()), float(raw.max()), float((raw > 16).mean() * 100.0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('dir_a')
    ap.add_argument('dir_b')
    ap.add_argument('--group', action='store_true')
    ap.add_argument('--top', type=int, default=0)
    ap.add_argument('--match', default='')
    args = ap.parse_args()

    shots = lambda folder: set(f for f in os.listdir(folder) if f.endswith('.png') and args.match in f)
    in_a, in_b = shots(args.dir_a), shots(args.dir_b)
    only = sorted(in_a ^ in_b)
    for name in only:
        print('%-40s only in %s' % (name, args.dir_a if name in in_a else args.dir_b))
    rows, sizes = [], []
    for name in sorted(in_a & in_b):
        r = compare(read_png(os.path.join(args.dir_a, name)), read_png(os.path.join(args.dir_b, name)))
        if r is None:
            print('%-40s sizes differ' % name)
            sizes.append(name)
            continue
        rows.append((name[:-4],) + r)
    if not rows:
        sys.exit('nothing to compare (%d only in one folder, %d size mismatches)' % (len(only), len(sizes)))

    if args.group:
        by = {}
        for name, mean, worst, far, share in rows:
            parts = name.rsplit('_', 2)
            if len(parts) == 3:
                model, team, state = parts
                keys = ('model ' + model, 'team ' + team, 'state ' + state, 'all')
            else:
                keys = ('other', 'all')
            for key in keys:
                by.setdefault(key, []).append((mean, worst, far, share))
        for key in sorted(by):
            v = np.array(by[key])
            print('%-14s %4d shots  mean %.3f  worst shot mean %.3f  worst block %.1f  worst pixel %.0f  most pixels over 16 %.2f%%' % (
                key, len(v), v[:, 0].mean(), v[:, 0].max(), v[:, 1].max(), v[:, 2].max(), v[:, 3].max()))
    show = sorted(rows, key=lambda r: -r[1])[:args.top] if args.top else rows
    for name, mean, worst, far, share in show:
        print('%-40s mean %6.3f  worst block %6.1f  worst pixel %4.0f  over 16: %.2f%%' % (name, mean, worst, far, share))
    print('%d shots: mean of means %.3f, largest mean %.3f; %d only in one folder, %d size mismatches' % (
        len(rows), np.mean([r[1] for r in rows]), max(r[1] for r in rows), len(only), len(sizes)))
    return 1 if only or sizes else 0


if __name__ == '__main__':
    sys.exit(main())
