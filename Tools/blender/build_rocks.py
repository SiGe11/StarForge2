"""Turn the scanned Poly Haven rocks into StarForge's boulders and crags.

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
        --python Tools/blender/build_rocks.py

Reads the glTF scans Tools/fetch_assets.py put in Art/Source/polyhaven/ and
writes, into Assets/StarForge/Art/Models/:

  SF_SCAN_BOULDER_<A..F>.fbx  single rocks sized to the Boulder footprint
                              (1.15 m radius at scale 1), which a Mauler crushes;
  SF_SCAN_CRAG_<A,B>.fbx      big outcrops standing where the old rock spires did;
  SF_SCAN_SHELF_A.fbx         a group of long slabs stepping down a slope;
  rocks.json                  radius, height and triangles per model, merged with
                              models.json by ModelFactory.Meta;

and their colour and normal maps into Assets/StarForge/Art/Textures/Rocks/.
The scans are 8-66k triangles; they are decimated to what a rock covering a
few dozen pixels at RTS range needs, the normal map keeping the surface
detail. Each model has one material slot, scan_<asset>, which
SFMaterialLibrary maps to a StarForge/Rock material with that asset's maps.
Orientation and scale follow export_fbx.py (Blender Z up is Unity Y up).
"""
import json
import math
import os
import shutil

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "Art", "Source", "polyhaven")
OUT = os.path.join(ROOT, "Assets", "StarForge", "Art", "Models")
TEX = os.path.join(ROOT, "Assets", "StarForge", "Art", "Textures", "Rocks")

BOULDER_RADIUS = 1.15   # Boulder.radius; the Rubble volume under it is 2.3 m wide

# (model, asset, [(object, offset xy, yaw degrees, scale, pitch degrees)], fit, size, triangles)
# fit: "radius" scales the group's footprint radius to size, "height" its height.
MODELS = [
    ("SCAN_BOULDER_A", "rock_moss_set_02", [("rock11", (0, 0), 0, 1, 0)], "radius", BOULDER_RADIUS, 900),
    ("SCAN_BOULDER_B", "rock_moss_set_02", [("rock13", (0, 0), 0, 1, 0)], "radius", BOULDER_RADIUS, 900),
    ("SCAN_BOULDER_C", "rock_moss_set_02", [("rock10", (0, 0), 0, 1, 0)], "radius", BOULDER_RADIUS, 900),
    ("SCAN_BOULDER_D", "rock_moss_set_02", [("rock12", (0, 0), 0, 1, 0)], "radius", BOULDER_RADIUS, 900),
    ("SCAN_BOULDER_E", "rock_moss_set_01", [("rock04", (0, 0), 0, 1, 0)], "radius", BOULDER_RADIUS, 900),
    ("SCAN_BOULDER_F", "rock_moss_set_01", [("rock03", (0, 0), 0, 1, 0)], "radius", BOULDER_RADIUS, 900),
    # A long slab stood on end with a block at its foot: the landmark the old
    # rock spire was.
    ("SCAN_CRAG_A", "rock_moss_set_01",
     [("rock01", (0, 0), 0, 1, 90), ("rock04", (1.3, -0.9), 30, 0.7, 0)], "height", 6.4, 3400),
    ("SCAN_CRAG_B", "boulder_01", [("boulder_01", (0, 0), 0, 1, 0)], "height", 4.4, 3200),
    ("SCAN_SHELF_A", "rock_moss_set_01",
     [("rock01", (-2.1, 0.2), 10, 1.0, 0), ("rock02", (0.6, 0.9), 55, 0.95, 0), ("rock05", (2.3, -0.7), -25, 0.85, 0)],
     "radius", 5.0, 3000),
]

SINK = 0.12   # of the height, so the uneven underside of a scan never shows


