"""export_fbx.py -- exports every StarForge model to FBX for Unity.

    Blender --background --factory-startup --python Tools/blender/export_fbx.py -- <out_dir> [<blend_path>]

Reuses the authoring layer unchanged (sf_model.py + build_models.py, plus the
Unity-edition models in build_models_ext.py, the set dressing in build_env.py
and the trees in build_flora.py) and replaces the old binary pack
with one FBX per model, carrying:

  * one material slot per palette entry actually used, named after the
    palette (`armor`, `team`, `glow_cyan`, ...) so Unity can remap each slot
    to a URP material by name;
  * a box-projected UV set in object space, so tiling detail textures stay
    put as a unit turns;
  * hard edges wherever faces meet at more than SMOOTH_ANGLE, and fully flat
    shading on parts marked flat (rock), exported as normals;
  * baked ambient occlusion in vertex colour R, cast against the whole model
    and the ground, which the unit shader uses for occlusion and grime, and
    per-vertex shading data in G and B where a part sets it (sf_model.set_vdata);
  * movable part groups (Model.group) as child objects with their origin at
    the pivot: legs, gun, cutter arm, barrels, rotating heads.

Structures also get SF_<NAME>_CHUNKS.fbx: the same parts clustered into a
handful of pieces, each with its origin at its centre, which the game throws
when the building is destroyed.

It also writes models.json (radius and height per model, which the prefab
builder uses to size selection decals and health bars, plus groups and chunk
counts) and, optionally, a .blend with every model laid out for editing.
"""

import json
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import bmesh
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

import sf_model as S
import build_models as B
import build_models_ext as X
import build_env as E
import build_flora as F

SMOOTH_ANGLE = math.radians(38.0)
UV_SCALE = 0.5   # one texture repeat every two metres
AO_RAYS = 32

# Structures that break apart on death, and into how many pieces.
CHUNKED = {'FOUNDRY': 12, 'GARRISON': 10, 'WORKSHOP': 10, 'BUNKHOUSE': 7, 'SENTINEL_BASE': 6}

# Game space is Unity space: +Y up, +Z forward, left-handed. Blender's FBX
# exporter (axis_up=Y, axis_forward=-Z, baked) followed by Unity's importer
# maps Blender (X, Y, Z) to Unity (X, Z, Y) -- measured, not assumed: an
# earlier (-x, -z, y) write, derived from the documented handedness flip,
# imported every model rotated 180 degrees, facing -Z. So a game point
# (x, y, z) is written to Blender as (x, z, y). That swap is a reflection, so
# faces are reversed to stay outward-facing in Blender; Unity's import
# reflection restores the winding.
TO_BLENDER = Matrix(((1, 0, 0, 0),
                     (0, 0, 1, 0),
                     (0, 1, 0, 0),
                     (0, 0, 0, 1)))


def to_blender(p):
    return Vector((p[0], p[2], p[1]))


def preview_material(name):
    """A Blender material that previews the palette entry; Unity remaps it."""
    r, g, b, rough, metal, team, emis, ao = S.MATERIALS[name]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    if bsdf:
        bsdf.inputs['Base Color'].default_value = (r, g, b, 1.0)
        bsdf.inputs['Roughness'].default_value = rough
        bsdf.inputs['Metallic'].default_value = metal
        if emis > 0 and 'Emission Color' in bsdf.inputs:
            bsdf.inputs['Emission Color'].default_value = (r, g, b, 1.0)
            bsdf.inputs['Emission Strength'].default_value = emis
    mat.diffuse_color = (r, g, b, 1.0)
    return mat


