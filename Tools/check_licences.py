#!/usr/bin/env python3
"""Check that every third-party asset in the repository is one we may ship.

    python3 Tools/check_licences.py             # re-checks the live sources
    python3 Tools/check_licences.py --offline   # structure only, no network

The repository is public, so an asset is only usable here if its licence allows
**redistribution**: CC0 for the art and the audio, SIL OFL 1.1 and Apache 2.0
for the two font families. THIRD-PARTY.md is the register; this script keeps it
honest, and answers four questions:

1. Does each source still state the licence the register claims? (network)
2. Does every file the register points at exist?
3. Is everything Tools/fetch_assets.py downloads registered?
4. **Is every image, model, font and sound under Assets/ accounted for** -- by
   the register, or by the list of what we make ourselves? This is the check
   that catches an asset dropped in without anyone recording where it came from.

Exit status is 1 if anything failed.
"""
import argparse
import fnmatch
import glob
import html
import json
import os
import re
import struct
import sys
import urllib.request

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UA = {"User-Agent": "StarForge-licence-check/1.0"}
CC0 = "CC0 1.0"


def S(name, authors, licence, url, check, files, ids=(), note=""):
    """One source. `ids` are the names Tools/fetch_assets.py downloads it under,
    so the two lists can be checked against each other."""
    return dict(name=name, authors=authors, licence=licence, url=url, check=check,
                files=files, ids=list(ids), note=note)


T = "Assets/StarForge/Art/Textures/"
M = "Assets/StarForge/Art/Models/"
A = "Assets/StarForge/Audio/"

