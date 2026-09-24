#!/usr/bin/env python3
"""Build StarForge's sound: recorded effects and music, synthesised ambience.

    python3 Tools/fetch_assets.py      # once: downloads the Kenney packs too
    python3 Tools/make_audio.py

1. Effects. Picks recordings from Kenney's CC0 packs (Sci-Fi Sounds, Impact
   Sounds, Interface Sounds; Art/Source/kenney, fetched by fetch_assets.py) and
   copies them under game names into Assets/StarForge/Audio/Sfx/ (Unity plays
   .ogg as it is). A name ending _0, _1 ... is one of several takes the game
   picks between, so a volley of rifles is not one sound repeated.

1b. Weapons and wood, built here from recordings rather than copied, into
   Assets/StarForge/Audio/Sfx/*.wav. Guns come from the Free Firearm Sound
   Library (CC0): every gun was recorded from beside the shooter and again at a
   distance, and a shot is mixed from both -- the crack from the near take, the
   report rolling back over the ground from the far one a moment later -- with a
   slap-back off the terrain behind. The Trooper's rifle is an AR-15, SKS,
   Savage and Tikka (four takes, so a firing line is not one sample repeated);
   the Mauler's gun is a 12-gauge and a .30-06 pitched down an octave; the
   Sentinel's bolt and the Skimmer's plasma keep their Kenney energy sound with
   a real muzzle crack under it, which is what they were missing. Wood: a tree
   cracks and groans as it goes over and crashes with a rush of leaves when it
   lands, from kheetor's tree fall, AntumDeluge's tree creak and rubberduck's
   wood breaks (all CC0).

2. Ambience, synthesised into seamless loops in Assets/StarForge/Audio/Ambience/:
   wind (a low rumble, a mid whoosh that gusts and a faint whistle), water
   lapping at a shore, a fire's roar and crackle, and a handful of bird calls.
   Loops are built by filtering noise in the frequency domain over exactly the
   loop's length, which makes them circular: there is no seam to crossfade.

3. Music, in Assets/StarForge/Audio/Music/: recorded CC0 tracks from
   OpenGameArt (fetched into Art/Source/music by fetch_assets.py), copied under
   the mood they play in -- music_calm_* (At Home by wolfgang: warm orchestral;
   First Light Particles by yoiyami: piano over pads; Contemplation by Joth:
   drifting ambience), music_tension_0 (Insistent by yd, a dark, quiet loop) and
   music_combat_0 (Battle Theme A by cynicmusic, strings and horns). The calm
   set is chosen to be major-key and consonant: the minor piano loops it used to
   hold sounded unsettling under a quiet base. WAV sources are Vorbis-encoded
   through Blender (Tools/blender/encode_audio.py) instead of being committed
   raw. music.json records each file's loudness (RMS) so the game can play them
   all at one level, well under the effects. (The music used to be synthesised
   here; its sine plucks and tremolo beeped.)

Needs numpy, and macOS's afconvert to decode the recordings and measure the music. Ambience is written as
16-bit PCM WAV (Unity compresses it to Vorbis on import).
"""
import math
import os
import shutil
import struct
import sys

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
KENNEY = os.path.join(ROOT, "Art", "Source", "kenney")
SFX_SRC = os.path.join(ROOT, "Art", "Source", "sfx")
GUNS = os.path.join(SFX_SRC, "firearms", "Prepared SFX Library")
AUDIO = os.path.join(ROOT, "Assets", "StarForge", "Audio")
SR = 32000
RNG = np.random.default_rng(1977)

# ---------------------------------------------------------------- effects
# game name: (pack, file). Several takes of a sound share a stem name.
SFX = {
    "boom_0": ("sci-fi-sounds", "explosionCrunch_000"),
    "boom_1": ("sci-fi-sounds", "explosionCrunch_001"),
    "boom_2": ("sci-fi-sounds", "explosionCrunch_002"),
    "boom_3": ("sci-fi-sounds", "explosionCrunch_003"),
    "bigboom_0": ("sci-fi-sounds", "explosionCrunch_004"),
    "rumble_0": ("sci-fi-sounds", "lowFrequency_explosion_000"),
    "thud_0": ("sci-fi-sounds", "lowFrequency_explosion_001"),
    "hitmetal_0": ("sci-fi-sounds", "impactMetal_000"),
    "hitmetal_1": ("sci-fi-sounds", "impactMetal_002"),
    "hitmetal_2": ("sci-fi-sounds", "impactMetal_004"),
    "shield_0": ("sci-fi-sounds", "forceField_000"),
    "build_0": ("sci-fi-sounds", "computerNoise_000"),
    "engine_hover": ("sci-fi-sounds", "engineCircular_001"),
    "crystal_0": ("impact-sounds", "impactGlass_medium_000"),
    "crystal_1": ("impact-sounds", "impactGlass_medium_002"),
    "crystal_2": ("impact-sounds", "impactGlass_light_001"),
    "rock_0": ("impact-sounds", "impactMining_000"),
    "rock_1": ("impact-sounds", "impactMining_001"),
    "mine_0": ("impact-sounds", "impactMining_002"),
    "mine_1": ("impact-sounds", "impactMining_003"),
    "mine_2": ("impact-sounds", "impactMining_004"),
    "stomp_0": ("impact-sounds", "impactPunch_heavy_001"),
    "ui_select": ("interface-sounds", "select_001"),
    "ui_click": ("interface-sounds", "click_002"),
    "ui_move": ("interface-sounds", "tick_002"),
    "ui_attack": ("interface-sounds", "switch_002"),
    "ui_error": ("interface-sounds", "error_004"),
    "ui_done": ("interface-sounds", "confirmation_002"),
    "ui_notice": ("interface-sounds", "question_001"),
    "ui_alert": ("interface-sounds", "bong_001"),
    "ui_promote": ("interface-sounds", "maximize_006"),
    "ui_place": ("interface-sounds", "drop_002"),
}


