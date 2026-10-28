"""build_creatures.py -- the Biolab's creatures, made monstrous from Quaternius's animals.

    Blender --background --factory-startup --python Tools/blender/build_creatures.py -- <out_dir> [preview_dir]

Each creature starts as one of Quaternius's CC0 animated animals (Art/Source/quaternius,
fetched by Tools/fetch_assets.py) and is given what makes it a monster: horns, tusks and
mandibles, spines down its back, armour plates, a glowing venom sac. The new parts are built
from primitives here, joined into the animal's skinned mesh and weighted wholly to the bone
they grow from, so they move with every one of its cycles. Three material slots are added
after the animal's own, always in this order -- Horn, Plate, Glow -- and the result is
exported with every action as Art/Fauna/Creature_<Name>.fbx, which Editor/PrefabBuilder.cs
colours per team by slot.

    SKITTER     the spider: fangs, a crest, a row of spines on the abdomen, a venom tip
    SPITTER     the frog: a glowing acid sac on its back, spines, horns and tusks
    RAZORWING   the wasp: mandibles, a spiked crest and thorax, a long scythe of a sting
    GOREHORN    the cow: great forward tusks, back-swept horns, a ridge of armour plates
                down its spine, shoulder plates and a spiked tail club

Positions are Blender world space of the imported file (Z up); each creature's head is
where its Head bone points (the spider, frog and cow face -Y, the wasp +X).
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, 'Art', 'Source', 'quaternius')
EASY = os.path.join(SRC, 'Easy_Enemy_Pack', 'Easy Animated Enemy Pack - Jan 2019', 'FBX')
FARM = os.path.join(SRC, 'Farm_Animals_by_Quaternius', 'Farm Animals by @Quaternius', 'FBX')

SLOTS = ('Horn', 'Plate', 'Glow')
HORN, PLATE, GLOW = 0, 1, 2


# ---------------------------------------------------------------- shapes
class Parts:
    """Geometry for one creature: faces carry a slot (Horn/Plate/Glow), vertices a bone."""

    def __init__(self):
        self.bm = bmesh.new()
        self.vbone = []          # bone name per vertex, in creation order

    def _take(self, verts, bone):
        for _ in verts:
            self.vbone.append(bone)

    def horn(self, bone, base, d0, d1, length, radius, seg=6, steps=6, slot=HORN):
        """A tapering curved horn from `base`, leaving along `d0` and ending along `d1`."""
        d0 = Vector(d0).normalized()
        d1 = Vector(d1).normalized()
        base = Vector(base)
        rings = []
        p = base.copy()
        for i in range(steps + 1):
            t = i / steps
            d = (d0 * (1 - t) + d1 * t).normalized()
            if i > 0:
                p = p + d * (length / steps)
            r = radius * (1.0 - t) ** 0.9 + 0.004
            # A ring perpendicular to the direction at this point.
            side = d.cross(Vector((0, 0, 1)))
            if side.length < 1e-3:
                side = d.cross(Vector((1, 0, 0)))
            side.normalize()
            up = side.cross(d).normalized()
            ring = []
            for k in range(seg):
                a = 2 * math.pi * k / seg
                ring.append(self.bm.verts.new(p + (side * math.cos(a) + up * math.sin(a)) * r))
            rings.append(ring)
        tip = self.bm.verts.new(p + (d0 * 0.0 + d1).normalized() * radius * 0.6)
        for i in range(steps):
            a, b = rings[i], rings[i + 1]
            for k in range(seg):
                f = self.bm.faces.new((a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k]))
                f.material_index = slot
        for k in range(seg):
            f = self.bm.faces.new((rings[-1][k], rings[-1][(k + 1) % seg], tip))
            f.material_index = slot
        f = self.bm.faces.new(list(reversed(rings[0])))
        f.material_index = slot
        self._take([v for r in rings for v in r] + [tip], bone)

    def spike(self, bone, base, d, length, radius, slot=HORN):
        self.horn(bone, base, d, d, length, radius, seg=5, steps=2, slot=slot)

    def blob(self, bone, center, size, axes=None, slot=PLATE, u=10, v=6):
        """A squashed sphere: `size` its half-extents along `axes` (default X, Y, Z)."""
        res = bmesh.ops.create_uvsphere(self.bm, u_segments=u, v_segments=v, radius=1.0)
        verts = res['verts']
        ax = axes or (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)))
        m = Matrix((tuple(ax[0] * size[0]), tuple(ax[1] * size[1]), tuple(ax[2] * size[2]))).transposed()
        for vt in verts:
            vt.co = Vector(center) + m @ vt.co
        faces = {f for vt in verts for f in vt.link_faces}
        for f in faces:
            f.material_index = slot
        self._take(verts, bone)


# ---------------------------------------------------------------- the animal
def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    me = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    return me, arm


def bone_head(arm, name):
    return arm.matrix_world @ arm.data.bones[name].head_local


def bone_tail(arm, name):
    return arm.matrix_world @ arm.data.bones[name].tail_local


def surface_top(me, x, y, r=0.25):
    """The highest point of the animal's skin over (x, y) (its back), world space."""
    W = me.matrix_world
    best = None
    for v in me.data.vertices:
        p = W @ v.co
        if (p.x - x) ** 2 + (p.y - y) ** 2 <= r * r and (best is None or p.z > best.z):
            best = p
    return best if best is not None else Vector((x, y, 0))