def prepare(model):
    """Per part: box UVs, material slots, hard edges, then conversion to Blender
    axes. Returns (slot names, radius, height) in game space."""
    slots = []
    slot_of = {}
    rad = 0.0
    top = 0.0
    cos_lim = math.cos(SMOOTH_ANGLE)

    for part in model.parts:
        complaint = S.check_bounds(part)
        if complaint:
            raise RuntimeError('%s: part escaped its intended bounds -- %s' % (model.name, complaint))
        part.normal_update()
        lay = S._layer(part)
        flat = id(part) in S._FLAT
        uv = part.loops.layers.uv.get('UVMap') or part.loops.layers.uv.new('UVMap')

        for v in part.verts:
            rad = max(rad, math.hypot(v.co.x, v.co.z))
            top = max(top, v.co.y)

        for f in part.faces:
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for loop in f.loops:
                co = loop.vert.co
                if ax == 0:
                    loop[uv].uv = (co.z * UV_SCALE, co.y * UV_SCALE)
                elif ax == 1:
                    loop[uv].uv = (co.x * UV_SCALE, co.z * UV_SCALE)
                else:
                    loop[uv].uv = (co.x * UV_SCALE, co.y * UV_SCALE)
            name = S.MAT_NAMES[f[lay]]
            if name not in slot_of:
                slot_of[name] = len(slots)
                slots.append(name)
            f.material_index = slot_of[name]
            f.smooth = not flat

        for e in part.edges:
            if flat or len(e.link_faces) != 2:
                e.smooth = False
            else:
                a, b = e.link_faces
                e.smooth = a.normal.dot(b.normal) >= cos_lim

        bmesh.ops.transform(part, matrix=TO_BLENDER, verts=part.verts[:])
        # Normal hints are directions in game space: swap them into Blender's axes too.
        hint = [part.verts.layers.float.get(n) for n in S.NORMAL_HINT]
        if all(hint):
            for v in part.verts:
                v[hint[1]], v[hint[2]] = v[hint[2]], v[hint[1]]
        bmesh.ops.reverse_faces(part, faces=part.faces[:])
        part.normal_update()
    return slots, rad, top


def hemisphere(n):
    """Cosine-weighted directions around +Z, stratified in elevation."""
    rng = random.Random(7)
    dirs = []
    for i in range(n):
        u = (i + 0.5) / n
        phi = 2.0 * math.pi * rng.random()
        r = math.sqrt(u)
        dirs.append((r * math.cos(phi), r * math.sin(phi), math.sqrt(max(0.0, 1.0 - u))))
    return dirs


def bake_ao(model, radius):
    """Per-vertex ambient occlusion into a float colour layer (R), cast against
    every part of the model and the ground plane: recesses, the undersides of
    overhangs and the feet of a model standing on the ground darken. Nearer
    occluders count for more. Reach scales with the model, so a building's
    buttresses shade its walls while a trooper's straps do not black out."""
    whole = bmesh.new()
    for part in model.parts:
        tmp = bpy.data.meshes.new('ao_tmp')
        part.to_mesh(tmp)
        whole.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)
    bvh = BVHTree.FromBMesh(whole)
    reach = min(1.0, max(0.25, 0.18 * radius))
    dirs = hemisphere(AO_RAYS)

    for part in model.parts:
        col = part.verts.layers.float_color.get('Col') or part.verts.layers.float_color.new('Col')
        for v in part.verts:
            n = v.normal if v.normal.length > 1e-6 else Vector((0.0, 0.0, 1.0))
            t = n.orthogonal().normalized()
            b = n.cross(t)
            o = v.co + n * 0.01
            hits = 0.0
            for (dx, dy, dz) in dirs:
                d = t * dx + b * dy + n * dz
                loc, _, _, dist = bvh.ray_cast(o, d, reach)
                # Only close geometry shades: a hit at the far end of the reach
                # is nearly free, or every surface ends up half in shadow.
                if loc is not None:
                    hits += (1.0 - dist / reach) ** 2
                elif d.z < -1e-4:
                    tg = -o.z / d.z            # Blender Z is up: the ground plane
                    if 0.0 < tg < reach:
                        hits += (1.0 - tg / reach) ** 2
            ao = 1.0 - 0.85 * hits / len(dirs)
            v[col] = (ao, S.get_vdata(part, v, S.VDATA_G), S.get_vdata(part, v, S.VDATA_B), 1.0)
    whole.free()