def copy_sfx():
    out = os.path.join(AUDIO, "Sfx")
    os.makedirs(out, exist_ok=True)
    # Anything this script no longer writes must go, or the game still finds it --
    # including a sound that used to be copied from Kenney (.ogg) and is now built
    # here (.wav) under the same name.
    want = {name: ".ogg" for name in SFX}
    want.update({name: ".wav" for name in list(WEAPONS) + list(WOOD) + list(BLASTS)})
    want["engine_heavy"] = ".wav"
    want["engine_tracks"] = ".wav"
    for f in os.listdir(out):
        stem, ext = os.path.splitext(f)
        if ext in (".ogg", ".wav") and want.get(stem) != ext:
            os.remove(os.path.join(out, f))
            if os.path.exists(os.path.join(out, f + ".meta")):
                os.remove(os.path.join(out, f + ".meta"))
    missing = []
    for name, (pack, src) in SFX.items():
        path = os.path.join(KENNEY, f"kenney_{pack}", "Audio", src + ".ogg")
        if not os.path.exists(path):
            missing.append(path)
            continue
        shutil.copyfile(path, os.path.join(out, name + ".ogg"))
    if missing:
        sys.exit("missing Kenney files (run Tools/fetch_assets.py):\n  " + "\n  ".join(missing))
    print(f"{len(SFX)} effects -> {os.path.relpath(out, ROOT)}")



# ---------------------------------------------------------------- weapons and wood
# Guns: (near take, far take, pitch, length, extra layers). The near take gives the
# crack, the far one the report coming back off the ground a moment later.
RMS = {"rifle": 0.085, "cannon": 0.125, "bolt": 0.10, "pulse": 0.10,
       "treefall": 0.055, "treecrash": 0.095, "crush": 0.05,
       "blast": 0.135, "blastbig": 0.15}
WEAPONS = {
    "rifle_0": dict(near="AR-15/D_32P.wav", far="AR-15/D_24P.wav", pitch=1.04, length=0.85),
    "rifle_1": dict(near="SKS/U_14P.wav", far="SKS/U_19P.wav", pitch=1.0, length=0.85),
    "rifle_2": dict(near="Savage 10 .300 Blackout/T_27P.wav", far="Savage 10 .300 Blackout/T_17P.wav", pitch=1.08, length=0.8),
    "rifle_3": dict(near="Tikka/W_29P.wav", far="Tikka/W_24P.wav", pitch=0.96, length=0.9),
    # The Mauler's gun: a 12-gauge and a .30-06 rifle, both pitched down about an octave.
    # The sub and the roll are deliberately well under the crack. A sine at 45 Hz
    # carries enormous energy for its loudness, and the first pass at these had the
    # shot 95% below 120 Hz: all thud, no gun.
    "cannon_0": dict(near="Mossberg/N_30P.wav", far="Mossberg/N_26P.wav", pitch=0.50, length=2.8, far_gain=0.6,
                     far_delay=0.09, tone=1000, weight=0.9, sub=(0.22, 46, 1.3), roll=(0.34, 1.8, 850)),
    "cannon_1": dict(near="1917/B_24P.wav", far="1917/B_16P.wav", pitch=0.52, length=2.8, far_gain=0.6,
                     far_delay=0.08, tone=1050, weight=0.85, sub=(0.20, 50, 1.2), roll=(0.32, 1.8, 900)),
    "cannon_2": dict(near="Model 12/K_22P.wav", far="Model 12/K_17P.wav", pitch=0.48, length=3.0, far_gain=0.55,
                     far_delay=0.10, tone=950, weight=1.0, sub=(0.25, 42, 1.5), roll=(0.38, 2.0, 800)),
    # Energy weapons: the Kenney sound with a real muzzle crack under it.
    "bolt_0": dict(near="Ruger Mark III/R_35P.wav", far=None, pitch=0.8, length=0.7, gain=0.5,
                   kenney="sci-fi-sounds/laserLarge_000", kenney_gain=0.85),
    "bolt_1": dict(near="Smith & Wesson 642/V_27P.wav", far=None, pitch=0.85, length=0.7, gain=0.45,
                   kenney="sci-fi-sounds/laserLarge_002", kenney_gain=0.85),
    "pulse_0": dict(near="Ruger Mark III/R_30P.wav", far=None, pitch=1.25, length=0.55, gain=0.33,
                    kenney="sci-fi-sounds/laserRetro_000", kenney_gain=0.9),
    "pulse_1": dict(near="Walther PPQ/X_31P.wav", far=None, pitch=1.3, length=0.55, gain=0.3,
                    kenney="sci-fi-sounds/laserRetro_002", kenney_gain=0.9),
}

