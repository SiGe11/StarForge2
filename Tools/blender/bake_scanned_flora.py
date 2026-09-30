"""bake_scanned_flora.py -- turn Poly Haven's scanned trees into game trees.

    Blender --background --factory-startup --python Tools/blender/bake_scanned_flora.py -- [KIND ...]

The scans (fetched by Tools/fetch_assets.py into Art/Source/polyhaven/) are the
real thing -- a trunk and limbs with their own bark, and hundreds of thousands
of individual leaves and twigs -- far past what the game can draw hundreds of
times over. This keeps what reads at RTS range and bakes the rest:

  * the trunk and limbs (the scan's bark material), decimated to a few hundred
    or thousand triangles, keeping the scan's UVs so its own bark texture fits;
  * the leaves and twigs, grouped by position into clusters of a few tens of
    centimetres; each cluster is rendered on its own, from the side the RTS
    camera sees it from (out of the crown and up), into one tile of an atlas:
    colour with alpha (with a little of the spray's own shade in it, from an
    ambient-occlusion term, so a card is not a flat decal), and a second atlas
    of the leaves' normals in the card's frame. A card in the game is one quad
    in that cluster's place, facing the way it was rendered from.

So a crown keeps the scanned tree's silhouette, its gaps and its real leaves,
for a hundred-odd quads. The previous crowns were drawn from the same kind of
cards, but set on a generated branch skeleton with photographed sprays that
belonged to no tree in particular, and too few of them: a bush was 64 triangles.

Writes, per kind:
  Assets/StarForge/Art/Textures/Leaves/scan_<KIND>_col.png   card colour + coverage
  Assets/StarForge/Art/Textures/Leaves/scan_<KIND>_nrm.png   card normals
  Assets/StarForge/Art/Textures/Leaves/scan_<KIND>_bark.png  the scan's bark (if it has a trunk)
  Tools/blender/scanned/<KIND>.json  trunk mesh and card layout, in game space,
                                     which scanned_flora.py (called from
                                     build_flora.py) turns into the model; it is
                                     committed, so export_fbx.py rebuilds the
                                     trees without the 700 MB of scans.

The rendering uses Cycles on the CPU with persistent data, so the scan's BVH is
built once and each tile is a small render. A big tree takes a few minutes.
"""

import json
import math
import os
import struct
import sys
import time
import zlib

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "Art", "Source", "polyhaven")
OUT_JSON = os.path.join(ROOT, "Tools", "blender", "scanned")
OUT_TEX = os.path.join(ROOT, "Assets", "StarForge", "Art", "Textures", "Leaves")

# Per kind: the source, which of its objects (and optionally which part along x,
# for a set of several plants side by side), which materials are bark and which
# are foliage, the height to scale it to (the kind's height before, so placement,
# blocking and the crown data stay as they were), how many cards, the trunk's
# triangle budget, and whether the cards are needles (the shader's leaf style).
KINDS = {
    "TREE_BROAD": dict(src="island_tree_01", obj="island_tree_01_LOD0",
                       bark=["island_tree_01"], foliage=["island_tree_01_leaves", "island_tree_01_branches"],
                       height=8.0, cards=150, trunk_tris=1600),
    # The tallest of the three firs; its boughs (the 'bark' material) and dead
    # twigs go into the cards with the needles, the stem stays geometry.
    "TREE_PINE": dict(src="fir_tree_01", obj="fir_tree_01_a_LOD0", bark_tex="trunk_a",
                      bark=["fir_tree_01_trunk_a"],
                      foliage=["fir_tree_01_twig", "fir_tree_01_bark", "fir_tree_01_dead_branches"],
                      height=10.5, cards=160, trunk_tris=700, needles=True),
    "TREE_TALL": dict(src="tree_small_02", obj="tree_small_02_LOD0",
                      bark=["tree_small_02_trunk"], foliage=["tree_small_02_leaves", "tree_small_02_branches"],
                      height=10.6, cards=140, trunk_tris=1400),
    "TREE_BIRCH": dict(src="jacaranda_tree", obj="jacaranda_tree_LOD0",
                       bark=["jacaranda_tree_trunk"], foliage=["jacaranda_tree_leaves", "jacaranda_tree_branches"],
                       height=9.0, cards=170, trunk_tris=1800),
    # Shrubs and the fern are all cards: their stems are twig-thin.
    # A shrub's cards show more of their neighbours (sphere): at this size a card
    # showing only its own cluster left the bush a see-through sprig.
    "BUSH": dict(src="searsia_burchellii", obj="searsia_burchellii_large_LOD0", bark=[],
                 foliage=["searsia_burchellii_leaves", "searsia_burchellii", "searsia_burchellii_twigs"],
                 height=1.75, cards=40, trunk_tris=0, sphere=1.85),
    "BUSH_FLOWER": dict(src="searsia_lucida", obj="searsia_lucida_b_LOD0", bark=[],
                        foliage=["searsia_lucida", "searsia_lucida_leaves", "searsia_lucida_twigs"],
                        height=1.95, cards=32, trunk_tris=0, sphere=1.85),
    "FERN": dict(src="fern_02", obj="fern_02_b", bark=[], foliage=["fern_02"],
                 height=0.46, cards=14, trunk_tris=0),
}