# ---------------------------------------------------------------- the register
SOURCES = [
    # Poly Haven -- scanned ground and rock (CC0, https://polyhaven.com/license)
    S("Coast Sand Rocks 02", "Rob Tuytel", CC0, "https://polyhaven.com/a/coast_sand_rocks_02",
      ("polyhaven", "coast_sand_rocks_02"), [T + "Terrain/meadow_*"], ["coast_sand_rocks_02"]),
    S("Forest Ground 04", "Rico Cilliers, Rob Tuytel", CC0, "https://polyhaven.com/a/forest_ground_04",
      ("polyhaven", "forest_ground_04"), [T + "Terrain/dirt_*"], ["forest_ground_04"]),
    S("Aerial Rocks 02", "Rob Tuytel", CC0, "https://polyhaven.com/a/aerial_rocks_02",
      ("polyhaven", "aerial_rocks_02"), [T + "Terrain/cliff_*"], ["aerial_rocks_02"]),
    S("Coast Sand 01", "Rob Tuytel", CC0, "https://polyhaven.com/a/coast_sand_01",
      ("polyhaven", "coast_sand_01"), [T + "Terrain/sand_*"], ["coast_sand_01"]),
    S("Rock Moss Set 01", "Kless Gyzen", CC0, "https://polyhaven.com/a/rock_moss_set_01",
      ("polyhaven", "rock_moss_set_01"),
      [T + "Rocks/rock_moss_set_01_*", M + "SF_SCAN_BOULDER_E.fbx", M + "SF_SCAN_BOULDER_F.fbx",
       M + "SF_SCAN_CRAG_A.fbx", M + "SF_SCAN_SHELF_A.fbx"], ["rock_moss_set_01"]),
    S("Rock Moss Set 02", "Kless Gyzen", CC0, "https://polyhaven.com/a/rock_moss_set_02",
      ("polyhaven", "rock_moss_set_02"),
      [T + "Rocks/rock_moss_set_02_*", M + "SF_SCAN_BOULDER_A.fbx", M + "SF_SCAN_BOULDER_B.fbx",
       M + "SF_SCAN_BOULDER_C.fbx", M + "SF_SCAN_BOULDER_D.fbx"], ["rock_moss_set_02"]),
    S("Boulder 01", "Rico Cilliers", CC0, "https://polyhaven.com/a/boulder_01",
      ("polyhaven", "boulder_01"), [T + "Rocks/boulder_01_*", M + "SF_SCAN_CRAG_B.fbx"], ["boulder_01"]),
    S("Rocky Trail", "Amal Kumar", CC0, "https://polyhaven.com/a/rocky_trail",
      ("polyhaven", "rocky_trail"), [], ["rocky_trail"], "fetched as a spare; nothing committed"),
    S("Cliff Side", "Dario Barresi, James Ray Cock, Jenelle van Heerden", CC0,
      "https://polyhaven.com/a/cliff_side", ("polyhaven", "cliff_side"), [], ["cliff_side"],
      "fetched as a spare; nothing committed"),

    # ambientCG -- leaf atlases (CC0, https://ambientcg.com/license)
    S("Leaf Set 016", "ambientCG", CC0, "https://ambientcg.com/view?id=LeafSet016",
      ("ambientcg", "LeafSet016"), [T + "Leaves/oak_*"], ["LeafSet016"]),
    S("Leaf Set 014", "ambientCG", CC0, "https://ambientcg.com/view?id=LeafSet014",
      ("ambientcg", "LeafSet014"), [T + "Leaves/birch_*"], ["LeafSet014"]),
    S("Leaf Set 024", "ambientCG", CC0, "https://ambientcg.com/view?id=LeafSet024",
      ("ambientcg", "LeafSet024"), [T + "Leaves/beech_*"], ["LeafSet024"]),
    S("Leaf Set 019", "ambientCG", CC0, "https://ambientcg.com/view?id=LeafSet019",
      ("ambientcg", "LeafSet019"), [T + "Leaves/conifer_*"], ["LeafSet019"]),

    # Kenney -- sound packs (CC0)
    S("Sci-Fi Sounds", "Kenney", CC0, "https://kenney.nl/assets/sci-fi-sounds", ("kenney", None),
      [A + "Sfx/boom_*.ogg", A + "Sfx/engine_*.ogg", A + "Sfx/mine_*.ogg", A + "Sfx/shield_0.ogg",
       A + "Sfx/bigboom_0.ogg", A + "Sfx/rumble_0.ogg", A + "Sfx/crystal_*.ogg"], ["sci-fi-sounds"]),
    S("Impact Sounds", "Kenney", CC0, "https://kenney.nl/assets/impact-sounds", ("kenney", None),
      [A + "Sfx/hitmetal_*.ogg", A + "Sfx/rock_*.ogg", A + "Sfx/thud_0.ogg", A + "Sfx/stomp_0.ogg",
       A + "Sfx/build_0.ogg"], ["impact-sounds"]),
    S("Interface Sounds", "Kenney", CC0, "https://kenney.nl/assets/interface-sounds", ("kenney", None),
      [A + "Sfx/ui_*.ogg"], ["interface-sounds"]),

    # Quaternius -- animated animals (CC0), via OpenGameArt
    S("Animals Pack", "Quaternius", CC0, "https://opengameart.org/content/5-low-poly-animals",
      ("oga", "5-low-poly-animals"),
      ["Assets/StarForge/Art/Fauna/Fox.fbx", "Assets/StarForge/Art/Fauna/Songbird.fbx",
       "Assets/StarForge/Art/Fauna/Songbird_palette.png"], ["Animals_Pack_by_Quaternius"]),
    S("Animal Pack Vol. 2", "Quaternius", CC0,
      "https://opengameart.org/content/animated-animales-low-poly", ("oga", "animated-animales-low-poly"),
      ["Assets/StarForge/Art/Fauna/Wolf.fbx", "Assets/StarForge/Art/Fauna/Eagle.fbx"],
      ["Animal_Pack_Vol.2_by_Quaternius"]),

    # OpenGameArt -- music (CC0)
    S("At Home (orchestral)", "wolfgang", CC0, "https://opengameart.org/content/at-home-orchestral",
      ("oga", "at-home-orchestral"), [A + "Music/music_calm_0.ogg"]),
    S("First Light Particles", "yoiyami", CC0,
      "https://opengameart.org/content/first-light-particles-%E2%80%93-cc0-atmospheric-pianoambient-track",
      ("oga", "first-light-particles-%E2%80%93-cc0-atmospheric-pianoambient-track"),
      [A + "Music/music_calm_1.ogg"]),
    S("Contemplation", "Joth", CC0, "https://opengameart.org/content/contemplation-0",
      ("oga", "contemplation-0"), [A + "Music/music_calm_2.mp3"]),
    S("Insistent: background loop", "yd", CC0,
      "https://opengameart.org/content/insistent-background-loop", ("oga", "insistent-background-loop"),
      [A + "Music/music_tension_0.ogg"]),
    S("Battle Theme A", "cynicmusic", CC0, "https://opengameart.org/content/battle-theme-a",
      ("oga", "battle-theme-a"), [A + "Music/music_combat_0.mp3"]),

    # OpenGameArt -- recorded sound the weapons and felled trees are built from (CC0)
    S("The Free Firearm Sound Library", "Ben Jaszczak, Brian Nelson, Kevin Heras, Matthew Nanney", CC0,
      "https://opengameart.org/content/the-free-firearm-sound-library",
      ("oga", "the-free-firearm-sound-library"),
      [A + "Sfx/rifle_*.wav", A + "Sfx/cannon_*.wav", A + "Sfx/bolt_*.wav", A + "Sfx/pulse_*.wav"],
      ["firearms.7z"]),
    S("tree chop fall thud", "kheetor", CC0, "https://opengameart.org/content/tree-chop-fall-thud",
      ("oga", "tree-chop-fall-thud"), [A + "Sfx/treefall_*.wav", A + "Sfx/treecrash_*.wav"],
      ["chop-tree-fall.ogg"]),
    S("Tree Creaking", "AntumDeluge, from a sample by Department64", CC0,
      "https://opengameart.org/content/tree-creaking", ("oga", "tree-creaking"), [], ["tree_creak.ogg"]),
    S("100 CC0 metal and wood SFX", "rubberduck", CC0,
      "https://opengameart.org/content/100-cc0-metal-and-wood-sfx", ("oga", "100-cc0-metal-and-wood-sfx"),
      [A + "Sfx/crush_*.wav"], ["100-CC0-wood-metal-SFX.zip"]),
    S("75 CC0 breaking / falling / hit sfx", "rubberduck", CC0,
      "https://opengameart.org/content/75-cc0-breaking-falling-hit-sfx",
      ("oga", "75-cc0-breaking-falling-hit-sfx"), [], ["sfx_breaking_and_falling.zip"]),

    # Fonts -- the only assets here with conditions; checked in the file itself
    S("Inter", "The Inter Project Authors", "SIL OFL 1.1", "https://rsms.me/inter/",
      ("font", "OFL"), ["Assets/StarForge/UI/Fonts/Inter-Regular.ttf",
                        "Assets/StarForge/UI/Fonts/Inter-SemiBold.ttf"]),
    S("Roboto Mono", "The Roboto Mono Project Authors", "Apache 2.0",
      "https://fonts.google.com/specimen/Roboto+Mono", ("font", "Apache License, Version 2.0"),
      ["Assets/StarForge/UI/Fonts/RobotoMono-Bold.ttf"]),
]