# Wood, cut out of the recordings: where each piece sits in its file (seconds).
FALL = os.path.join(SFX_SRC, "chop-tree-fall.ogg")      # chop, creaks, then the crash
CREAK = os.path.join(SFX_SRC, "tree_creak.ogg")
WOOD_SRC = os.path.join(SFX_SRC, "100-CC0-wood-metal-SFX")
BREAK_SRC = os.path.join(SFX_SRC, "sfx_breaking_and_falling")
WOOD = {
    # It starts to go: the trunk splitting, then the groan of it leaning over.
    "treefall_0": [(WOOD_SRC + "/wood_cracking_01.ogg", 0, 0.9, 0.0, 0.9), (CREAK, 0.40, 1.5, 0.06, 0.8), (FALL, 0.95, 0.6, 0.35, 0.7)],
    "treefall_1": [(BREAK_SRC + "/bfh1_wood_breaking_02.ogg", 0, 0.8, 0.0, 0.9), (CREAK, 3.2, 1.6, 0.05, 0.9), (FALL, 1.15, 0.45, 0.3, 0.7)],
    "treefall_2": [(WOOD_SRC + "/wood_cracking_03.ogg", 0, 0.9, 0.0, 0.9), (CREAK, 5.0, 1.4, 0.06, 0.85), (FALL, 1.3, 0.5, 0.3, 0.7)],
    # It lands: the trunk hitting the ground and the crown coming down after it.
    # The crash is mostly the crown coming down; the trunk's knock under it is a
    # heavy wood impact from Kenney's pack, or a falling tree is all leaves.
    "treecrash_0": [("kenney:impact-sounds/impactWood_heavy_000", 0, 0.7, 0.0, 1.4), (FALL, 1.58, 1.0, 0.0, 0.75, 4200),
                    (WOOD_SRC + "/wood_falling_03.ogg", 0, 0.5, 0.02, 0.5)],
    "treecrash_1": [("kenney:impact-sounds/impactWood_heavy_002", 0, 0.7, 0.0, 1.4), (FALL, 1.62, 0.95, 0.0, 0.7, 4200),
                    (BREAK_SRC + "/bfh1_wood_falling_01.ogg", 0, 0.6, 0.03, 0.5)],
    "treecrash_2": [("kenney:impact-sounds/impactWood_heavy_004", 0, 0.7, 0.0, 1.4), (FALL, 1.66, 0.9, 0.0, 0.7, 4200),
                    (WOOD_SRC + "/wood_breaking_02.ogg", 0, 0.5, 0.0, 0.45)],
    # A bush flattened under a Mauler: leaves and small stuff, no trunk.
    "crush_0": [(FALL, 1.95, 0.5, 0.0, 0.7), (WOOD_SRC + "/wood_cracking_02.ogg", 0, 0.4, 0.05, 0.35)],
    "crush_1": [(FALL, 2.05, 0.45, 0.0, 0.7), (BREAK_SRC + "/bfh1_wood_hit_02.ogg", 0, 0.4, 0.04, 0.3)],
}


def decode(path, sr=SR):
    """A recording as mono float, through macOS's afconvert (it reads ogg and wav)."""
    import subprocess
    import tempfile
    with tempfile.TemporaryDirectory() as d:
        wav = os.path.join(d, "t.wav")
        subprocess.run(["afconvert", "-f", "WAVE", "-d", f"LEI16@{sr}", "-c", "1", path, wav],
                       check=True, capture_output=True)
        b = open(wav, "rb").read()
        i = b.find(b"data")
        return np.frombuffer(b[i + 8:], dtype="<i2").astype(np.float64) / 32768.0


def cut(x, start, length, fade_in=0.0, gain=1.0):
    """A piece of a recording, with a little fade at each end so it never clicks."""
    a, b = int(start * SR), int((start + length) * SR)
    y = np.array(x[a:min(b, len(x))]) * gain
    n_in = max(1, int(fade_in * SR)) if fade_in > 0 else int(0.002 * SR)
    n_out = int(0.03 * SR)
    if len(y) > n_in + n_out:
        y[:n_in] *= np.linspace(0, 1, n_in)
        y[-n_out:] *= np.linspace(1, 0, n_out)
    return y


def onset(x, thresh=0.05, pre=0.008):
    """Drop the silence before a shot."""
    loud = np.abs(x) > thresh * np.max(np.abs(x))
    i = int(np.argmax(loud)) if loud.any() else 0
    return x[max(0, i - int(pre * SR)):]


