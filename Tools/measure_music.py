#!/usr/bin/env python3
"""Measure how calm or how unsettling a piece of music sounds.

    python3 Tools/measure_music.py Assets/StarForge/Audio/Music/music_calm_*.ogg
    python3 Tools/measure_music.py --segments some_candidate.wav

Picking the calm music from titles and descriptions does not work: two tracks
both called "calm" put a dark minor piano loop under a quiet base, and that
reads as creepy, not peaceful. These numbers say what a track is actually doing,
and can be compared between candidates:

  key / minor_share   the key (Krumhansl profiles) and how much of the playing
                      time the sounding harmony is minor rather than major --
                      the single best predictor of an unsettling track
  dissonance          semitone and tritone pairs sounding together, weighted
  roughness           beating: partials 15-150 Hz apart, which is what makes a
                      chord sound tense rather than open
  brightness          spectral centroid in Hz; low is dark and close, high is airy
  onsets_per_min      note density -- a restful track is sparse
  dynamic_swell       loud passages over quiet ones; a big number lurches

The calm set aims for major keys, minor_share well under 0.5, dissonance under
about 0.05 and a swell under 3. --segments prints the same per 20 seconds, which
catches a dark passage inside an otherwise warm track.

Needs numpy, and macOS's afconvert to decode.
"""
import argparse
import os
import subprocess
import sys
import tempfile

import numpy as np

SR = 22050
NAMES = "C C# D D# E F F# G G# A A# B".split()
# Krumhansl-Kessler key profiles.
MAJ = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
MIN = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])


def decode(path):
    """Mono float samples, through afconvert (it reads ogg, mp3 and wav)."""
    with tempfile.TemporaryDirectory() as d:
        wav = os.path.join(d, "t.wav")
        r = subprocess.run(["afconvert", "-f", "WAVE", "-d", f"LEI16@{SR}", "-c", "1", path, wav],
                           capture_output=True, text=True)
        if r.returncode != 0:
            raise RuntimeError(f"afconvert could not decode it: {(r.stderr or r.stdout).strip()[:300]}")
        b = open(wav, "rb").read()
        return np.frombuffer(b[b.find(b"data") + 8:], dtype="<i2").astype(np.float64) / 32768.0


def spectra(x, n=4096, hop=2048):
    w = np.hanning(n)
    return (np.array([np.abs(np.fft.rfft(x[i:i + n] * w)) for i in range(0, len(x) - n, hop)]),
            np.fft.rfftfreq(n, 1.0 / SR))


def chroma(S, f):
    """Energy per pitch class, per frame, over the frames that are playing."""
    ok = (f > 55) & (f < 2000)
    cls = np.rint(69 + 12 * np.log2(np.where(ok, f, 440.0) / 440.0)).astype(int) % 12
    c = np.array([S[:, ok & (cls == p)].sum(1) for p in range(12)]).T
    live = c.sum(1) > np.percentile(c.sum(1), 35)
    c = c[live]
    return c / (c.sum(1, keepdims=True) + 1e-9), live


def fit(row, profile):
    return max(np.corrcoef(np.roll(row, -k), profile)[0, 1] for k in range(12))


def minor_share(c):
    return float(np.mean([fit(r, MIN) > fit(r, MAJ) for r in c]))


def dissonance(c):
    """Semitones and tritones among the four loudest pitch classes."""
    out = []
    for r in c:
        idx = np.argsort(r)[-4:]
        w = r[idx] / (r[idx].sum() + 1e-9)
        d = 0.0
        for a in range(4):
            for b in range(a + 1, 4):
                iv = abs(int(idx[a]) - int(idx[b])) % 12
                if min(iv, 12 - iv) in (1, 6):
                    d += w[a] * w[b]
        out.append(d)
    return float(np.mean(out))


def moving_rms(x, n):
    """np.sqrt(np.convolve(x * x, np.ones(n) / n, "same")), by a running sum: the direct
    convolution with an 11,025-tap window took ~2.7 s a 4-minute track, this ~0.03 s."""
    c = np.concatenate(([0.0], np.cumsum(x * x)))
    k = np.arange(len(x)) + (n - 1) // 2            # "same" = the full convolution from (n-1)//2
    hi = np.minimum(k, len(x) - 1) + 1
    lo = np.maximum(k - n + 1, 0)
    return np.sqrt(np.maximum(c[hi] - c[lo], 0.0) / n)


def measure(x):
    S, f = spectra(x)
    c, live = chroma(S, f)
    g = c.mean(0)
    key = max(((fit_v, f"{NAMES[r]} {name}")
               for r in range(12)
               for prof, name in ((MAJ, "major"), (MIN, "minor"))
               for fit_v in [np.corrcoef(np.roll(g, -r), prof)[0, 1]]))
    rough = []
    for row in S[live][::4]:
        pk = np.argsort(row)[-24:]
        e = row[pk] / (row[pk].sum() + 1e-9)
        d = np.abs(f[pk][:, None] - f[pk][None, :])
        rough.append(float((np.outer(e, e) * ((d > 15) & (d < 150))).sum() / 2))
    flux = np.maximum(0, np.diff(S.sum(1)))
    env = moving_rms(x, SR // 2)
    return {
        "key": key[1],
        "minor_share": round(minor_share(c), 2),
        "dissonance": round(dissonance(c), 3),
        "roughness": round(float(np.mean(rough)), 3),
        "brightness": int((S[live] * f).sum() / (S[live].sum() + 1e-9)),
        "onsets_per_min": round(int((flux > flux.mean() + 2 * flux.std()).sum()) / (len(x) / SR) * 60, 1),
        "dynamic_swell": round(float(np.percentile(env, 95) / (np.percentile(env, 25) + 1e-6)), 1),
        "seconds": round(len(x) / SR, 1),
    }


def segments(x, window=20):
    out = []
    for s in range(0, max(1, len(x) - SR * 5), SR * window):
        S, f = spectra(x[s:s + SR * window])
        c, _ = chroma(S, f)
        out.append((s // SR, round(minor_share(c), 2), round(dissonance(c), 3)))
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("files", nargs="+")
    ap.add_argument("--segments", action="store_true", help="also print minor share and dissonance per 20 s")
    a = ap.parse_args()
    failed = 0
    for path in a.files:
        try:
            x = decode(path)
        except Exception as e:   # one bad file must not hide the rest
            print(f"{os.path.basename(path):34s} FAILED: {e}")
            failed += 1
            continue
        m = measure(x)
        print(f"{os.path.basename(path):34s} " + "  ".join(f"{k}={v}" for k, v in m.items()))
        if a.segments:
            print("    " + "  ".join(f"{t}s: minor {mi} diss {d}" for t, mi, d in segments(x)))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