def apply_normal_hints(me, blend=0.8):
    """Custom normals for vertices with a normal hint (sf_model.set_normal_hint):
    each corner's normal is turned most of the way toward the hint, so a leaf
    cluster shades as a rounded mass. Corners without a hint keep their normal,
    hard edges and all."""
    attrs = [me.attributes.get(n) for n in S.NORMAL_HINT]
    if not all(attrs):
        return
    hints = [Vector((attrs[0].data[i].value, attrs[1].data[i].value, attrs[2].data[i].value)) for i in range(len(me.vertices))]
    normals = []
    for corner, loop in zip(me.corner_normals, me.loops):
        n = Vector(corner.vector)
        h = hints[loop.vertex_index]
        if h.length > 0.5:
            n = n.lerp(h, blend).normalized()
        normals.append(n)
    me.normals_split_custom_set(normals)
    for n in S.NORMAL_HINT:
        me.attributes.remove(me.attributes[n])


def make_object(name, parts, slots, pivot=(0.0, 0.0, 0.0), parent=None, parent_pivot=(0.0, 0.0, 0.0)):
    """Merge `parts` into one mesh object whose origin sits at `pivot` (game space)."""
    combined = bmesh.new()
    shift = Matrix.Translation(-to_blender(pivot))
    for part in parts:
        tmp = bpy.data.meshes.new('tmp')
        part.to_mesh(tmp)
        tmp.transform(shift)
        combined.from_mesh(tmp)
        bpy.data.meshes.remove(tmp)

    me = bpy.data.meshes.new(name)
    combined.to_mesh(me)
    combined.free()
    for slot in slots:
        me.materials.append(bpy.data.materials.get(slot) or preview_material(slot))
    apply_normal_hints(me)
    if me.color_attributes.get('Col') is not None:
        me.color_attributes.active_color = me.color_attributes['Col']
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    obj.location = to_blender(pivot) - to_blender(parent_pivot)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    return obj, tris


def export(objs, path):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        mesh_smooth_type='FACE', use_mesh_modifiers=False, colors_type='LINEAR',
        add_leaf_bones=False, bake_anim=False, path_mode='STRIP')


def export_groups(model, slots, out_dir):
    """The body as the root object and each group as a child at its pivot."""
    by_group = {'': []}
    for part, g in zip(model.parts, model.part_groups):
        by_group.setdefault(g, []).append(part)

    root, tris = make_object(model.name, by_group[''], slots)
    objs = [root]
    placed = {'': (root, (0.0, 0.0, 0.0))}
    # Sorted, so a parent ('Arm') is always made before its child ('Arm/Cutter').
    for g in sorted(k for k in by_group if k):
        parent_name, _, leaf = g.rpartition('/')
        parent, parent_pivot = placed[parent_name]
        obj, t = make_object(leaf, by_group[g], slots, model.groups[g], parent, parent_pivot)
        placed[g] = (obj, model.groups[g])
        objs.append(obj)
        tris += t
    export(objs, os.path.join(out_dir, 'SF_%s.fbx' % model.name))
    return objs, tris


def export_chunks(model, slots, count, out_dir):
    """Pre-cut debris: parts clustered by position (k-means on part centres) into
    `count` pieces, each with its origin at its centre."""
    centres = []
    for p in model.parts:
        c = Vector()
        for v in p.verts:
            c += v.co
        centres.append(c / max(1, len(p.verts)))
    k = min(count, len(centres))
    rng = random.Random(len(centres))
    means = [centres[i].copy() for i in rng.sample(range(len(centres)), k)]
    buckets = []
    for _ in range(16):
        buckets = [[] for _ in range(k)]
        for i, c in enumerate(centres):
            j = min(range(k), key=lambda j: (c - means[j]).length_squared)
            buckets[j].append(i)
        for j in range(k):
            if buckets[j]:
                s = Vector()
                for i in buckets[j]:
                    s += centres[i]
                means[j] = s / len(buckets[j])

    objs = []
    for j, bucket in enumerate(b for b in buckets if b):
        m = means[buckets.index(bucket)]
        pivot = (m.x, m.z, m.y)   # Blender back to game space
        obj, _ = make_object('Chunk%d' % j, [model.parts[i] for i in bucket], slots, pivot)
        objs.append(obj)
    export(objs, os.path.join(out_dir, 'SF_%s_CHUNKS.fbx' % model.name))
    return objs