def pitched(x, factor):
    """Resampled, so it plays slower (deeper) or faster: a .45 becomes a tank gun."""
    if abs(factor - 1.0) < 1e-3:
        return x
    n = int(len(x) / factor)
    t = np.arange(n) * factor
    i = np.clip(t.astype(int), 0, len(x) - 2)
    f = t - i
    return x[i] * (1 - f) + x[i + 1] * f


def add(base, x, at=0.0, gain=1.0):
    i = int(at * SR)
    if i + len(x) > len(base):
        base = np.concatenate([base, np.zeros(i + len(x) - len(base))])
    base[i:i + len(x)] += x * gain
    return base


def slapback(x, gain=0.12):
    """The report coming back off the ground and the trees behind: two soft
    reflections and a short diffuse tail. Open country, not a room."""
    out = np.array(x)
    for delay, g, fc in ((0.055, 0.5, 2600), (0.115, 0.32, 1500), (0.19, 0.18, 900)):
        echo = filt_lin(x, fc)
        out = add(out, echo, delay, gain * g * 4)
    n = int(0.35 * SR)
    noise = RNG.normal(size=n) * np.exp(-np.arange(n) / (0.09 * SR))
    tail = np.convolve(filt_lin(x, 2200), filt_lin(noise, 1800), mode="full")[:len(x) + n]
    m = np.max(np.abs(tail))
    if m > 0:
        out = add(out, tail / m * np.max(np.abs(x)), 0.03, gain)
    return out


def filt_lin(x, fc, order=2):
    """A plain low-pass on a one-shot (not circular: these do not loop)."""
    n = 1
    while n < len(x) * 2:
        n *= 2
    spec = np.fft.rfft(x, n)
    f = np.fft.rfftfreq(n, 1.0 / SR)
    return np.fft.irfft(spec / np.sqrt(1.0 + (f / fc) ** (2 * order)), n)[:len(x)]


def hipass_lin(x, fc, order=2):
    """A plain high-pass on a one-shot. Below about 45 Hz a laptop speaker moves
    no air at all, so energy down there is not weight -- it is headroom spent on
    something nobody can hear, and match() then turns the audible part down to
    make room for it."""
    n = 1
    while n < len(x) * 2:
        n *= 2
    spec = np.fft.rfft(x, n)
    f = np.fft.rfftfreq(n, 1.0 / SR)
    return np.fft.irfft(spec / np.sqrt(1.0 + (fc / np.maximum(f, 1e-3)) ** (2 * order)), n)[:len(x)]


def match(x, name):
    """Every take of a sound at one loudness, so a volley does not lurch about.
    Peaks are rounded off rather than the whole take turned down, or one shot with
    a sharper crack than the rest would play quieter than all of them."""
    target = RMS[name.rsplit("_", 1)[0]]
    rms = float(np.sqrt(np.mean(x * x)))
    if rms > 0:
        x = x * (target / rms)
    return np.tanh(x / 0.7) * 0.7


def build_weapon(spec):
    near = pitched(onset(decode(os.path.join(GUNS, spec["near"]))), spec["pitch"])
    out = near * spec.get("gain", 1.0)
    if spec.get("far"):
        far = pitched(onset(decode(os.path.join(GUNS, spec["far"]))), spec["pitch"])
        out = add(out, far, spec.get("far_delay", 0.045), spec.get("far_gain", 0.55))
    if spec.get("kenney"):
        pack, name = spec["kenney"].split("/")
        laser = decode(os.path.join(KENNEY, f"kenney_{pack}", "Audio", name + ".ogg"))
        out = add(out, laser, 0.0, spec.get("kenney_gain", 0.8))
    out = slapback(out, 0.10)
    if spec.get("tone"):
        # A gun this size is heard as its low end from across the map.
        out = filt_lin(out, spec["tone"], 1)
    if spec.get("weight"):
        out = out + filt_lin(out, 180, 2) * spec["weight"]
    if spec.get("sub"):
        # The thump under the crack: a short sine that falls in pitch, which is
        # what a big gun is felt as rather than heard as. Without it the shot is
        # just a loud bang, and a loud bang is not frightening.
        gain, f0, decay = spec["sub"]
        out = add(out, thump(f0, decay), 0.006, gain)
    if spec.get("roll"):
        # And the report rolling away over the ground for a second or two after
        # it: the near and far takes end long before a gun this size would.
        gain, seconds, fc = spec["roll"]
        out = add(out, rolling(out, seconds, fc), 0.12, gain)
    out = hipass_lin(out[:int(spec["length"] * SR)], 45, 2)
    n_out = int(0.04 * SR)
    out[-n_out:] *= np.linspace(1, 0, n_out)
    return out


def thump(f0, decay, f1=None, seconds=None):
    """A sine falling from f0 to f1 over an exponential decay: the body of a
    heavy gun or a burst, the part you feel."""
    f1 = f0 * 0.45 if f1 is None else f1
    seconds = decay * 1.6 if seconds is None else seconds
    n = int(seconds * SR)
    t = np.arange(n) / SR
    k = np.exp(-t / (decay * 0.42))
    f = f1 + (f0 - f1) * k
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * k