def material(name, color, emit=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1.0)
    return m


def join(me, parts):
    """Join the parts into the animal's skinned mesh, each vertex weighted wholly to its bone."""
    # Every part must hang on a bone the animal has (else it exports unskinned), and our slot
    # names must not clash with the animal's own (else join() would merge them).
    arm = me.find_armature()
    bones = set(arm.data.bones.keys()) if arm else set()
    missing = sorted(set(parts.vbone) - bones)
    assert not missing, '%s: parts on bones the rig lacks: %s' % (me.name, missing)
    own = [s.material.name for s in me.material_slots if s.material]
    assert not set(SLOTS) & set(own), '%s: the animal already has a slot named %s' % (me.name, set(SLOTS) & set(own))
    mesh = bpy.data.meshes.new('MonsterParts')
    parts.bm.to_mesh(mesh)
    parts.bm.free()
    obj = bpy.data.objects.new('MonsterParts', mesh)
    bpy.context.scene.collection.objects.link(obj)
    for name in SLOTS:
        obj.data.materials.append(material(name, (0.5, 0.5, 0.5)))
    groups = {}
    for i, b in enumerate(parts.vbone):
        if b not in groups:
            groups[b] = obj.vertex_groups.new(name=b)
        groups[b].add([i], 1.0, 'REPLACE')
    # The animal's own slots stay first, ours after in SLOTS order (join appends what is new).
    for name in SLOTS:
        if name not in [s.material.name for s in me.material_slots if s.material]:
            me.data.materials.append(bpy.data.materials[name])
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    me.select_set(True)
    bpy.context.view_layer.objects.active = me
    bpy.ops.object.join()
    return me


def export(me, arm, out):
    bpy.ops.object.select_all(action='DESELECT')
    me.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE', 'MESH'},
                             add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
                             bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                             path_mode='STRIP', use_armature_deform_only=False)


def mirrored(fn, *args):
    """Call fn for a part and its mirror across X (left and right)."""
    fn(1, *args)
    fn(-1, *args)


# ---------------------------------------------------------------- the creatures
def skitter():
    me, arm = load(os.path.join(EASY, 'Spider.fbx'))
    P = Parts()
    head = bone_head(arm, 'Head')
    # Fangs: two curved hooks down and forward from under the eyes.
    for s in (1, -1):
        P.horn('Head', (0.17 * s, -0.42, 0.55), (0.15 * s, -1, -0.35), (0.0, -0.3, -1), 0.75, 0.10)
    # A crest of horns swept back over the carapace.
    for s in (1, -1):
        P.horn('Head', (0.22 * s, -0.10, 1.05), (0.3 * s, 0.4, 1), (0.2 * s, 1, 0.25), 0.85, 0.11)
    P.horn('Head', (0.0, -0.05, 1.12), (0, 0.2, 1), (0, 1, 0.4), 1.0, 0.12)
    # Spines down the abdomen, longest in the middle, and a glowing venom tip at its end.
    for i, y in enumerate((0.9, 1.25, 1.6, 1.95, 2.25)):
        top = surface_top(me, 0.0, y, 0.3)
        l = 0.55 + 0.35 * math.sin(math.pi * (i + 0.5) / 5)
        P.spike('Abdomen', top - Vector((0, 0, 0.06)), (0, 0.35, 1), l, 0.13)
        for s in (1, -1):
            side = surface_top(me, 0.35 * s, y, 0.25)
            P.spike('Abdomen', side - Vector((0, 0, 0.08)), (0.8 * s, 0.25, 0.6), l * 0.55, 0.08)
    tip = bone_tail(arm, 'Abdomen')
    P.blob('Abdomen', tip + Vector((0, 0.18, 0.15)), (0.22, 0.30, 0.20), slot=GLOW)
    # Plates over the thorax.
    P.blob('Thorax', surface_top(me, 0.0, 0.45, 0.3) - Vector((0, 0, 0.05)), (0.42, 0.34, 0.10))
    return me, arm, P