def import_asset(aid):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, aid, f"{aid}_1k.gltf"))
    found = {}
    for o in bpy.data.objects:
        if o.type == "MESH":
            # Bake the node transform into the mesh so every piece starts in its own frame.
            o.data.transform(o.matrix_world)
            o.matrix_world = Matrix.Identity(4)
            # glTF splits vertices along every UV seam; welded again (UVs live on
            # the face corners in Blender, so nothing is lost) the surface is one
            # piece the decimator can collapse. Unwelded, boulder_01 stalled at
            # 26k of its 66k triangles.
            bm = bmesh.new()
            bm.from_mesh(o.data)
            bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
            bm.to_mesh(o.data)
            bm.free()
            found[o.name.replace(aid + "_", "")] = o
    return found


def bounds(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for v in o.data.vertices:
            p = o.matrix_world @ v.co
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
    return lo, hi


def decimate(o, target):
    # A collapse pass can stop short of the ratio; repeat until it gets there.
    bpy.context.view_layer.objects.active = o
    for _ in range(6):
        tris = sum(len(p.vertices) - 2 for p in o.data.polygons)
        if tris <= target * 1.05:
            break
        mod = o.modifiers.new("dec", "DECIMATE")
        mod.ratio = target / tris
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def build(name, aid, parts, fit, size, target):
    pieces = import_asset(aid)
    objs = []
    share = target // len(parts)
    for key, (ox, oy), yaw, scale, pitch in parts:
        o = pieces[key]
        # Each piece stood up if asked, centred on its own footprint and set
        # down on z = 0 first.
        if pitch:
            o.data.transform(Matrix.Rotation(math.radians(pitch), 4, "X"))
        lo, hi = bounds([o])
        c = (lo + hi) * 0.5
        o.data.transform(Matrix.Translation((-c.x, -c.y, -lo.z)))
        o.data.transform(Matrix.Translation((ox, oy, 0)) @ Matrix.Rotation(math.radians(yaw), 4, "Z") @ Matrix.Scale(scale, 4))
        decimate(o, share)
        objs.append(o)
    for o in list(pieces.values()):
        if o not in objs:
            bpy.data.objects.remove(o)

    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = objs[0]
    o.name = name

    lo, hi = bounds([o])
    c = (lo + hi) * 0.5
    radius = max(hi.x - lo.x, hi.y - lo.y) * 0.5
    height = hi.z - lo.z
    s = size / radius if fit == "radius" else size / height
    o.data.transform(Matrix.Scale(s, 4) @ Matrix.Translation((-c.x, -c.y, -lo.z - height * SINK)))

    # The decimated surface is shaded smooth (the scan's own split normals do
    # not survive decimation); the normal map carries the chips and cracks.
    me = o.data
    if hasattr(me, "use_auto_smooth"):
        me.use_auto_smooth = False
    for p in me.polygons:
        p.use_smooth = True
    try:
        bpy.context.view_layer.objects.active = o
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    except RuntimeError:
        pass
    me.materials.clear()
    mat = bpy.data.materials.get(f"scan_{aid}") or bpy.data.materials.new(f"scan_{aid}")
    me.materials.append(mat)

    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, f"SF_{name}.fbx"), use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", use_mesh_modifiers=False, colors_type="LINEAR",
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP")

    lo, hi = bounds([o])
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    info = {"radius": round(max(-lo.x, hi.x, -lo.y, hi.y), 3), "height": round(hi.z, 3), "triangles": tris,
            "materials": [f"scan_{aid}"], "source": f"https://polyhaven.com/a/{aid}"}
    print(f"{name:16s} {aid:18s} tris {tris:5d} radius {info['radius']:5.2f} height {info['height']:5.2f}")
    return info


def copy_maps(aid):
    d = os.path.join(SRC, aid, "textures")
    os.makedirs(TEX, exist_ok=True)
    for suffix, out in (("diff", "col"), ("nor_gl", "nrm")):
        shutil.copyfile(os.path.join(d, f"{aid}_{suffix}_1k.jpg"), os.path.join(TEX, f"{aid}_{out}.jpg"))


def main():
    meta = {}
    for name, aid, parts, fit, size, target in MODELS:
        meta[name] = build(name, aid, parts, fit, size, target)
    for aid in sorted({m[1] for m in MODELS}):
        copy_maps(aid)
    with open(os.path.join(OUT, "rocks.json"), "w") as f:
        json.dump(meta, f, indent=2)


main()