def rolling(x, seconds, fc=420):
    """The report rolling away over open country: the sound smeared through a
    long, dark, decaying noise tail. Slapback gives the first reflections; this
    is the rumble that goes on after them."""
    n = int(seconds * SR)
    tail = RNG.normal(size=n) * np.exp(-np.arange(n) / (seconds * 0.30 * SR))
    out = np.convolve(filt_lin(x, fc, 2), filt_lin(tail, fc, 2), mode="full")[:len(x) + n]
    m = np.max(np.abs(out))
    return out / m * np.max(np.abs(x)) if m > 0 else out


def build_wood(parts):
    files = {}
    out = np.zeros(1)
    for path, start, length, at, gain, *rest in parts:
        if path not in files:
            src = path
            if path.startswith("kenney:"):
                pack, name = path[len("kenney:"):].split("/")
                src = os.path.join(KENNEY, f"kenney_{pack}", "Audio", name + ".ogg")
            files[path] = decode(src)
        piece = cut(files[path], start, length, 0.004, gain)
        if rest:
            piece = filt_lin(piece, rest[0], 1)     # the leaf rush, taken off the top
        out = add(out, piece, at)
    return slapback(out, 0.07)


# ---------------------------------------------------------------- blasts and engines
# Shells landing and things blowing up. Kenney's explosion crunches carry the
# detail; what they have no low end for is the thump you feel and the rumble
# rolling away after it, which is where the weight of a burst actually lives.
BLASTS = {
    "blast_0": dict(crunch="explosionCrunch_000", pitch=0.80, sub=(0.28, 52, 1.4), roll=(0.38, 2.0, 950), length=3.2),
    "blast_1": dict(crunch="explosionCrunch_002", pitch=0.66, sub=(0.36, 48, 1.5), roll=(0.42, 2.0, 950), length=3.2),
    "blast_2": dict(crunch="explosionCrunch_003", pitch=0.84, sub=(0.30, 56, 1.3), roll=(0.36, 1.9, 1000), length=3.0),
    # A structure going up: deeper, slower, and it goes on rolling much longer.
    "blastbig_0": dict(crunch="explosionCrunch_004", pitch=0.78, sub=(0.22, 40, 2.4), roll=(0.45, 2.8, 750), length=5.0,
                       low="lowFrequency_explosion_000", low_gain=0.18, low_pitch=0.95),
    "blastbig_1": dict(crunch="explosionCrunch_001", pitch=0.56, sub=(0.34, 34, 2.4), roll=(0.45, 3.0, 700), length=5.2,
                       low="lowFrequency_explosion_001", low_gain=0.7, low_pitch=0.65),
}


def kenney_clip(pack, name):
    return decode(os.path.join(KENNEY, f"kenney_{pack}", "Audio", name + ".ogg"))


def build_blast(spec):
    # Aim the weight at 80-400 Hz, not at 40. A laptop speaker cannot move enough
    # air to reproduce sub-bass at all, so energy put down there is not felt --
    # it is simply lost, and the blast comes out quiet.

    out = pitched(onset(kenney_clip("sci-fi-sounds", spec["crunch"])), spec["pitch"])
    if spec.get("low"):
        out = add(out, pitched(onset(kenney_clip("sci-fi-sounds", spec["low"])), spec["low_pitch"]),
                  0.01, spec["low_gain"])
    out = slapback(out, 0.12)
    gain, f0, decay = spec["sub"]
    out = add(out, thump(f0, decay), 0.004, gain)
    gain, seconds, fc = spec["roll"]
    out = add(out, rolling(out, seconds, fc), 0.10, gain)
    out = hipass_lin(out[:int(spec["length"] * SR)], 45, 2)
    n_out = int(0.12 * SR)
    out[-n_out:] *= np.linspace(1, 0, n_out)
    return out


def make_blasts():
    out = os.path.join(AUDIO, "Sfx")
    for name, spec in BLASTS.items():
        write_wav(os.path.join(out, name + ".wav"), match(build_blast(spec), name))
    print(f"{len(BLASTS)} blast sounds -> {os.path.relpath(out, ROOT)}")


