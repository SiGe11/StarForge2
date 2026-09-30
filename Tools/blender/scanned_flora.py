"""scanned_flora.py -- flora models from the scanned trees baked by bake_scanned_flora.py.

build_flora.py asks this first for each kind that has a bake in scanned/: a
Model of the scan's own trunk and limbs (material 'bark', the scan's UVs in a
'CardUV' layer so the exporter keeps them, and the shader maps the scan's bark
texture with them) and one quad per baked leaf cluster (material 'leaf' or
'needle', vertex alpha 1, UVs on its tile of the kind's atlas, facing the way it
was rendered from, normals hinted out of the crown). Vertex colour G is a random
value per card, B the sway weight the wind and the blast pressure bend by
(zero at the foot of the trunk, one at the crown's outer tips), the same
contract as the procedural trees, so SF_Tree needs nothing new for them to
move.
"""

import json
import math
import os
import random

import bmesh
from mathutils import Vector

import sf_model as S
from sf_model import Model

HERE = os.path.dirname(os.path.abspath(__file__))
SCANNED = os.path.join(HERE, "scanned")
LEAVES = os.path.join(os.path.dirname(os.path.dirname(HERE)), "Assets", "StarForge", "Art", "Textures", "Leaves")


def available(name):
    return os.path.exists(os.path.join(SCANNED, name + ".json"))


def _clamp01(x):
    return max(0.0, min(1.0, x))


def build(mesh_id, name, lod=False):
    with open(os.path.join(SCANNED, name + ".json")) as fh:
        d = json.load(fh)
    m = Model(mesh_id, name)
    height = d["height"]
    # The crown, from the cards themselves: centred over the foliage, not the
    # trunk, since a scanned tree can lean (the shader bends the leaf normals
    # out from it and the flames stand in it).
    lo = [min(c["c"][a] - 0.6 * c["half"] for c in d["cards"]) for a in range(3)]
    hi = [max(c["c"][a] + 0.6 * c["half"] for c in d["cards"]) for a in range(3)]
    half = [(hi[a] - lo[a]) * 0.5 for a in range(3)]
    crown = {"center": [round((lo[a] + hi[a]) * 0.5, 3) for a in range(3)],
             "radii": [round(max(half[0], half[2]), 3), round(half[1], 3), round(max(half[0], half[2]), 3)]}
    cc = Vector(crown["center"])
    # How far the foliage reaches from the trunk, for the sway weights.
    radius = max(max(math.hypot(c["c"][0], c["c"][2]) + 0.6 * c["half"] for c in d["cards"]), 0.3)
    rx, ry = max(crown["radii"][0], 0.1), max(crown["radii"][1], 0.1)
    mat = "needle" if d.get("needles") else "leaf"

    def sway(co, bark):
        up = _clamp01(co.y / height) ** 1.6
        out = 0.55 + 0.45 * _clamp01(math.hypot(co.x, co.z) / radius)
        return _clamp01(up * out * (0.45 if bark else 1.0))

    # The trunk and limbs, decimated in the bake; the far copy decimates again.
    t = d.get("trunk")
    if t:
        bm = S.new_bm()
        vs = [bm.verts.new(Vector(p)) for p in t["verts"]]
        uv = bm.loops.layers.uv.new("CardUV")
        for tri, tuv in zip(t["tris"], t["uvs"]):
            try:
                # Game space is a reflection of Blender's: reverse the winding.
                f = bm.faces.new((vs[tri[0]], vs[tri[2]], vs[tri[1]]))
            except ValueError:
                continue
            for loop, k in zip(f.loops, (0, 2, 1)):
                loop[uv].uv = tuv[k]
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context="VERTS")
        if lod:
            _decimate(bm, 0.3)
        bm.normal_update()
        S.set_mat(bm, bm.faces[:], "bark")
        S.set_vdata(bm, g=0.5, b=lambda co: sway(co, True), a=0.0)
        m.add(bm)

    # The cards. The far copy keeps them all: a card is two triangles, and thinning
    # them only saves fill if the crown is let go thin (the alpha-tested cards'
    # area is what a crown costs); tried at every other card 1.3 times the size,
    # the far crowns went patchy.
    rng = random.Random(len(d["cards"]) * 7 + int(height * 10))
    for c in d["cards"]:
        centre, u, v, n = Vector(c["c"]), Vector(c["u"]), Vector(c["v"]), Vector(c["n"])
        h = c["half"]
        u0, v0, u1, v1 = c["uv"]
        corners = [(centre - u * h - v * h, (u0, v0)), (centre + u * h - v * h, (u1, v0)),
                   (centre + u * h + v * h, (u1, v1)), (centre - u * h + v * h, (u0, v1))]
        # Wind the quad so its face normal is the way it was rendered from.
        if (corners[1][0] - corners[0][0]).cross(corners[2][0] - corners[0][0]).dot(n) < 0:
            corners = [corners[0], corners[3], corners[2], corners[1]]
        bm = S.new_bm()
        verts = [bm.verts.new(p) for p, _ in corners]
        f = bm.faces.new(verts)
        uvl = bm.loops.layers.uv.new("CardUV")
        for loop in f.loops:
            loop[uvl].uv = corners[verts.index(loop.vert)][1]
        bm.normal_update()
        S.set_normal_hint(bm, cc)
        S.set_mat(bm, bm.faces[:], mat)
        g = rng.random()
        S.set_vdata(bm, g=g, b=lambda co: sway(co, False), a=1.0)
        # Occlusion by depth in the crown: the outer cards in the open, the inner
        # ones and the underside in the crown's shade (SF_Tree adds the height term).
        lay = bm.verts.layers.float.new("sf_ao")
        for vert in bm.verts:
            q = vert.co - cc
            r = math.sqrt((q.x / rx) ** 2 + (q.y / ry) ** 2 + (q.z / rx) ** 2)
            vert[lay] = 0.35 + 0.65 * _clamp01((r - 0.2) / 0.8)
        m.add(bm)

    m.meta["foliage"] = {"center": crown["center"], "radii": crown["radii"]}
    m.meta["scanned"] = d["source"]
    albedo = _atlas_albedo(name)
    if albedo:
        m.meta["leaf_albedo"] = albedo
    return m


def _atlas_albedo(name):
    """The mean linear colour of the leaves in the kind's card atlas (where they
    pass the alpha cut-off), which MapBuilder tints onto the kind's target green."""
    import bpy
    import numpy as np
    path = os.path.join(LEAVES, "scan_%s_col.png" % name)
    if not os.path.exists(path):
        return None
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    px = np.empty(len(img.pixels), dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    px = px.reshape(-1, 4)
    c = px[px[:, 3] > 0.45, :3]
    lin = np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)
    return [round(float(x), 4) for x in lin.mean(0)]


def _decimate(bm, ratio):
    """Collapse edges until about `ratio` of the faces are left (bmesh has no
    decimate; this goes through a throwaway mesh object and the modifier)."""
    import bpy
    me = bpy.data.meshes.new("dec_tmp")
    bm.to_mesh(me)
    obj = bpy.data.objects.new("dec_tmp", me)
    bpy.context.scene.collection.objects.link(obj)
    mod = obj.modifiers.new("dec", "DECIMATE")
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    out = bpy.data.meshes.new_from_object(obj.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    bm.clear()
    bm.from_mesh(out)
    bpy.data.objects.remove(obj)
    bpy.data.meshes.remove(me)
    bpy.data.meshes.remove(out)
