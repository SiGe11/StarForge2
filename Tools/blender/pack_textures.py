"""Pack the downloaded ground scans into the textures SF_Terrain samples.

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
        --python Tools/blender/pack_textures.py

Blender is only the image library here (the system Python has no imaging
module). For every ground layer it reads the Poly Haven scan from
Art/Source/polyhaven/ (Tools/fetch_assets.py) and writes, into
Assets/StarForge/Art/Textures/Terrain/:

  <layer>_ch.png   colour in RGB (sRGB) and height in A. The terrain blends its
                   layers on this height, so stones stand out of the sand and
                   moss fills the hollows between them instead of a soft
                   cross-fade. The displacement scan is stretched to its own
                   2nd..98th percentile so every layer uses the whole range.
  <layer>_nrm.jpg  the OpenGL normal map, unchanged.

Poly Haven's real-world size of each scan goes to terrain_layers.json, which
MapBuilder reads for the tiling.
"""
import json
import os
import shutil

import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "Art", "Source", "polyhaven")
OUT = os.path.join(ROOT, "Assets", "StarForge", "Art", "Textures", "Terrain")

# Terrain layer order is the splat order: lichen, gravel, cliff, ash.
LAYERS = [
    ("meadow", "coast_sand_rocks_02"),
    ("dirt", "forest_ground_04"),
    ("cliff", "aerial_rocks_02"),
    ("sand", "coast_sand_01"),
]


def load(path, data):
    img = bpy.data.images.load(path)
    if data:
        img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    return img, px


def main():
    manifest = json.load(open(os.path.join(SRC, "manifest.json")))
    os.makedirs(OUT, exist_ok=True)
    info = {}
    for layer, aid in LAYERS:
        files = manifest[aid]["files"]
        d = os.path.join(SRC, aid)
        _, col = load(os.path.join(d, files["Diffuse"]), False)
        _, disp = load(os.path.join(d, files["Displacement"]), True)
        hgt = disp[..., 0]
        lo, hi = np.percentile(hgt, 2), np.percentile(hgt, 98)
        hgt = np.clip((hgt - lo) / max(hi - lo, 1e-4), 0.0, 1.0)
        h, w = hgt.shape
        if col.shape[:2] != (h, w):
            raise RuntimeError(f"{aid}: colour {col.shape[:2]} and height {hgt.shape} differ")
        out = np.empty((h, w, 4), dtype=np.float32)
        out[..., :3] = col[..., :3]
        out[..., 3] = hgt
        img = bpy.data.images.new(f"{layer}_ch", w, h, alpha=True)
        img.alpha_mode = "STRAIGHT"
        img.pixels[:] = out.ravel()
        img.filepath_raw = os.path.join(OUT, f"{layer}_ch.png")
        img.file_format = "PNG"
        img.save()
        shutil.copyfile(os.path.join(d, files["nor_gl"]), os.path.join(OUT, f"{layer}_nrm.jpg"))
        mean = col[..., :3].reshape(-1, 3).mean(0)
        info[layer] = {
            "source": aid,
            "size_m": manifest[aid].get("dimensions_m", [2.0, 2.0])[0],
            "mean_srgb": [round(float(v), 3) for v in mean],
        }
        print(f"{layer:7s} {aid:22s} {w}x{h}  {info[layer]['size_m']} m  mean {info[layer]['mean_srgb']}")
    with open(os.path.join(OUT, "terrain_layers.json"), "w") as f:
        json.dump(info, f, indent=2)


main()