def seamless(x, n):
    """A one-shot tiled to exactly n samples with its own tail crossfaded over its
    head, so the loop has no seam."""
    x = np.asarray(x, dtype=np.float64)
    if len(x) < n:
        x = np.tile(x, int(np.ceil(n / len(x))) + 1)
    head = x[:n]
    fade = min(int(0.25 * SR), n // 4)
    over = x[n:n + fade]
    if len(over) == fade and fade > 0:
        w = np.linspace(0, 1, fade)
        head = head.copy()
        head[:fade] = head[:fade] * w + over * (1 - w)
    return head


def make_engine(seconds=6.0):
    """The Mauler's engine: Kenney's low space engine dropped an octave for the
    body, a diesel beat under it at a rate that divides the loop exactly, and a
    broad rumble. A sci-fi hum on its own has no bottom and no pulse, and a tank
    that sounds like a fridge is not frightening."""
    n = int(seconds * SR)
    body = seamless(pitched(kenney_clip("sci-fi-sounds", "spaceEngineLow_001"), 0.55), n)
    body = body / max(1e-9, np.std(body))
    # Cylinders firing: a beat whose period divides the loop, so it does not click
    # at the wrap. Each beat is a short thump, not a click.
    beats = 60
    out = np.zeros(n)
    step = n / beats
    for b in range(beats):
        # A diesel knock is harsh, not a clean sine: soft-clipped, so it carries
        # harmonics a laptop speaker can actually play.
        k = np.tanh(thump(92 + 8 * math.sin(b * 0.7), 0.05, 56, 0.10) * 2.2) / np.tanh(2.2)
        i = int(b * step + (b % 3) * 90)
        take = min(len(k), n - i)
        if take > 0:
            out[i:i + take] += k[:take] * (0.9 if b % 2 else 1.0)
        if take < len(k):                      # wrap the tail round the loop
            rest = k[take:][:n]
            out[:len(rest)] += rest
    rumble = shaped_noise(n, lowpass(110, 3))
    rumble = rumble / np.std(rumble)
    growl = shaped_noise(n, bandpass(140, 520, 2))
    growl = growl / np.std(growl) * (0.55 + 0.45 * smooth_random(n, 7, lo=0.2, hi=1.0))
    mix = body * 0.30 + out * 0.80 + rumble * 0.30 + growl * 0.38
    # Nothing under 45 Hz: a laptop speaker cannot move it, and the first version
    # of this bed was 87% below 120 Hz -- most of the engine was inaudible.
    return normalize(filt(mix, lambda f: lowpass(2200, 2)(f) * highpass(45, 2)(f)), 0.85)


def make_tracks(seconds=4.0):
    """Track clatter: the cleats and the road wheels, built from the CC0 metal
    hits. It is the giveaway that a thing is tracked rather than wheeled, and
    the only part of a tank you hear before you see it."""
    n = int(seconds * SR)
    hits = [decode(os.path.join(WOOD_SRC, f"metal_hit_0{i}.ogg")) for i in (1, 2, 3, 4, 5)]
    sheet = decode(os.path.join(WOOD_SRC, "metal_sheet_02.ogg"))
    out = np.zeros(n)
    rng = np.random.default_rng(31337)
    t = 0.0
    while t < seconds:
        h = pitched(onset(hits[rng.integers(0, len(hits))]), rng.uniform(0.42, 0.62))
        h = filt_lin(h, rng.uniform(900, 2000), 2)[:int(0.28 * SR)]
        i = int(t * SR)
        take = min(len(h), n - i)
        g = rng.uniform(0.25, 0.7)
        out[i:i + take] += h[:take] * g
        if take < len(h):                      # wrap, so the loop has no gap
            rest = h[take:][:n]
            out[:len(rest)] += rest * g
        t += rng.uniform(0.055, 0.10)
    # A sheet of metal groaning under the weight, well down in the mix.
    body = seamless(pitched(sheet, 0.4), n)
    out += body / max(1e-9, np.std(body)) * 0.10
    return normalize(filt(out, bandpass(70, 3200, 2)), 0.8)


def make_weapons():
    out = os.path.join(AUDIO, "Sfx")
    os.makedirs(out, exist_ok=True)
    if not os.path.isdir(GUNS):
        sys.exit(f"missing {GUNS} (run Tools/fetch_assets.py)")
    for name, spec in WEAPONS.items():
        write_wav(os.path.join(out, name + ".wav"), match(build_weapon(spec), name))
    for name, parts in WOOD.items():
        write_wav(os.path.join(out, name + ".wav"), match(build_wood(parts), name))
    print(f"{len(WEAPONS)} weapon sounds and {len(WOOD)} wood sounds -> {os.path.relpath(out, ROOT)}")
    make_blasts()
    write_wav(os.path.join(out, "engine_heavy.wav"), make_engine())
    write_wav(os.path.join(out, "engine_tracks.wav"), make_tracks())
    print(f"engine and track beds -> {os.path.relpath(out, ROOT)}")


# ---------------------------------------------------------------- helpers
def write_wav(path, data):
    """data: (n,) mono or (n, 2) stereo float in -1..1."""
    data = np.asarray(data, dtype=np.float64)
    if data.ndim == 1:
        data = data[:, None]
    ch = data.shape[1]
    pcm = (np.clip(data, -1, 1) * 32767).astype("<i2").tobytes()
    with open(path, "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", 36 + len(pcm)) + b"WAVE")
        f.write(b"fmt " + struct.pack("<IHHIIHH", 16, 1, ch, SR, SR * ch * 2, ch * 2, 16))
        f.write(b"data" + struct.pack("<I", len(pcm)) + pcm)


def normalize(x, peak=0.9):
    m = np.max(np.abs(x))
    return x * (peak / m) if m > 0 else x


