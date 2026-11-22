#!/usr/bin/env python3
"""optimize_pngs.py -- lossless PNG recompression for the repo's LFS textures.

Re-encodes only the pixel data (zlib level 9 with Pillow's per-row filter choice), keeps every
other chunk byte for byte (sRGB, gAMA, cHRM, eXIf, pHYs, ...: Unity reads gAMA while a texture's
`ignorePngGamma` is 0), and **proves the result decodes to exactly the same pixels** before it
counts. A file that does not shrink enough (by `--min-gain` bytes and `--min-ratio` of its size), is
16-bit or interlaced, carries a C2PA provenance manifest (`caBX`: its signature covers the file's bytes) or
fails the check is left alone. The ratio floor matters because every rewritten LFS file stores its whole new
size in history for what it saves on checkout: a 1% gain is a bad trade.

Why it exists: Blender and the packers write PNGs with default compression, so a bake or pack
hands back ~12% (on the scanned-plant sheets up to 60%) that this takes out again. Run it on
what a bake wrote, before committing. It never changes what Unity imports (the pixels are equal),
only the file's bytes: Unity reimports the file once.

    /usr/bin/python3 Tools/optimize_pngs.py PATH...                 dry run: report only
    /usr/bin/python3 Tools/optimize_pngs.py --stage DIR PATH...     write the smaller files under DIR
                                                                    (mirroring the repo path) + manifest.json
    /usr/bin/python3 Tools/optimize_pngs.py --apply DIR             copy DIR's files over the repo's, but only
                                                                    those still byte-identical to what was measured
    /usr/bin/python3 Tools/optimize_pngs.py --in-place PATH...      recompress and replace right away

PATH is a file or a folder (searched for *.png). Needs Pillow and numpy: `/usr/bin/python3` has both
(the Homebrew python3 does not). Staging outside Assets/ lets a peer's Editor run on undisturbed;
`--apply` is the one step that touches Assets/ (and makes Unity reimport), so do it in a window.
"""
import argparse, hashlib, io, json, os, struct, sys, zlib

try:
    import numpy as np
    from PIL import Image
except ImportError:
    sys.exit("needs Pillow and numpy: run it with /usr/bin/python3")

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SIG = b"\x89PNG\r\n\x1a\n"


def chunks(b):
    p, out = 8, []
    while p < len(b):
        n = struct.unpack(">I", b[p:p + 4])[0]
        out.append((b[p + 4:p + 8], b[p + 8:p + 8 + n]))
        p += 12 + n
    return out


def mk(t, d):
    c = struct.pack(">I", len(d)) + t + d
    return c + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)


def sha(b):
    return hashlib.sha256(b).hexdigest()


def read16(cs):
    """16-bit RGB, non-interlaced PNG chunks -> (h, w, 3) uint16 (Pillow would drop the low byte)."""
    w, h = struct.unpack(">II", cs[0][1][:8])
    raw = zlib.decompress(b"".join(d for t, d in cs if t == b"IDAT")); bpp, stride = 6, w * 6
    img = np.zeros((h, stride), np.uint8); prev = np.zeros(stride, np.int32)
    for y in range(h):
        f = raw[y * (stride + 1)]; line = np.frombuffer(raw, np.uint8, stride, y * (stride + 1) + 1).astype(np.int32)
        if f == 0: cur = line.copy()
        elif f == 2: cur = (line + prev) & 255
        elif f == 1:
            cur = line.copy()
            for c in range(bpp): cur[c::bpp] = np.cumsum(line[c::bpp]) & 255
        else:
            cur = np.zeros(stride, np.int32)
            for i in range(stride):
                a = cur[i - bpp] if i >= bpp else 0; bb = prev[i]; c = prev[i - bpp] if i >= bpp else 0
                if f == 3: pred = (a + bb) >> 1
                else:
                    pp = a + bb - c; pa, pb, pc = abs(pp - a), abs(pp - bb), abs(pp - c)
                    pred = a if (pa <= pb and pa <= pc) else (bb if pb <= pc else c)
                cur[i] = (line[i] + pred) & 255
        img[y] = cur; prev = cur
    a = img.reshape(h, w, 3, 2).astype(np.uint32)
    return (a[..., 0] << 8 | a[..., 1]).astype(np.uint16)