# Made for this project: models from the Blender builders, generated textures,
# synthesised ambience, icons, and the textures carried over from the author's
# own C++ StarForge (MIT). Anything matching none of these and none of the
# register above is an asset nobody has recorded the origin of.
OWN = [
    M + "SF_*.fbx",                       # minus SF_SCAN_*, which the register claims
    T + "*.png", T + "*.jpg",
    "Assets/StarForge/Map/*.png",
    "Assets/StarForge/Art/Icons/*.png",
    "Assets/StarForge/Art/AppIcon.png",
    "Assets/icon.png", "Assets/cursor.png",
    A + "Ambience/*.wav",
    "Assets/StarForge/Scenes/**/*.exr",   # baked lighting
]
# Unity's own project-template leftovers: redistributable (Unity Companion
# License) but not ours, unused, and better deleted than kept.
TEMPLATE = ["Assets/TutorialInfo/*"]

MEDIA = (".png", ".jpg", ".jpeg", ".tga", ".exr", ".hdr", ".psd",
         ".fbx", ".obj", ".blend", ".ttf", ".otf", ".wav", ".ogg", ".mp3", ".aiff")

# ---------------------------------------------------------------- live checks
_cache = {}


def fetch(url):
    if url not in _cache:
        with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60) as r:
            _cache[url] = r.read().decode("utf-8", "replace")
    return _cache[url]