def freqs(n):
    return np.fft.rfftfreq(n, 1.0 / SR)


def shaped_noise(n, response, rng=RNG):
    """Circular noise of length n with magnitude `response(f)`."""
    spec = np.fft.rfft(rng.normal(size=n))
    return np.fft.irfft(spec * response(freqs(n)), n)


def lowpass(fc, order=2):
    return lambda f: 1.0 / np.sqrt(1.0 + (f / fc) ** (2 * order))


def highpass(fc, order=2):
    return lambda f: 1.0 / np.sqrt(1.0 + (fc / np.maximum(f, 1e-3)) ** (2 * order))


def bandpass(lo, hi, order=2):
    return lambda f: lowpass(hi, order)(f) * highpass(lo, order)(f)


def filt(x, response):
    """Circular zero-phase filtering (the result loops if x does)."""
    n = len(x)
    return np.fft.irfft(np.fft.rfft(x) * response(freqs(n)), n)


def smooth_random(n, points, rng=RNG, lo=0.0, hi=1.0):
    """A periodic, smooth random curve through `points` random values."""
    v = rng.uniform(lo, hi, points)
    t = np.arange(n) / n * points
    i = t.astype(int)
    f = t - i
    f = f * f * (3 - 2 * f)
    return v[i % points] * (1 - f) + v[(i + 1) % points] * f


# ---------------------------------------------------------------- ambience
def make_wind(seconds=24.0):
    n = int(seconds * SR)
    rumble = shaped_noise(n, lowpass(140, 3))
    whoosh = shaped_noise(n, bandpass(260, 900, 2))
    whistle = shaped_noise(n, bandpass(1300, 1700, 4))
    gust = smooth_random(n, 9, lo=0.15, hi=1.0) ** 1.8
    gust2 = smooth_random(n, 5, lo=0.3, hi=1.0)
    mono = rumble / np.std(rumble) * 0.5 * (0.6 + 0.4 * gust2) \
        + whoosh / np.std(whoosh) * 0.45 * gust \
        + whistle / np.std(whistle) * 0.05 * gust ** 2
    # Slight stereo drift: the two channels gust a little apart.
    side = shaped_noise(n, bandpass(260, 900, 2))
    side = side / np.std(side) * 0.15 * smooth_random(n, 7, lo=0.0, hi=1.0)
    return normalize(np.stack([mono + side, mono - side], 1), 0.8)


def make_water(seconds=14.0):
    n = int(seconds * SR)
    body = shaped_noise(n, bandpass(90, 700, 2))
    trickle = shaped_noise(n, bandpass(1800, 5200, 2))
    # Laps: soft swells every one to three seconds, each washing out and back.
    env = np.zeros(n)
    t = 0.0
    while t < seconds:
        c = int(t * SR)
        w = RNG.uniform(0.6, 1.4)
        k = np.arange(-int(w * SR), int(w * SR * 1.6))
        idx = (c + k) % n
        shape = np.where(k < 0, np.exp(-((k / (w * SR * 0.45)) ** 2)), np.exp(-k / (w * SR * 0.55)))
        env[idx] += shape * RNG.uniform(0.5, 1.0)
        t += RNG.uniform(1.0, 2.6)
    env = env / env.max()
    flick = smooth_random(n, int(seconds * 18), lo=0.0, hi=1.0) ** 3
    mono = body / np.std(body) * (0.25 + 0.75 * env) * 0.55 + trickle / np.std(trickle) * env * flick * 0.18
    side = filt(RNG.normal(size=n), bandpass(1500, 5000)) * env * 0.04
    return normalize(np.stack([mono + side, mono - side], 1), 0.8)


def make_fire(seconds=10.0):
    n = int(seconds * SR)
    roar = shaped_noise(n, lowpass(220, 2))
    roar = roar / np.std(roar) * (0.6 + 0.4 * smooth_random(n, 23, lo=0.2, hi=1.0))
    hiss = shaped_noise(n, bandpass(2000, 7000))
    hiss = hiss / np.std(hiss) * 0.08
    crackle = np.zeros(n)
    count = int(seconds * 22)
    for _ in range(count):
        c = RNG.integers(0, n)
        length = int(RNG.uniform(0.002, 0.012) * SR)
        burst = RNG.normal(size=length) * np.exp(-np.arange(length) / (length * 0.3))
        amp = RNG.uniform(0.2, 1.0) ** 2 * (3.0 if RNG.random() < 0.06 else 1.0)
        idx = (c + np.arange(length)) % n
        crackle[idx] += burst * amp
    crackle = filt(crackle, highpass(900, 1))
    mono = roar * 0.5 + hiss + crackle / np.max(np.abs(crackle)) * 0.9
    left = mono + filt(crackle, lowpass(4000)) * 0.1
    return normalize(np.stack([left, mono], 1), 0.85)


