"""check_palette_fbx.py -- compares staged FBXs with the ones they replace, face by face.

    Blender --background --factory-startup --python Tools/blender/check_palette_fbx.py -- <old_dir> <new_dir> [NAME ...]

Unity's imported meshes are not readable (only the ore's), so what the palette merge wrote can only
be read here. For every SF_*.fbx in <new_dir> whose old copy exists, object by object:

  * the same objects, with the same origins;
  * the same faces (centre and normal, to a tenth of a millimetre) on the same surface: a merged
    face's colour G names the palette entry the old face's slot was;
  * colour R (occlusion), B and A unchanged, and colour G unchanged on faces keeping their own
    material, on every corner;
  * the slots on the new mesh: `palette` and only `crystal`, `ore_glow`, `rock`, with no empty slot.

Prints one line per file that differs and a total. Target: 0 files off.
"""

import math
import os
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.dont_write_bytecode = True

import bpy

import sf_model as S

OWN_MATERIAL = ('crystal', 'ore_glow', 'rock')


def base_name(name):
    dot = name.find('.')
    return name[:dot] if dot > 0 else name


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def read(path):
    """{object name: (origin, Counter of (face key, surface), colour R by corner key, used slots, slots)}."""
    clear()
    bpy.ops.import_scene.fbx(filepath=path, colors_type='LINEAR', use_custom_normals=False)
    result = {}
    for ob in bpy.data.objects:
        if ob.type != 'MESH':
            continue
        me = ob.data
        names = [base_name(m.name) if m is not None else '' for m in me.materials]
        attr = me.color_attributes.get('Col') or (me.color_attributes[0] if len(me.color_attributes) else None)
        corner = attr is not None and attr.domain == 'CORNER'
        faces = Counter()
        colour = Counter()
        used = set()
        for p in me.polygons:
            slot = names[p.material_index] if p.material_index < len(names) else ''
            used.add(slot)
            c = p.center
            n = p.normal
            key = (round(c.x, 4), round(c.y, 4), round(c.z, 4), round(n.x, 2), round(n.y, 2), round(n.z, 2))
            col = None
            if attr is not None:
                li = p.loop_start
                col = attr.data[li if corner else me.loops[li].vertex_index].color
            corners = [] if col is None else [attr.data[l if corner else me.loops[l].vertex_index].color
                                              for l in range(p.loop_start, p.loop_start + p.loop_total)]
            # G and B on every corner of a face keeping its material (crystal's shard data).
            faces[(key, slot, tuple(sorted((round(v[1], 3), round(v[2], 3)) for v in corners)) if slot != 'palette' else None)] += 1
            if col is not None:
                total = []
                gs = set()
                for v in corners:
                    # R, B and A do not change on any face; G does, on merged ones only.
                    total.append((round(v[0], 3), round(v[2], 3), round(v[3], 3)))
                    gs.add(round(v[1] * 255.0))
                colour[(key, tuple(sorted(total)))] += 1
                if slot == 'palette':
                    if len(gs) != 1:
                        faces[(key, 'MIXED G', None)] += 1
                    else:
                        index = gs.pop()
                        name = S.MAT_NAMES[index] if 0 <= index < len(S.MAT_NAMES) else 'bad %d' % index
                        faces[(key, 'palette', None)] -= 1
                        faces[(key, name, 'merged')] += 1
        loc = tuple(round(x, 4) for x in ob.matrix_world.translation)
        result[base_name(ob.name)] = (loc, faces, colour, used, names)
    return result


def surface_counts(faces):
    """Faces by (key, slot), forgetting how the slot was told."""
    out = Counter()
    for (key, slot, how), n in faces.items():
        if n:
            out[(key, slot)] += n
    return out


def own_data(faces):
    return Counter({k: n for k, n in faces.items() if n and k[2] != 'merged' and k[1] in OWN_MATERIAL})


def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    old_dir, new_dir = argv[0], argv[1]
    only = set(argv[2:])
    files = sorted(f for f in os.listdir(new_dir) if f.startswith('SF_') and f.endswith('.fbx'))
    off = merged = same = 0
    slots_before = slots_after = 0
    for f in files:
        name = f[3:-4]
        if only and name not in only:
            continue
        old_path = os.path.join(old_dir, f)
        if not os.path.exists(old_path):
            print('%-24s no old copy' % name)
            off += 1
            continue
        old = read(old_path)
        new = read(os.path.join(new_dir, f))
        problems = []
        if set(old) != set(new):
            problems.append('objects differ: %s against %s' % (sorted(old), sorted(new)))
        for ob in sorted(set(old) & set(new)):
            o_loc, o_faces, o_col, o_used, o_names = old[ob]
            n_loc, n_faces, n_col, n_used, n_names = new[ob]
            if any(abs(a - b) > 1e-3 for a, b in zip(o_loc, n_loc)):
                problems.append('%s origin %s against %s' % (ob, o_loc, n_loc))
            so, sn = surface_counts(o_faces), surface_counts(n_faces)
            if so != sn:
                d = sum(((so - sn) + (sn - so)).values())
                ex = list((so - sn).items())[:2] + list((sn - so).items())[:2]
                problems.append('%s: %d faces differ of %d, e.g. %s' % (ob, d, sum(so.values()), ex))
            if own_data(o_faces) != own_data(n_faces):
                problems.append('%s: colour G/B changed on faces keeping their material' % ob)
            if o_col != n_col:
                d = sum(((o_col - n_col) + (n_col - o_col)).values())
                problems.append('%s: colour R, B or A differs on %d faces' % (ob, d))
            if any('palette' in new[o][4] for o in new):
                bad = [s for s in n_names if s != 'palette' and s not in OWN_MATERIAL]
                if bad:
                    problems.append('%s: slots still on the mesh %s' % (ob, bad))
                if set(n_names) - n_used:
                    problems.append('%s: empty slots %s' % (ob, sorted(set(n_names) - n_used)))
            slots_before += len(o_used)
            slots_after += len(n_used)
        was_merged = any('palette' in new[ob][4] for ob in new)
        merged += 1 if was_merged else 0
        same += 0 if was_merged else 1
        if problems:
            off += 1
            print('%-24s OFF: %s' % (name, ' | '.join(problems[:4])))
    print('checked %d files: %d merged, %d left as they were, %d off; slots drawn across objects %d -> %d' % (
        merged + same, merged, same, off, slots_before, slots_after))


if __name__ == '__main__':
    main()
