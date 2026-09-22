#!/usr/bin/env python3
"""Download the third-party source art StarForge is built from.

    python3 Tools/fetch_assets.py

Textures and rock scans come from Poly Haven (https://polyhaven.com), leaf
atlases from ambientCG (https://ambientcg.com), sound effects from Kenney
(https://kenney.nl), and from OpenGameArt (https://opengameart.org) the music,
the animated animals (by Quaternius) and the recorded gunshots, tree falls and
wood breaks the weapons and felled trees are built from; all CC0: free to use, modify and
redistribute, no attribution required (credited in README anyway).
Files land in Art/Source/polyhaven/<id>/ outside Assets/, so Unity never imports
the raw scans; Tools/blender/pack_textures.py and build_rocks.py turn them
into what the game uses, and those outputs are what the repository keeps.
Art/Source/ itself is ignored by git: this script fetches it again, checking
every file against the md5 Poly Haven publishes, and skips files already there.

Art/Source/polyhaven/manifest.json records, per asset, its authors, licence and
real-world size (texture dimensions in metres), which the packers read.
"""
import hashlib
import json
import os
import sys
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Art", "Source", "polyhaven")
API = "https://api.polyhaven.com"

# Ground textures: colour, OpenGL normal, displacement (height, for blending
# the layers) and roughness, at 1k. At the closest zoom the camera sees about
# 65 texels per metre of ground, so 1k over a 3-7 m tile is already enough.
TEXTURES = {
    "coast_sand_rocks_02": "meadow: earth, moss and small stones (the layer grass grows on)",
    "forest_ground_04": "main ground: grey-brown stony dirt",
    "aerial_rocks_02": "cliffs: grey rock slabs with moss in the cracks",
    "coast_sand_01": "shores and dry ridges: grey-beige sand",
    "rocky_trail": "gravel (spare)",
    "cliff_side": "stratified cliff (spare)",
}
TEXTURE_MAPS = ["Diffuse", "nor_gl", "Displacement", "Rough"]

# Scanned rocks, as glTF with 1k textures; build_rocks.py decimates them.
MODELS = {
    "rock_moss_set_02": "boulders: grey, mossy",
    "rock_moss_set_01": "boulders: warmer, lichen",
    "boulder_01": "large outcrop",
}


# Kenney's CC0 sound packs (https://kenney.nl), unpacked into Art/Source/kenney/;
# Tools/make_audio.py picks the effects it uses from them.
KENNEY = {
    "sci-fi-sounds": "https://kenney.nl/media/pages/assets/sci-fi-sounds/6b296f9ecf-1677589334/kenney_sci-fi-sounds.zip",
    "impact-sounds": "https://kenney.nl/media/pages/assets/impact-sounds/87b4ddecda-1677589768/kenney_impact-sounds.zip",
    "interface-sounds": "https://kenney.nl/media/pages/assets/interface-sounds/fa43c1dd4d-1677589452/kenney_interface-sounds.zip",
}


# Music from OpenGameArt, all CC0: file name: (page, author, title).
# The calm set is deliberately warm and major-key: the minor piano loops that
# used to sit here (Kistol's Snowfall, pauliuw's The Field Of Dreams) sounded
# unsettling under a quiet base.
MUSIC = {
    "cinematic-calm.wav": ("at-home-orchestral", "wolfgang", "At Home (orchestral)"),
    "first_light_particles_0.wav": ("first-light-particles-%E2%80%93-cc0-atmospheric-pianoambient-track",
                                    "yoiyami", "First Light Particles"),
    "Contemplation.mp3": ("contemplation-0", "Joth", "Contemplation"),
    "Insistent.ogg": ("insistent-background-loop", "yd", "Insistent: background loop"),
    "battleThemeA.mp3": ("battle-theme-a", "cynicmusic", "Battle Theme A"),
}

# Leaf atlases from ambientCG (CC0), which Tools/blender/make_leaf_cards.py cuts
# the crowns' leaf sprays out of: oak, birch, beech and spruce sprigs.
AMBIENTCG = ["LeafSet016", "LeafSet014", "LeafSet024", "LeafSet019"]