def make_bird(seed):
    """A short song: a pattern of chirps -- slides, trills and warbles."""
    rng = np.random.default_rng(seed)
    parts = []
    kind = seed % 3
    base = rng.uniform(2600, 4200)
    for k in range(rng.integers(3, 8) if kind != 1 else rng.integers(8, 14)):
        dur = rng.uniform(0.05, 0.14) if kind != 1 else rng.uniform(0.03, 0.05)
        m = int(dur * SR)
        t = np.arange(m) / SR
        if kind == 0:     # falling slides
            f = base * (1.25 - 0.4 * (t / dur)) * rng.uniform(0.9, 1.1)
        elif kind == 1:   # fast trill
            f = base * (1.0 + 0.15 * np.sin(t / dur * math.pi)) * (1.1 if k % 2 else 0.95)
        else:             # warble
            f = base * (1.0 + 0.12 * np.sin(2 * math.pi * rng.uniform(30, 55) * t)) * rng.uniform(0.85, 1.2)
        phase = 2 * math.pi * np.cumsum(f) / SR
        env = np.sin(np.clip(t / dur, 0, 1) * math.pi) ** 1.5
        tone = (np.sin(phase) + 0.18 * np.sin(2 * phase)) * env
        parts.append(tone * rng.uniform(0.6, 1.0))
        parts.append(np.zeros(int(rng.uniform(0.02, 0.09 if kind == 1 else 0.16) * SR)))
    song = np.concatenate(parts + [np.zeros(int(0.15 * SR))])
    return normalize(song, 0.8)


# ---------------------------------------------------------------- music
MUSIC_SRC = os.path.join(ROOT, "Art", "Source", "music")
# game name: source file (see fetch_assets.py for authors and pages).
MUSIC = {
    "music_calm_0.ogg": "cinematic-calm.wav",
    "music_calm_1.ogg": "first_light_particles_0.wav",
    "music_calm_2.mp3": "Contemplation.mp3",
    "music_tension_0.ogg": "Insistent.ogg",
    "music_combat_0.mp3": "battleThemeA.mp3",
}
BLENDER = "/Applications/Blender.app/Contents/MacOS/Blender"


def loudness(path):
    """RMS of a compressed track, decoded with macOS's afconvert."""
    import subprocess
    import tempfile
    with tempfile.TemporaryDirectory() as d:
        wav = os.path.join(d, "t.wav")
        subprocess.run(["afconvert", "-f", "WAVE", "-d", "LEI16@22050", "-c", "1", path, wav], check=True)
        b = open(wav, "rb").read()
        i = b.find(b"data")
        x = np.frombuffer(b[i + 8:], dtype="<i2").astype(np.float64) / 32768.0
        return float(np.sqrt(np.mean(x * x))), len(x) / 22050.0


def encode_ogg(src, dst, kbps=112):
    """Vorbis-encode a WAV source. macOS cannot: afconvert writes AAC and ALAC,
    so Blender's audio library does it (Tools/blender/encode_audio.py)."""
    import subprocess
    if not os.path.exists(BLENDER):
        sys.exit(f"{src} needs encoding to Ogg and {BLENDER} is not there")
    subprocess.run([BLENDER, "--background", "--factory-startup", "--python",
                    os.path.join(ROOT, "Tools", "blender", "encode_audio.py"),
                    "--", src, dst, str(kbps)], check=True, capture_output=True)


def copy_music():
    import json
    out = os.path.join(AUDIO, "Music")
    os.makedirs(out, exist_ok=True)
    for f in os.listdir(out):
        if f.startswith("music_") and not f.endswith(".meta"):
            os.remove(os.path.join(out, f))
            if os.path.exists(os.path.join(out, f + ".meta")):
                os.remove(os.path.join(out, f + ".meta"))
    info = {}
    for name, src in MUSIC.items():
        path = os.path.join(MUSIC_SRC, src)
        if not os.path.exists(path):
            sys.exit(f"missing {path} (run Tools/fetch_assets.py)")
        dst = os.path.join(out, name)
        if src.lower().endswith(".wav") and name.endswith(".ogg"):
            encode_ogg(path, dst)
        else:
            shutil.copyfile(path, dst)
        rms, seconds = loudness(dst)
        info[os.path.splitext(name)[0]] = {"rms": round(rms, 4), "seconds": round(seconds, 1), "source": src}
        print(f"music {name:22s} {seconds:6.1f} s  rms {rms:.3f}")
    with open(os.path.join(out, "music.json"), "w") as f:
        json.dump(info, f, indent=2)


def main():
    copy_sfx()
    make_weapons()
    amb = os.path.join(AUDIO, "Ambience")
    os.makedirs(amb, exist_ok=True)
    write_wav(os.path.join(amb, "wind.wav"), make_wind())
    write_wav(os.path.join(amb, "water.wav"), make_water())
    write_wav(os.path.join(amb, "fire.wav"), make_fire())
    for i in range(6):
        write_wav(os.path.join(amb, f"bird_{i}.wav"), make_bird(100 + i))
    print("ambience ->", os.path.relpath(amb, ROOT))
    copy_music()


if __name__ == "__main__":
    main()