def font_licence(path):
    """The licence the font itself declares (name table records 13 and 14)."""
    d = open(path, "rb").read()
    count = struct.unpack(">H", d[4:6])[0]
    tables = {}
    for i in range(count):
        tag, _, off, ln = struct.unpack(">4sIII", d[12 + 16 * i:28 + 16 * i])
        tables[tag.decode("latin1")] = (off, ln)
    off, _ = tables["name"]
    n, store = struct.unpack(">HH", d[off + 2:off + 6])
    out = []
    for i in range(n):
        pid, _, _, nid, ln, o = struct.unpack(">HHHHHH", d[off + 6 + 12 * i:off + 18 + 12 * i])
        if nid not in (0, 13, 14):
            continue
        raw = d[off + store + o:off + store + o + ln]
        try:
            out.append(raw.decode("utf-16-be") if pid == 3 else raw.decode("latin1"))
        except UnicodeDecodeError:
            pass
    return " | ".join(out)


def verify(src):
    """Ask the source itself what licence it is under. Returns (ok, what it said)."""
    kind, key = src["check"]
    if kind == "polyhaven":
        fetch(f"https://api.polyhaven.com/info/{key}")           # the asset is still there
        page = fetch("https://polyhaven.com/license")
        return ("CC0" in page, "polyhaven.com/license: CC0")
    if kind == "ambientcg":
        found = json.loads(fetch(f"https://ambientcg.com/api/v2/full_json?id={key}"))["foundAssets"]
        page = fetch("https://ambientcg.com/license")
        return (bool(found) and "CC0" in page, "ambientcg.com/license: CC0 1.0 Universal")
    if kind == "kenney":
        page = fetch(src["url"])
        return ("Creative Commons CC0" in page, "the asset page states Creative Commons CC0")
    if kind == "oga":
        page = fetch("https://opengameart.org/content/" + key)
        text = re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", " ", page)).replace("\xa0", " "))
        m = re.search(r"License\(s\):(.*?)(?:Collections:|Favorites:|Preview:|Description:)", text)
        said = (m.group(1).strip() if m else "?")
        badge = "publicdomain/zero" in page
        return (badge and said.upper().replace(" ", "") == "CC0", f"the page states {said!r}")
    if kind == "font":
        said = font_licence(os.path.join(ROOT, src["files"][0]))
        return (key in said, said[:120])
    return (False, f"no check for {kind}")


# ---------------------------------------------------------------- structure
def matched(path, patterns):
    return any(fnmatch.fnmatch(path, p) for p in patterns)


def media_files():
    out = []
    for base, dirs, names in os.walk(os.path.join(ROOT, "Assets")):
        dirs[:] = [d for d in dirs if d not in (".git",)]
        for n in names:
            if n.lower().endswith(MEDIA):
                out.append(os.path.relpath(os.path.join(base, n), ROOT))
    return sorted(out)