TILE_MAX = 2048          # atlas side
GROW = 0.7              # coverage a leaf's edge texel neighbours get (cut-off 0.45)
SPHERE = 1.45            # a card shows the foliage this far round its cluster, relative to its spread
AO_DISTANCE = 0.18       # metres in the scan's own size, before scaling


# ---------------------------------------------------------------- small helpers
def write_png(path, img):
    """img: (h, w, c) uint8, row 0 at the top."""
    h, w, c = img.shape
    raw = b"".join(b"\0" + img[y].tobytes() for y in range(h))

    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)

    ct = {3: 2, 4: 6}[c]
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, ct, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 7)) + chunk(b"IEND", b""))


def read_render(path):
    """A rendered PNG as float RGBA, row 0 at the bottom (Blender's order), raw values."""
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = "Non-Color"
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)


def dilate(rgb, alpha, passes=6):
    """Spread colour into the transparent texels round the leaves, so mipmapping
    does not blend the cards' edges toward black."""
    rgb = rgb.copy()
    filled = alpha > 0.02
    for _ in range(passes):
        acc = np.zeros_like(rgb)
        cnt = np.zeros(alpha.shape, dtype=np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1)):
            m = np.roll(np.roll(filled, dy, 0), dx, 1)
            c = np.roll(np.roll(rgb, dy, 0), dx, 1)
            acc += c * m[..., None]
            cnt += m
        grow = (~filled) & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][:, None]
        filled = filled | grow
    return rgb


def kmeans(pts, k, iters=18, seed=7):
    """Positions into k clusters (k-means++ on a sample, then every point assigned)."""
    rng = np.random.default_rng(seed)
    n = len(pts)
    sample = pts[rng.choice(n, min(n, 60000), replace=False)]
    centers = [sample[rng.integers(len(sample))]]
    d2 = np.sum((sample - centers[0]) ** 2, 1)
    for _ in range(k - 1):
        c = sample[rng.choice(len(sample), p=d2 / d2.sum())]
        centers.append(c)
        d2 = np.minimum(d2, np.sum((sample - c) ** 2, 1))
    C = np.array(centers)

    def assign(p):
        out = np.empty(len(p), dtype=np.int32)
        for s in range(0, len(p), 40000):
            q = p[s:s + 40000]
            out[s:s + 40000] = np.argmin(((q[:, None, :] - C[None, :, :]) ** 2).sum(2), 1)
        return out

    for _ in range(iters):
        lab = assign(sample)
        for j in range(k):
            m = sample[lab == j]
            if len(m):
                C[j] = m.mean(0)
    return assign(pts), C