def spitter():
    me, arm = load(os.path.join(EASY, 'Frog.fbx'))
    P = Parts()
    # The acid sac: a great glowing swelling on its back, ringed by spines.
    back = surface_top(me, 0.0, 0.35, 0.35)
    P.blob('Torso', back + Vector((0, 0.05, 0.22)), (0.48, 0.55, 0.42), slot=GLOW)
    for k in range(8):
        a = 2 * math.pi * k / 8
        d = Vector((math.cos(a), math.sin(a), 0.0))
        base = back + Vector((0, 0.05, 0.12)) + d * 0.5
        P.spike('Torso', base, (d.x * 0.7, d.y * 0.7, 1.0), 0.45, 0.08)
    # Spines down the spine behind the sac and over the hips.
    for y in (0.75, 0.95, 1.12):
        P.spike('Hips', surface_top(me, 0.0, y, 0.25) - Vector((0, 0, 0.04)), (0, 0.6, 1), 0.38, 0.08)
    # Back-swept horns on the head, and tusks out of the jaw.
    for s in (1, -1):
        P.horn('Head', (0.22 * s, -1.05, 1.32), (0.4 * s, 0.2, 1), (0.25 * s, 1, 0.3), 0.75, 0.10)
        P.horn('Head', (0.30 * s, -1.42, 0.80), (0.3 * s, -1, -0.2), (0.1 * s, -0.4, 1), 0.55, 0.08)
    # Warty plates on its flanks.
    for s in (1, -1):
        P.blob('Torso', (0.62 * s, 0.10, 0.80), (0.10, 0.36, 0.22))
    return me, arm, P


def razorwing():
    me, arm = load(os.path.join(EASY, 'Wasp.fbx'))
    P = Parts()
    head = bone_head(arm, 'Head')
    # Mandibles: two curved blades forward from the face.
    for s in (1, -1):
        P.horn('Head', (1.42, 0.16 * s, 2.55), (1, 0.4 * s, -0.4), (0.3, -0.6 * s, -0.6), 0.6, 0.07)
    # A crest of spikes swept back over the head and thorax.
    for i, x in enumerate((1.15, 0.85, 0.55, 0.25)):
        z = 3.18 - 0.06 * i
        P.spike('Head' if i < 2 else 'Thorax', (x, 0.03, z), (-0.6, 0, 1), 0.5 + 0.08 * i, 0.07)
    # The sting becomes a long hooked scythe.
    st = bone_tail(arm, 'Sting')
    P.horn('Sting', st, (0.4, 0, -1), (1, 0, 0.6), 0.95, 0.11)
    # Spines along the abdomen's back.
    for b in ('Abdomen', 'Abdomen2', 'Abdomen3'):
        h, t = bone_head(arm, b), bone_tail(arm, b)
        mid = (h + t) * 0.5
        P.spike(b, mid + Vector((-0.42, 0.0, 0.05)), (-1, 0, 0.35), 0.4, 0.07)
    # A glowing venom gland under the thorax.
    P.blob('Thorax', (0.40, 0.03, 2.30), (0.18, 0.18, 0.14), slot=GLOW)
    return me, arm, P