def to_8bit(b, cs):
    """A 16-bit RGB PNG as 8-bit, rounded (v*255/65535), other chunks kept. LOSSY at the source (max error 0.5 of 255);
    Unity imports 8 bits anyway. Only with --to-8bit: it is a decision, not a recompression."""
    a8 = ((read16(cs).astype(np.uint32) * 255 + 32767) // 65535).astype(np.uint8)
    buf = io.BytesIO(); Image.fromarray(a8).save(buf, "PNG", optimize=True, compress_level=9)
    idat = [d for t, d in chunks(buf.getvalue()) if t == b"IDAT"]
    out = SIG + b"".join(mk(t, d) if t != b"IHDR" else mk(b"IHDR", chunks(buf.getvalue())[0][1]) for t, d in cs if t not in (b"IDAT", b"IEND"))
    return out + b"".join(mk(b"IDAT", d) for d in idat) + mk(b"IEND", b""), a8


def recompress(b, convert16=False):
    """(new bytes, reason-or-None). new is None when the file must be left alone."""
    if b[:8] != SIG:
        return None, "not a PNG"
    cs = chunks(b)
    _, _, bitdepth, _, _, _, interlace = struct.unpack(">IIBBBBB", cs[0][1][:13])
    if any(t == b"caBX" for t, _ in cs):
        return None, "C2PA manifest (caBX): re-encoding would break its signature"
    if bitdepth == 16 and convert16 and cs[0][1][9] == 2 and not interlace:
        out, _ = to_8bit(b, cs)
        return out, None                       # no pixel proof possible across bit depths: --to-8bit is a lossy decision
    if bitdepth != 8:
        return None, "%d-bit (Pillow would reduce it; --to-8bit converts a 16-bit RGB one)" % bitdepth
    if interlace:
        return None, "interlaced"
    im = Image.open(io.BytesIO(b))
    im.load()
    buf = io.BytesIO()
    im.save(buf, "PNG", optimize=True, compress_level=9)
    idat = [d for t, d in chunks(buf.getvalue()) if t == b"IDAT"]
    out = SIG + b"".join(mk(t, d) for t, d in cs if t not in (b"IDAT", b"IEND"))
    out += b"".join(mk(b"IDAT", d) for d in idat) + mk(b"IEND", b"")
    # The proof: both decode (Pillow checks every chunk's CRC) to the same shape, type and values.
    a = np.asarray(Image.open(io.BytesIO(b)))
    c = np.asarray(Image.open(io.BytesIO(out)))
    if not (a.shape == c.shape and a.dtype == c.dtype and np.array_equal(a, c)):
        return None, "PIXELS DIFFER: left alone"
    return out, None


def find(paths):
    for p in paths:
        p = os.path.abspath(p)
        if os.path.isdir(p):
            for d, _, fs in os.walk(p):
                for f in sorted(fs):
                    if f.lower().endswith(".png"):
                        yield os.path.join(d, f)
        elif p.lower().endswith(".png"):
            yield p


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("paths", nargs="*")
    ap.add_argument("--stage", metavar="DIR")
    ap.add_argument("--in-place", action="store_true")
    ap.add_argument("--apply", metavar="DIR")
    ap.add_argument("--to-8bit", action="store_true", help="also convert 16-bit RGB PNGs to 8-bit (lossy at the source, max error 0.5/255)")
    ap.add_argument("--min-gain", type=int, default=2048, help="skip a file that would save fewer bytes (default 2048)")
    ap.add_argument("--min-ratio", type=float, default=0.05, help="skip a file that would save less than this share of its size (default 0.05)")
    a = ap.parse_args()

    if a.apply:
        man = json.load(open(os.path.join(a.apply, "manifest.json")))
        bad = [m["path"] for m in man["files"]
               if not os.path.exists(os.path.join(ROOT, m["path"]))
               or sha(open(os.path.join(ROOT, m["path"]), "rb").read()) != m["old_sha256"]]
        if bad:
            sys.exit("refusing: changed (or missing) since they were measured, restage first:\n  " + "\n  ".join(bad))
        saved = 0
        for m in man["files"]:
            dst = os.path.join(ROOT, m["path"])
            new = open(os.path.join(a.apply, m["path"]), "rb").read()
            if sha(new) != m["new_sha256"]:
                sys.exit("staged file corrupt: " + m["path"])
            tmp = dst + ".optmp"
            open(tmp, "wb").write(new)
            os.replace(tmp, dst)
            saved += m["old"] - m["new"]
        print("applied %d files, %.2f MB smaller" % (len(man["files"]), saved / 1e6))
        return

    if not a.paths:
        ap.error("give PATH... (or --apply DIR)")
    rows, tot_old, tot_new = [], 0, 0
    for f in find(a.paths):
        rel = os.path.relpath(f, ROOT)
        b = open(f, "rb").read()
        try:
            new, why = recompress(b, a.to_8bit)
        except Exception as e:  # a PNG Pillow cannot read stays as it is
            new, why = None, "error: %s" % e
        if new is not None and (len(b) - len(new) < a.min_gain or len(b) - len(new) < a.min_ratio * len(b)):
            new, why = None, "gain too small (%.1f%%)" % (100.0 * (len(b) - len(new)) / len(b))
        if new is None:
            print("  keep   %-70s %s" % (rel, why))
            continue
        tot_old += len(b)
        tot_new += len(new)
        print("  %-6s %-70s %8.2f -> %8.2f MB" % ("smaller", rel, len(b) / 1e6, len(new) / 1e6))
        rows.append(dict(path=rel, old=len(b), new=len(new), old_sha256=sha(b), new_sha256=sha(new), to8=b[24] == 16))
        if a.stage:
            dst = os.path.join(a.stage, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            open(dst, "wb").write(new)
        elif a.in_place:
            tmp = f + ".optmp"
            open(tmp, "wb").write(new)
            os.replace(tmp, f)
    lossy = sum(1 for r in rows if r["to8"])
    print("%d files shrink: %.2f -> %.2f MB (saves %.2f MB; %d verified pixel-identical%s)" %
          (len(rows), tot_old / 1e6, tot_new / 1e6, (tot_old - tot_new) / 1e6, len(rows) - lossy,
           ", %d converted 16 to 8 bit: LOSSY at the source" % lossy if lossy else ""))
    if a.stage:
        json.dump(dict(files=rows), open(os.path.join(a.stage, "manifest.json"), "w"), indent=1)
        print("staged under", a.stage, "-- `--apply` copies them in (a window: Unity reimports each once)")
    elif not a.in_place:
        print("dry run: nothing written")


if __name__ == "__main__":
    main()