# ---------------------------------------------------------------- scene set-up
def mesh_arrays(obj):
    """World-space vertex positions, per-face vertex lists, material index and
    loop UVs of a mesh object, pulled out with foreach_get."""
    me = obj.data
    mw = np.array(obj.matrix_world)
    nv = len(me.vertices)
    co = np.empty(nv * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
    npoly = len(me.polygons)
    ls = np.empty(npoly, dtype=np.int64); me.polygons.foreach_get("loop_start", ls)
    lt = np.empty(npoly, dtype=np.int64); me.polygons.foreach_get("loop_total", lt)
    mi = np.empty(npoly, dtype=np.int64); me.polygons.foreach_get("material_index", mi)
    nl = len(me.loops)
    lv = np.empty(nl, dtype=np.int64); me.loops.foreach_get("vertex_index", lv)
    uv = np.zeros(nl * 2, dtype=np.float64)
    if me.uv_layers.active is not None:
        me.uv_layers.active.data.foreach_get("uv", uv)
    elif "UVMap" in me.attributes and me.attributes["UVMap"].domain == "CORNER":
        # Some scans (the firs) keep their UVs as a plain 3-vector corner attribute.
        a = me.attributes["UVMap"]
        n = 3 if a.data_type == "FLOAT_VECTOR" else 2
        raw = np.empty(nl * n, dtype=np.float64)
        a.data.foreach_get("vector", raw)
        uv = raw.reshape(-1, n)[:, :2].ravel()
    return co, ls, lt, mi, lv, uv.reshape(-1, 2)


def face_centroids(co, ls, lt, lv):
    cen = np.zeros((len(ls), 3))
    for k in range(int(lt.max())):
        has = lt > k
        cen[has] += co[lv[ls[has] + k]]
    return cen / lt[:, None]


def foliage_material(mat, uv_name):
    """Replace a foliage material with what the card renders need: the texture's
    colour (with a little ambient occlusion from the spray round it) or the
    surface normal in camera space, switched by the scene's sf_mode, cut out on
    the leaf alpha, and shown only within the sphere round the card being
    rendered (the view layer's sf_centre and sf_radius)."""
    nt = mat.node_tree
    diff = alpha_img = None
    for n in nt.nodes:
        if n.type == "TEX_IMAGE" and n.image is not None:
            name = n.image.name.lower()
            if "alpha" in name:
                alpha_img = n.image
            elif "diff" in name or "col" in name:
                diff = n.image
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    N, L = nt.nodes, nt.links
    out = N.new("ShaderNodeOutputMaterial")
    # The UVs by attribute name: a real UV map or the firs' plain attribute alike.
    uvn = N.new("ShaderNodeAttribute"); uvn.attribute_type = "GEOMETRY"; uvn.attribute_name = uv_name
    col = N.new("ShaderNodeTexImage"); col.image = diff
    L.new(uvn.outputs["Vector"], col.inputs["Vector"])
    if alpha_img is not None:
        al = N.new("ShaderNodeTexImage"); al.image = alpha_img
        al.image.colorspace_settings.name = "Non-Color"
        L.new(uvn.outputs["Vector"], al.inputs["Vector"])
        alpha = al.outputs["Color"]
    else:
        alpha = col.outputs["Alpha"] if (diff is not None and diff.depth == 32) else None

    ao = N.new("ShaderNodeAmbientOcclusion")
    ao.inputs["Distance"].default_value = AO_DISTANCE
    ao.samples = 8
    L.new(col.outputs["Color"], ao.inputs["Color"])
    shade = N.new("ShaderNodeMapRange")
    shade.inputs["To Min"].default_value = 0.62
    L.new(ao.outputs["AO"], shade.inputs["Value"])
    lit = N.new("ShaderNodeVectorMath"); lit.operation = "SCALE"
    L.new(col.outputs["Color"], lit.inputs[0])
    L.new(shade.outputs["Result"], lit.inputs["Scale"])

    # Camera-space normal, turned toward the camera (the leaves are thin sheets
    # seen from either side).
    geo = N.new("ShaderNodeNewGeometry")
    facing = N.new("ShaderNodeVectorMath"); facing.operation = "DOT_PRODUCT"
    L.new(geo.outputs["Normal"], facing.inputs[0]); L.new(geo.outputs["Incoming"], facing.inputs[1])
    flip = N.new("ShaderNodeMath"); flip.operation = "SIGN"
    L.new(facing.outputs["Value"], flip.inputs[0])
    nflip = N.new("ShaderNodeVectorMath"); nflip.operation = "SCALE"
    L.new(geo.outputs["Normal"], nflip.inputs[0]); L.new(flip.outputs["Value"], nflip.inputs["Scale"])
    cam = N.new("ShaderNodeVectorTransform"); cam.vector_type = "NORMAL"
    cam.convert_from = "WORLD"; cam.convert_to = "CAMERA"
    L.new(nflip.outputs["Vector"], cam.inputs["Vector"])
    enc = N.new("ShaderNodeVectorMath"); enc.operation = "MULTIPLY_ADD"
    enc.inputs[1].default_value = (0.5, 0.5, -0.5)   # Blender's camera looks down -Z
    enc.inputs[2].default_value = (0.5, 0.5, 0.5)
    L.new(cam.outputs["Vector"], enc.inputs[0])

    mode = N.new("ShaderNodeAttribute"); mode.attribute_type = "VIEW_LAYER"; mode.attribute_name = "sf_mode"
    pick = N.new("ShaderNodeMix"); pick.data_type = "VECTOR"
    L.new(mode.outputs["Fac"], pick.inputs[0])
    L.new(lit.outputs["Vector"], pick.inputs[4])
    L.new(enc.outputs["Vector"], pick.inputs[5])
    emit = N.new("ShaderNodeEmission")
    L.new(pick.outputs[1], emit.inputs["Color"])

    # Shown only within the sphere round the card being rendered (the scene's
    # sf_centre, sf_radius): its own cluster and the edges of its neighbours, so
    # neighbouring cards overlap the way sprays on a bough do.
    geo2 = N.new("ShaderNodeNewGeometry")
    centre = N.new("ShaderNodeAttribute"); centre.attribute_type = "VIEW_LAYER"; centre.attribute_name = "sf_centre"
    rad = N.new("ShaderNodeAttribute"); rad.attribute_type = "VIEW_LAYER"; rad.attribute_name = "sf_radius"
    dist = N.new("ShaderNodeVectorMath"); dist.operation = "DISTANCE"
    L.new(geo2.outputs["Position"], dist.inputs[0]); L.new(centre.outputs["Vector"], dist.inputs[1])
    # The rim is ragged, not a circle: each leaf (mesh island) has its own reach,
    # so toward the edge of the sphere leaves drop out one by one.
    reach = N.new("ShaderNodeMapRange")
    reach.inputs["To Min"].default_value = 0.55
    L.new(geo2.outputs["Random Per Island"], reach.inputs["Value"])
    limit = N.new("ShaderNodeMath"); limit.operation = "MULTIPLY"
    L.new(reach.outputs["Result"], limit.inputs[0]); L.new(rad.outputs["Fac"], limit.inputs[1])
    inside = N.new("ShaderNodeMath"); inside.operation = "LESS_THAN"
    L.new(dist.outputs["Value"], inside.inputs[0]); L.new(limit.outputs["Value"], inside.inputs[1])
    cover = N.new("ShaderNodeMath"); cover.operation = "MULTIPLY"
    L.new(inside.outputs["Value"], cover.inputs[0])
    if alpha is not None:
        L.new(alpha, cover.inputs[1])
    else:
        cover.inputs[1].default_value = 1.0
    transp = N.new("ShaderNodeBsdfTransparent")
    mix = N.new("ShaderNodeMixShader")
    L.new(cover.outputs["Value"], mix.inputs["Fac"])
    L.new(transp.outputs["BSDF"], mix.inputs[1]); L.new(emit.outputs["Emission"], mix.inputs[2])
    L.new(mix.outputs["Shader"], out.inputs["Surface"])
    if hasattr(mat, "blend_method"):
        mat.blend_method = "HASHED"
    return diff


def hide_material(mat):
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    nt.links.new(tr.outputs["BSDF"], out.inputs["Surface"])


# ---------------------------------------------------------------- one kind
def bake(kind, cfg):
    t0 = time.time()
    path = os.path.join(SRC, cfg["src"], cfg["src"] + "_1k.blend")
    if not os.path.exists(path):
        raise SystemExit(f"{path} missing: run python3 Tools/fetch_assets.py {cfg['src']}")
    bpy.ops.wm.open_mainfile(filepath=path)
    obj = bpy.data.objects[cfg["obj"]]
    # Nothing else renders.
    for o in bpy.data.objects:
        o.hide_render = o is not obj
    co, ls, lt, mi, lv, luv = mesh_arrays(obj)
    names = [m.name if m else "" for m in obj.data.materials]
    bark_i = [i for i, n in enumerate(names) if n in cfg["bark"]]
    fol_i = [i for i, n in enumerate(names) if n in cfg["foliage"]]
    is_bark = np.isin(mi, bark_i)
    is_fol = np.isin(mi, fol_i)
    cen = face_centroids(co, ls, lt, lv)
    if "xrange" in cfg:
        x0, x1 = cfg["xrange"]
        keep = (cen[:, 0] >= x0) & (cen[:, 0] <= x1)
        is_bark &= keep
        is_fol &= keep
    print(f"{kind}: {len(ls)} faces, {is_bark.sum()} bark, {is_fol.sum()} foliage; materials {names}")

    # Scale and origin: the trunk's foot at (0, 0, 0), the plant at the kind's height.
    used = is_bark | is_fol
    face_of_loop = np.repeat(np.arange(len(ls)), lt)
    used_z = co[lv[used[face_of_loop]], 2]
    zmin, zmax = used_z.min(), used_z.max()
    base_src = is_bark if is_bark.any() else is_fol
    foot = cen[base_src & (cen[:, 2] < zmin + 0.35 * max(0.3, (zmax - zmin) * 0.1))]
    if len(foot) == 0:
        foot = cen[base_src]
    ox, oy = foot[:, 0].mean(), foot[:, 1].mean()
    scale = cfg["height"] / max(1e-3, zmax - zmin)
    origin = np.array([ox, oy, zmin])

    # ------------------------------------------------------------ clusters
    fol_faces = np.nonzero(is_fol)[0]
    pts = cen[fol_faces]
    k = int(os.environ.get("SF_CARDS", cfg["cards"]))
    labels, C = kmeans(pts, k)
    # The crown: centre and extent of the foliage.
    lo, hi = pts.min(0), pts.max(0)
    crown_c = (lo + hi) / 2.0
    print(f"  {k} clusters from {len(pts)} foliage faces ({time.time() - t0:.0f} s)")

    # Each face's cluster, or -1 off the foliage.
    cid = np.full(len(ls), -1.0, dtype=np.float32)
    cid[fol_faces] = labels
    me = obj.data

    # The bark's colour texture, before the materials are replaced.
    bark_tex = None
    for i in bark_i:
        m = me.materials[i]
        for n in (m.node_tree.nodes if m and m.node_tree else []):
            if n.type == "TEX_IMAGE" and n.image is not None and "diff" in n.image.name.lower() \
                    and cfg.get("bark_tex", "") in n.image.name:
                bark_tex = bpy.path.abspath(n.image.filepath, library=n.image.library)
                break
        if bark_tex:
            break

    # Materials: foliage renders as described; everything else is invisible.
    for i, m in enumerate(me.materials):
        if m is None:
            continue
        if i in fol_i:
            foliage_material(m, me.uv_layers.active.name if me.uv_layers.active else "UVMap")
        else:
            hide_material(m)

    scn = bpy.context.scene
    scn.render.engine = "CYCLES"
    scn.cycles.device = "CPU"
    scn.cycles.samples = 24
    scn.cycles.use_denoising = False
    scn.cycles.max_bounces = 0
    scn.cycles.transparent_max_bounces = 64
    scn.render.film_transparent = True
    scn.render.use_persistent_data = True
    scn.render.image_settings.file_format = "PNG"
    scn.render.image_settings.color_mode = "RGBA"
    scn.render.image_settings.color_depth = "8"
    scn.world = scn.world or bpy.data.worlds.new("w")
    scn.world.color = (1, 1, 1)
    cam_data = bpy.data.cameras.new("card_cam"); cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("card_cam", cam_data)
    scn.collection.objects.link(cam)
    scn.camera = cam
    vl = bpy.context.view_layer

    grid = int(math.ceil(math.sqrt(k)))
    tile = TILE_MAX // grid
    atlas_col = np.zeros((TILE_MAX, TILE_MAX, 4), dtype=np.float32)
    atlas_nrm = np.zeros((TILE_MAX, TILE_MAX, 3), dtype=np.float32)
    atlas_nrm[..., :] = (0.5, 0.5, 1.0)
    scn.render.resolution_x = scn.render.resolution_y = tile
    scn.render.resolution_percentage = 100
    tmp = os.path.join(bpy.app.tempdir or "/tmp", f"sf_card_{kind}.png")

    up = np.array([0.0, 0.0, 1.0])
    loop_lab = cid[face_of_loop]
    order = np.argsort(loop_lab, kind="stable")
    bounds = np.searchsorted(loop_lab[order], np.arange(-1, k + 1))
    cards = []
    for j in range(k):
        loops_j = order[bounds[j + 1]:bounds[j + 2]]
        if len(loops_j) < 9:
            continue
        p = co[np.unique(lv[loops_j])]
        c = p.mean(0)
        # The card faces out of the crown and up, the way the RTS camera looks at it.
        out = c - crown_c
        out[2] = 0.0
        ol = np.linalg.norm(out)
        out = out / ol if ol > 1e-4 else np.array([1.0, 0.0, 0.0])
        n = out * 1.0 + up * 0.9
        n /= np.linalg.norm(n)
        u = np.cross(up, n); u /= np.linalg.norm(u)
        v = np.cross(n, u)
        a = (p - c) @ u
        b = (p - c) @ v
        # The sphere the card shows: a little beyond the cluster's own spread.
        spread = float(np.sqrt(((a - a.mean()) ** 2 + (b - b.mean()) ** 2).mean()))
        half = max(0.5 * max(np.ptp(a), np.ptp(b)) * 0.8, spread * 1.9) * cfg.get("sphere", SPHERE) + 1e-3
        cc = c + u * a.mean() + v * b.mean()
        # The camera, orthographic, looking down -n with v up.
        rot = Matrix(((u[0], v[0], n[0]), (u[1], v[1], n[1]), (u[2], v[2], n[2]))).to_4x4()
        cam.matrix_world = Matrix.Translation(Vector(cc + n * 20.0)) @ rot
        cam_data.ortho_scale = 2.0 * half
        cam_data.clip_start = max(0.05, 20.0 - half - 0.05)
        cam_data.clip_end = 20.0 + half + 0.05
        vl["sf_centre"] = [float(x) for x in cc]
        vl["sf_radius"] = float(half)
        slot = len(cards)
        gx, gy = slot % grid, slot // grid
        y0 = gy * tile
        x0 = gx * tile
        for mode in (0, 1):
            vl["sf_mode"] = float(mode)
            scn.view_settings.view_transform = "Standard" if mode == 0 else "Raw"
            scn.render.filepath = tmp
            bpy.ops.render.render(write_still=True)
            img = read_render(tmp)
            if mode == 0:
                atlas_col[y0:y0 + tile, x0:x0 + tile] = img
            else:
                atlas_nrm[y0:y0 + tile, x0:x0 + tile] = img[..., :3]
        # Game space is (x, z, y) of Blender's: +Y up.
        g = lambda q: [round(float(q[0]), 4), round(float(q[2]), 4), round(float(q[1]), 4)]
        cc_s = (cc - origin) * scale
        cards.append({
            "c": g(cc_s), "u": g(u), "v": g(v), "n": g(n), "half": round(float(half * scale), 4),
            # The tile in UV (Unity's: v up from the bottom row), half a texel in.
            "uv": [round((x0 + 0.5) / TILE_MAX, 5), round((y0 + 0.5) / TILE_MAX, 5),
                   round((x0 + tile - 0.5) / TILE_MAX, 5), round((y0 + tile - 0.5) / TILE_MAX, 5)],
        })
        if j % 20 == 0:
            print(f"  card {j}/{k}  ({time.time() - t0:.0f} s)")

    # Atlases: colour and normals spread into the transparent texels (so mipmaps do
    # not blend edges toward black or flat), then each leaf's coverage grown by a
    # texel: thin leaves otherwise fall under the alpha cut-off as they shrink.
    alpha = atlas_col[..., 3]
    rgb = dilate(atlas_col[..., :3], alpha)
    nrm = dilate(atlas_nrm, alpha)
    nrm[nrm.sum(-1) < 0.05] = (0.5, 0.5, 1.0)
    grown = alpha.copy()
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        grown = np.maximum(grown, np.roll(np.roll(alpha, dy, 0), dx, 1) * GROW)
    col8 = (np.clip(np.concatenate([rgb, grown[..., None]], 2), 0, 1) * 255 + 0.5).astype(np.uint8)[::-1]
    nrm8 = (np.clip(nrm, 0, 1) * 255 + 0.5).astype(np.uint8)[::-1]
    os.makedirs(OUT_TEX, exist_ok=True)
    write_png(os.path.join(OUT_TEX, f"scan_{kind}_col.png"), col8)
    write_png(os.path.join(OUT_TEX, f"scan_{kind}_nrm.png"), nrm8)

    # ------------------------------------------------------------ trunk
    trunk = None
    bark_faces = np.nonzero(is_bark)[0]
    if len(bark_faces):
        bm = bmesh.new()
        vmap = {}
        uvl = bm.loops.layers.uv.new("UVMap")
        for f in bark_faces:
            idx = lv[ls[f]:ls[f] + lt[f]]
            vs = []
            for i in idx:
                if i not in vmap:
                    vmap[i] = bm.verts.new(Vector(co[i]))
                vs.append(vmap[i])
            try:
                face = bm.faces.new(vs)
            except ValueError:
                continue
            for lp, li in zip(face.loops, range(ls[f], ls[f] + lt[f])):
                lp[uvl].uv = luv[li]
        tm = bpy.data.meshes.new("trunk")
        bm.to_mesh(tm)
        bm.free()
        tobj = bpy.data.objects.new("trunk", tm)
        scn.collection.objects.link(tobj)
        tris = sum(len(p.vertices) - 2 for p in tm.polygons)
        if tris > cfg["trunk_tris"]:
            dec = tobj.modifiers.new("dec", "DECIMATE")
            dec.ratio = cfg["trunk_tris"] / tris
            dec.use_collapse_triangulate = True
            bpy.context.view_layer.objects.active = tobj
            dg = bpy.context.evaluated_depsgraph_get()
            ev = tobj.evaluated_get(dg)
            tm2 = bpy.data.meshes.new_from_object(ev)
        else:
            tm2 = tm
        tm2.calc_loop_triangles()
        tv = np.array([(np.array(v.co) - origin) * scale for v in tm2.vertices])
        uvd = tm2.uv_layers.active.data
        faces, fuv = [], []
        for t in tm2.loop_triangles:
            faces.append([int(i) for i in t.vertices])
            fuv.append([[round(float(uvd[l].uv[0]), 5), round(float(uvd[l].uv[1]), 5)] for l in t.loops])
        trunk = {"verts": [[round(float(q[0]), 4), round(float(q[2]), 4), round(float(q[1]), 4)] for q in tv],
                 "tris": faces, "uvs": fuv}
        # Its bark texture, as it is (the scan's UVs are kept for it).
        if bark_tex:
            import shutil
            ext = os.path.splitext(bark_tex)[1]
            for old in (".png", ".jpg"):
                p_ = os.path.join(OUT_TEX, f"scan_{kind}_bark{old}")
                if os.path.exists(p_):
                    os.remove(p_)
            shutil.copyfile(bark_tex, os.path.join(OUT_TEX, f"scan_{kind}_bark{ext}"))
            print(f"  bark texture {os.path.basename(bark_tex)}")
        print(f"  trunk {tris} -> {len(faces)} triangles")

    fol_s = (pts - origin) * scale
    flo, fhi = fol_s.min(0), fol_s.max(0)
    crown = {"center": [0.0, round(float((flo[2] + fhi[2]) / 2), 3), 0.0],
             "radii": [round(float(max(abs(flo[0]), abs(fhi[0]), abs(flo[1]), abs(fhi[1]))), 3),
                       round(float((fhi[2] - flo[2]) / 2), 3),
                       round(float(max(abs(flo[0]), abs(fhi[0]), abs(flo[1]), abs(fhi[1]))), 3)]}
    os.makedirs(OUT_JSON, exist_ok=True)
    with open(os.path.join(OUT_JSON, f"{kind}.json"), "w") as fh:
        json.dump({"kind": kind, "source": cfg["src"], "height": cfg["height"], "needles": cfg.get("needles", False),
                   "grid": grid, "crown": crown, "cards": cards, "trunk": trunk}, fh)
    print(f"{kind}: {len(cards)} cards in a {grid}x{grid} atlas of {tile} px tiles, crown {crown}, {time.time() - t0:.0f} s")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    kinds = argv or list(KINDS)
    for kind in kinds:
        bake(kind, KINDS[kind])


if __name__ == "__main__":
    main()