# Recorded sound from OpenGameArt, all CC0: the Free Firearm Sound Library (Ben
# Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney; every gun recorded from
# beside the shooter and again at a distance), a tree being chopped down
# (kheetor), a tree creaking (AntumDeluge) and rubberduck's wood and breaking
# packs. Tools/make_audio.py builds the game's guns and tree falls from them.
SFX = {
    "Prepared%20SFX%20Library.7z": "firearms.7z",
    "sfx_breaking_and_falling.zip": "sfx_breaking_and_falling.zip",
    "100-CC0-wood-metal-SFX.zip": "100-CC0-wood-metal-SFX.zip",
    "chop-tree-fall.ogg": "chop-tree-fall.ogg",
    "tree_creak.ogg": "tree_creak.ogg",
}

# Animated low-poly animals by Quaternius (CC0), from OpenGameArt.
QUATERNIUS = {
    "Animals_Pack_by_Quaternius": "https://opengameart.org/sites/default/files/Animals%20Pack%20by%20Quaternius.zip",
    "Animal_Pack_Vol.2_by_Quaternius": "https://opengameart.org/sites/default/files/Animal%20Pack%20Vol.2%20by%20%40Quaternius.zip",
}


def fetch_ambientcg():
    import zipfile
    out = os.path.join(ROOT, "Art", "Source", "ambientcg")
    os.makedirs(out, exist_ok=True)
    for name in AMBIENTCG:
        folder = os.path.join(out, name)
        if os.path.isdir(folder):
            continue
        zpath = folder + "_1K-JPG.zip"
        req = urllib.request.Request(f"https://ambientcg.com/get?file={name}_1K-JPG.zip",
                                     headers={"User-Agent": "StarForge-fetch/1.0"})
        with urllib.request.urlopen(req, timeout=600) as r, open(zpath, "wb") as f:
            f.write(r.read())
        os.makedirs(folder, exist_ok=True)
        with zipfile.ZipFile(zpath) as z:
            z.extractall(folder)
        print(f"  ambientcg {name}  {os.path.getsize(zpath) / 1e6:.1f} MB")


def fetch_sfx():
    """The recorded sound the weapons and the falling trees are built from."""
    import subprocess
    import zipfile
    out = os.path.join(ROOT, "Art", "Source", "sfx")
    os.makedirs(out, exist_ok=True)
    for src, name in SFX.items():
        path = os.path.join(out, name)
        folder = os.path.join(out, os.path.splitext(name)[0])
        if os.path.exists(path) or os.path.isdir(folder):
            continue
        req = urllib.request.Request("https://opengameart.org/sites/default/files/" + src,
                                     headers={"User-Agent": "StarForge-fetch/1.0"})
        with urllib.request.urlopen(req, timeout=900) as r, open(path, "wb") as f:
            f.write(r.read())
        print(f"  sfx {name}  {os.path.getsize(path) / 1e6:.1f} MB")
        if name.endswith(".zip"):
            os.makedirs(folder, exist_ok=True)
            with zipfile.ZipFile(path) as z:
                z.extractall(folder)
        elif name.endswith(".7z"):
            # No 7z in the standard library; macOS's tar (libarchive) reads it.
            os.makedirs(folder, exist_ok=True)
            subprocess.run(["tar", "-xf", path, "-C", folder], check=True)


def fetch_plain():
    import urllib.parse
    import zipfile
    music = os.path.join(ROOT, "Art", "Source", "music")
    os.makedirs(music, exist_ok=True)
    for f, (page, author, title) in MUSIC.items():
        path = os.path.join(music, urllib.parse.unquote(f))
        if os.path.exists(path):
            continue
        req = urllib.request.Request("https://opengameart.org/sites/default/files/" + f, headers={"User-Agent": "StarForge-fetch/1.0"})
        with urllib.request.urlopen(req, timeout=300) as r, open(path, "wb") as out:
            out.write(r.read())
        print(f"  music {title} by {author}  {os.path.getsize(path) / 1e6:.1f} MB")
    animals = os.path.join(ROOT, "Art", "Source", "quaternius")
    os.makedirs(animals, exist_ok=True)
    for name, url in QUATERNIUS.items():
        folder = os.path.join(animals, name)
        if os.path.isdir(folder):
            continue
        zpath = folder + ".zip"
        req = urllib.request.Request(url, headers={"User-Agent": "StarForge-fetch/1.0"})
        with urllib.request.urlopen(req, timeout=300) as r, open(zpath, "wb") as out:
            out.write(r.read())
        with zipfile.ZipFile(zpath) as z:
            z.extractall(folder)
        print(f"  quaternius {name}  {os.path.getsize(zpath) / 1e6:.1f} MB")