def export_lod(src, name, ratio, out_dir):
    """A decimated copy of a single-object model for distant instances:
    SF_<NAME>_LOD1.fbx, with the same slots, vertex colours and origin."""
    tmp = src.copy()
    tmp.data = src.data.copy()
    bpy.context.scene.collection.objects.link(tmp)
    mod = tmp.modifiers.new('decimate', 'DECIMATE')
    mod.ratio = ratio
    mod.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    bpy.data.objects.remove(tmp)
    lod = bpy.data.objects.new(name + '_LOD1', me)
    bpy.context.scene.collection.objects.link(lod)
    lod.location = src.location
    export([lod], os.path.join(out_dir, 'SF_%s_LOD1.fbx' % name))
    tris = sum(len(poly.vertices) - 2 for poly in me.polygons)
    bpy.data.objects.remove(lod)
    return tris


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out_dir = argv[0] if argv else os.path.join(os.getcwd(), 'Assets', 'StarForge', 'Art', 'Models')
    blend_path = argv[1] if len(argv) > 1 else None
    os.makedirs(out_dir, exist_ok=True)

    bpy.ops.wm.read_factory_settings(use_empty=True)

    meta = {}
    builders = B.BUILDERS + X.EXT_BUILDERS + E.ENV_BUILDERS + F.FLORA_BUILDERS
    # Build every model before exporting any. sf_model keys its bounds and
    # flat-shading records by id(bmesh); letting one model's parts be freed
    # while the next is built lets Python reuse those ids, and a new part then
    # inherits a stale bounds record and fails the check for no real reason.
    models = [fn() for fn in builders]
    for i, model in enumerate(models):
        slots, rad, top = prepare(model)
        bake_ao(model, rad)
        objs, tris = export_groups(model, slots, out_dir)
        chunks = export_chunks(model, slots, CHUNKED[model.name], out_dir) if model.name in CHUNKED else []
        lod_tris = export_lod(objs[0], model.name, F.LOD_RATIO[model.name], out_dir) if model.name in F.LOD_RATIO else 0

        # Object names are global in a .blend: rename after export so the next
        # model's 'Head' or 'Chunk0' exports under its own clean name, and lay
        # the models out in a row for browsing.
        for o in objs[1:]:
            o.name = '%s_%s' % (model.name, o.name)
        for o in chunks:
            o.name = '%s_%s' % (model.name, o.name)
            o.location += Vector((i * 7.0, 12.0, 0.0))
        objs[0].location.x = i * 7.0

        meta[model.name] = {'radius': round(rad, 3), 'height': round(top, 3), 'triangles': tris,
                            'materials': slots,
                            'groups': {g: [round(c, 3) for c in p] for g, p in model.groups.items()},
                            'chunks': len(chunks), 'lod1_triangles': lod_tris}
        meta[model.name].update(model.meta)
        print('%-16s tris=%6d radius=%5.2f height=%5.2f groups=%s chunks=%d lod1=%d  %s' % (
            model.name, tris, rad, top, ','.join(sorted(model.groups)) or '-', len(chunks), lod_tris, ','.join(slots)))

    meta['_mounts'] = {'SENTINEL_HEAD_Y': X.SENTINEL_HEAD_MOUNT_Y}
    with open(os.path.join(out_dir, 'models.json'), 'w') as fh:
        json.dump(meta, fh, indent=2)

    if blend_path:
        bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    print('exported %d models to %s' % (len(builders), out_dir))


if __name__ == '__main__':
    main()