def gorehorn():
    me, arm = load(os.path.join(FARM, 'Cow.fbx'))
    P = Parts()
    hh, ht = bone_head(arm, 'Head'), bone_tail(arm, 'Head')
    # Great tusks out of the jaw, curving forward and up: the silhouette.
    for s in (1, -1):
        P.horn('Head', (0.55 * s, -4.55, 3.95), (0.35 * s, -1, -0.35), (0.1 * s, -0.4, 1), 2.6, 0.32, seg=8, steps=8)
    # Horns swept back and out from the crown.
    for s in (1, -1):
        P.horn('Head', (0.45 * s, -3.85, 4.95), (0.8 * s, 0.1, 0.6), (0.4 * s, 1, 0.5), 1.8, 0.24, seg=7, steps=7)
    # A ridge of armour plates and spikes down the spine, biggest over the shoulders.
    ridge = [(-3.0, 'Neck', 0.6), (-2.2, 'Shoulders', 1.0), (-1.4, 'Torso', 0.95), (-0.6, 'Torso', 0.8),
             (0.2, 'Hips', 0.75), (1.0, 'Hips', 0.7), (1.8, 'Back', 0.6), (2.5, 'Back', 0.5)]
    for y, b, k in ridge:
        top = surface_top(me, 0.0, y, 0.4)
        P.blob(b, top + Vector((0, 0, 0.05)), (0.55 * k + 0.25, 0.40, 0.18))
        P.spike(b, top + Vector((0, 0, 0.12)), (0, 0.45, 1), 1.1 * k + 0.2, 0.22 * k + 0.06)
    # A carapace over its back and down its flanks, in overlapping shells (front, middle,
    # rear), each ridged with spikes along its lower edge: it no longer reads as a hide.
    for y, b, wide in ((-2.0, 'Shoulders', 1.0), (-0.5, 'Torso', 1.0), (1.2, 'Hips', 0.92)):
        top = surface_top(me, 0.0, y, 0.5)
        P.blob(b, top + Vector((0, 0, -0.35)), (1.35 * wide, 0.95, 0.75), u=12, v=8)
        for s in (1, -1):
            for dy in (-0.45, 0.35):
                P.spike(b, (1.25 * wide * s, y + dy, top.z - 0.75), (1 * s, 0.1, -0.15), 0.55, 0.10)
    # A crest of short spikes over the brow.
    for s in (-1, 0, 1):
        P.spike('Head', (0.28 * s, -4.15, 5.05), (0.35 * s, 0.5, 1), 0.7, 0.12)
    # A spiked club at the end of its tail.
    tt = bone_tail(arm, 'Tail4')
    P.blob('Tail4', tt, (0.32, 0.32, 0.38))
    for k in range(6):
        a = 2 * math.pi * k / 6
        d = Vector((math.cos(a), 0.4, math.sin(a)))
        P.spike('Tail4', tt + d.normalized() * 0.25, d, 0.42, 0.08)
    # Glowing eyes over the old ones (the head plate below hides the cow's face).
    for s in (1, -1):
        P.blob('Head', (0.62 * s, -4.30, 4.55), (0.14, 0.14, 0.10), slot=GLOW)
    # An armoured brow plate over the face.
    P.blob('Head', (0.0, -4.35, 4.80), (0.75, 0.55, 0.25))
    return me, arm, P


CREATURES = {'Creature_Skitter': skitter, 'Creature_Spitter': spitter,
             'Creature_Razorwing': razorwing, 'Creature_Gorehorn': gorehorn}


def preview(path):
    """A quick Workbench render from the front three-quarter, for checking the shapes."""
    scene = bpy.context.scene
    objs = [o for o in scene.objects if o.type == 'MESH']
    lo = Vector((1e9, 1e9, 1e9)); hi = -lo
    for o in objs:
        for c in o.bound_box:
            p = o.matrix_world @ Vector(c)
            lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
            hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
    c = (lo + hi) * 0.5
    size = (hi - lo).length
    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    scene.collection.objects.link(cam)
    cam.location = c + Vector((size * 0.9, -size * 0.9, size * 0.6))
    cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'MATERIAL'
    scene.render.resolution_x = scene.render.resolution_y = 512
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out_dir = argv[0] if argv else os.path.join(ROOT, 'Assets', 'StarForge', 'Art', 'Fauna')
    prev_dir = argv[1] if len(argv) > 1 else None
    os.makedirs(out_dir, exist_ok=True)
    for name, fn in CREATURES.items():
        me, arm, parts = fn()
        nverts = len(parts.vbone)
        join(me, parts)
        tris = sum(len(p.vertices) - 2 for p in me.data.polygons)
        slots = [s.material.name for s in me.material_slots]
        if prev_dir:
            # Colour the preview by slot so the new parts stand out.
            cols = {'Horn': (0.85, 0.8, 0.65), 'Plate': (0.25, 0.2, 0.3), 'Glow': (0.4, 1.0, 0.3)}
            for s in me.material_slots:
                s.material.diffuse_color = (*cols.get(s.material.name, (0.45, 0.35, 0.35)), 1.0)
            preview(os.path.join(prev_dir, name + '.png'))
        export(me, arm, os.path.join(out_dir, name + '.fbx'))
        print('%-20s +%5d verts  %6d tris  slots %s' % (name, nverts, tris, ', '.join(slots)))


if __name__ == '__main__':
    main()