def fetch_kenney():
    import zipfile
    out = os.path.join(ROOT, "Art", "Source", "kenney")
    os.makedirs(out, exist_ok=True)
    for name, url in KENNEY.items():
        folder = os.path.join(out, f"kenney_{name}")
        if os.path.isdir(folder):
            continue
        zpath = folder + ".zip"
        req = urllib.request.Request(url, headers={"User-Agent": "StarForge-fetch/1.0"})
        with urllib.request.urlopen(req, timeout=300) as r, open(zpath, "wb") as f:
            f.write(r.read())
        with zipfile.ZipFile(zpath) as z:
            z.extractall(folder)
        print(f"  kenney {name}  {os.path.getsize(zpath) / 1e6:.1f} MB")


def get_json(url):
    req = urllib.request.Request(url, headers={"User-Agent": "StarForge-fetch/1.0"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


def md5(path):
    h = hashlib.md5()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def fetch(url, path, want_md5, size):
    if os.path.exists(path) and md5(path) == want_md5:
        return False
    os.makedirs(os.path.dirname(path), exist_ok=True)
    req = urllib.request.Request(url, headers={"User-Agent": "StarForge-fetch/1.0"})
    with urllib.request.urlopen(req, timeout=300) as r, open(path + ".part", "wb") as f:
        f.write(r.read())
    got = md5(path + ".part")
    if got != want_md5:
        os.remove(path + ".part")
        raise RuntimeError(f"{url}: md5 {got}, expected {want_md5}")
    os.replace(path + ".part", path)
    print(f"  {os.path.relpath(path, ROOT)}  {size / 1e6:.1f} MB")
    return True


def main():
    manifest = {}
    total = 0
    for aid, role in {**TEXTURES, **MODELS}.items():
        info = get_json(f"{API}/info/{aid}")
        files = get_json(f"{API}/files/{aid}")
        entry = {
            "name": info.get("name", aid),
            "role": role,
            "type": "texture" if aid in TEXTURES else "model",
            "authors": sorted(info.get("authors", {}).keys()),
            "license": "CC0 1.0",
            "source": f"https://polyhaven.com/a/{aid}",
            "files": {},
        }
        if "dimensions" in info:
            entry["dimensions_m"] = [round(d / 1000.0, 3) for d in info["dimensions"]]
        print(aid)
        if aid in TEXTURES:
            for m in TEXTURE_MAPS:
                fmt = "png" if m == "Displacement" else "jpg"   # 8-bit JPEG bands the height
                f = files[m]["1k"][fmt]
                name = os.path.basename(f["url"])
                fetch(f["url"], os.path.join(OUT, aid, name), f["md5"], f["size"])
                entry["files"][m] = name
                total += f["size"]
        else:
            g = files["gltf"]["1k"]["gltf"]
            name = os.path.basename(g["url"])
            fetch(g["url"], os.path.join(OUT, aid, name), g["md5"], g["size"])
            entry["files"]["gltf"] = name
            total += g["size"]
            for rel, f in g["include"].items():
                fetch(f["url"], os.path.join(OUT, aid, rel), f["md5"], f["size"])
                total += f["size"]
        manifest[aid] = entry
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "manifest.json"), "w") as f:
        json.dump(manifest, f, indent=2)
    print(f"{len(manifest)} assets, {total / 1e6:.1f} MB, in {os.path.relpath(OUT, ROOT)}")
    fetch_kenney()
    fetch_ambientcg()
    fetch_sfx()
    fetch_plain()


if __name__ == "__main__":
    sys.exit(main())
