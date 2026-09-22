#!/usr/bin/env python3
"""Copy the animated animals StarForge uses into the project.

    python3 Tools/fetch_assets.py      # once: downloads the Quaternius packs
    python3 Tools/pack_fauna.py

The animals are Quaternius's low-poly animated models (CC0, from OpenGameArt;
Art/Source/quaternius). They come as rigged FBX with their walk, idle and flight
cycles and no colours (every material is plain grey); SceneAssembler gives each
part its colour and builds the prefabs View/Fauna.cs spawns. Copied under clean
names into Assets/StarForge/Art/Fauna/.
"""
import os
import shutil
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "Art", "Source", "quaternius")
OUT = os.path.join(ROOT, "Assets", "StarForge", "Art", "Fauna")
FILES = {
    "Wolf.fbx": "Animal_Pack_Vol.2_by_Quaternius/Animal Pack Vol.2 by @Quaternius/FBX/Wolf.fbx",
    "Eagle.fbx": "Animal_Pack_Vol.2_by_Quaternius/Animal Pack Vol.2 by @Quaternius/FBX/Eagle.fbx",
    "Fox.fbx": "Animals_Pack_by_Quaternius/Animals Pack by Quaternius/FBX/Red Fox.fbx",
    "Songbird.fbx": "Animals_Pack_by_Quaternius/Animals Pack by Quaternius/FBX/bird.fbx",
}


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, rel in FILES.items():
        src = os.path.join(SRC, rel)
        if not os.path.exists(src):
            sys.exit(f"missing {src} (run Tools/fetch_assets.py)")
        shutil.copyfile(src, os.path.join(OUT, name))
        print(name)


if __name__ == "__main__":
    main()