def fetched_ids():
    """Everything Tools/fetch_assets.py downloads, as the names it uses for them."""
    src = open(os.path.join(ROOT, "Tools", "fetch_assets.py")).read()

    def block(name, pattern):
        m = re.search(name + r"\s*=\s*[\[{](.*?)\n[\]}]", src, re.S)
        return set(re.findall(pattern, m.group(1))) if m else set()

    ids = block("TEXTURES", r'"([a-z0-9_]+)":') | block("MODELS", r'"([a-z0-9_]+)":')
    ids |= block("AMBIENTCG", r'"(LeafSet\d+)"') | block("KENNEY", r'"([a-z\-]+)":')
    ids |= block("QUATERNIUS", r'"([A-Za-z0-9_\.]+)":') | block("SFX", r':\s*"([^"]+)"')
    ids |= {t for _, _, t in re.findall(r'\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)"\)',
                                       re.search(r"MUSIC\s*=\s*\{(.*?)\n\}", src, re.S).group(1))}
    return ids


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--offline", action="store_true", help="skip the source checks")
    args = ap.parse_args()
    bad = warn = 0

    print("Third-party sources")
    for s in SOURCES:
        if args.offline:
            said, ok = "(offline)", True
        else:
            try:
                ok, said = verify(s)
            except Exception as e:
                ok, said = False, f"could not reach the source: {e}"
        mark = "ok  " if ok else "FAIL"
        bad += not ok
        print(f"  {mark} {s['name']:38s} {s['licence']:14s} {said}")
        missing = [p for p in s["files"] if not glob.glob(os.path.join(ROOT, p))]
        for p in missing:
            print(f"       FAIL nothing at {p}")
        bad += len(missing)

    print("\nThe register covers what we ship")
    claimed = [p for s in SOURCES for p in s["files"]]
    unknown = []
    for f in media_files():
        if matched(f, claimed) or matched(f, TEMPLATE):
            continue
        if f.startswith(M + "SF_SCAN_"):          # a scan the register must claim
            unknown.append(f)
        elif not matched(f, OWN):
            unknown.append(f)
    for f in unknown:
        print(f"  FAIL {f} is in no register entry and on no list of our own work")
    bad += len(unknown)
    if not unknown:
        print(f"  ok   all {len(media_files())} images, models, fonts and sounds under Assets/ are accounted for")

    print("\nEverything fetch_assets.py downloads is registered")
    known = {i for s in SOURCES for i in s["ids"]} | {s["name"] for s in SOURCES}
    loose = sorted(fetched_ids() - known)
    for i in loose:
        print(f"  FAIL {i} is downloaded but is in no register entry")
    bad += len(loose)
    if not loose:
        print(f"  ok   all {len(fetched_ids())} downloads belong to a registered source")

    print("\nThe register is written down")
    reg = open(os.path.join(ROOT, "THIRD-PARTY.md")).read()
    for s in SOURCES:
        if s["name"] not in reg:
            print(f"  FAIL THIRD-PARTY.md does not mention {s['name']}")
            bad += 1
    readme = open(os.path.join(ROOT, "README.md")).read()
    for who in ("Poly Haven", "ambientCG", "Kenney", "Quaternius", "OpenGameArt", "Inter", "Roboto Mono"):
        if who not in readme:
            print(f"  FAIL the README credits no {who}")
            bad += 1
    for lic in ("CC0-1.0.txt", "OFL-1.1-Inter.txt", "Apache-2.0.txt"):
        if not os.path.exists(os.path.join(ROOT, "LICENSES", lic)):
            print(f"  FAIL LICENSES/{lic} is missing")
            bad += 1
    if os.path.isdir(os.path.join(ROOT, "Assets", "TutorialInfo")):
        print("  warn Assets/TutorialInfo/ is Unity template content, unused (delete it)")
        warn += 1
    if not any(os.path.exists(os.path.join(ROOT, n)) for n in ("LICENSE", "LICENSE.txt", "LICENSE.md")):
        print("  warn the project itself declares no licence (no LICENSE file)")
        warn += 1
    if bad == 0:
        print("  ok   register, README credits and licence texts are all present")

    print(f"\n{'FAILED' if bad else 'clean'}: {bad} problem(s), {warn} warning(s)")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
