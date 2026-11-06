"""build_soldier_rig.py -- a skeleton under the Trooper, with KayKit's clips on it.

    Blender --background --factory-startup --python Tools/blender/build_soldier_rig.py -- preview <png> [front|side]
    Blender --background --factory-startup --python Tools/blender/build_soldier_rig.py -- export <out_dir>
    Blender --background --factory-startup --python Tools/blender/build_soldier_rig.py -- check

The soldier stays the model build_models.py makes: its parts are bound rigidly, each to
one bone, so armour plates never stretch and a vertex costs one bone. The skeleton is
KayKit's Rig_Medium with its bones moved to the Trooper's proportions and their directions
and rolls left alone, so a clip's local rotations apply as they are; only the hips' travel
is scaled, by the difference in leg length. The arms are modelled hanging and the rig's
rest pose is KayKit's T, so the arm parts are turned into the T for binding.

KayKit gives the legs, hips and trunk. Its mannequin's arms suit a huge head on short
limbs, so the upper body is ours: a Hold says where the rifle sits against the chest and
where it points, and the arms are solved each frame to reach it (or to where the source's
hands are, scaled to this wider chest, for a hand that is not on the rifle). The rifle is
its own bone under the chest, keyed in every clip, so it can change hands.

Space: export_fbx writes a game point (x, y, z) to Blender as (x, z, y). Here the parts
then turn 180 degrees about Blender's Z, to (-x, -z, y): KayKit's own orientation (facing
-Y), which the FBX writer with no space baking takes to Unity's +Z forward.

Source: Art/Source/kaykit (KayKit Character Animations, CC0; Tools/fetch_assets.py).
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
import bmesh
from mathutils import Matrix, Quaternion, Vector

import build_models as B
import build_roster as R
import export_fbx as X
from sf_model import box, sphere

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
KAYKIT = os.path.join(ROOT, 'Art', 'Source', 'kaykit', 'KayKit_Character_Animations',
                      'KayKit_Character_Animations_1.1', 'Animations', 'gltf', 'Rig_Medium')
PACKS = ('General', 'MovementBasic', 'MovementAdvanced', 'CombatRanged', 'CombatMelee', 'Simulation')
FPS = 24

LOWER = ('root', 'hips', 'upperleg.l', 'lowerleg.l', 'foot.l', 'toes.l',
         'upperleg.r', 'lowerleg.r', 'foot.r', 'toes.r')
UPPER = ('spine', 'chest', 'head', 'upperarm.l', 'lowerarm.l', 'wrist.l', 'hand.l',
         'upperarm.r', 'lowerarm.r', 'wrist.r', 'hand.r')
BONES = LOWER + UPPER

# How much further the Trooper's hips travel than KayKit's: joint heights 0.80 and 0.519.
LEG_RATIO = 0.80 / 0.519

# Where the arms are modelled, game space (x on the right): shoulder, elbow, wrist.
SHOULDER = (0.47, 1.40, 0.00)
ELBOW = (0.47, 1.07, 0.05)
WRIST = (0.45, 0.90, 0.22)
# The forearm is drawn out along its own block at binding: modelled short, it could not reach a rifle held in
# front of the chest from these shoulders.
FOREARM_STRETCH = 1.8
FOREARM = 0.40                 # elbow to hand
# Where each side arm hangs, game space. Which clips have it in his right hand; a grenade leaves it
# partway through the throw (u), and the world's throw lets go at about the same moment.
SIDE_ARMS = {'Pistol': (0.31, 1.03, 0.16), 'Knife': (-0.17, 1.36, 0.23), 'Grenade': (0.0, 1.30, 0.0)}
IN_HAND = {'Stab': 'Knife', 'Throw': 'Grenade', 'PistolAim': 'Pistol', 'PistolFire': 'Pistol',
           'CrouchPistol': 'Pistol', 'RunPistol': 'Pistol'}
THROW_RELEASE = 0.42
# The pistol held out, against the chest but level whatever the trunk does (right, up, forward).
PISTOL_AT = (0.36, 0.24, 0.70)
# The top of the forearm's block as modelled, which goes on the elbow (game space).
FOREARM_TOP = (0.45, 1.105, 0.16)
# The rifle, game space: the point that goes in the right hand; the left hand's, ahead of it.
GRIP = (0.30, 1.12, 0.22)
FOREGRIP = (0.30, 1.10, 0.46)
# Where a free hand goes, against the source's, relative to the chest: across, forward, up.
HAND_SCALE = (1.35, 1.7, 1.5)
# From this share of an arm's length the reach is eased toward straight, not cut off there: at
# a hard limit the elbow snaps straight in one frame when the hand passes it.
SOFT_REACH = 0.88
# Which way the elbows hang when the hands are on the rifle (right, up, forward).
POLE = {'r': (0.7, -1.0, -0.2), 'l': (-0.8, -1.0, -0.2)}
POLE_LEAN = 0.08


def g2b(p):
    """A game point in the rig's Blender space."""
    return Vector((-p[0], -p[2], p[1]))


def bone_heads():
    """Each bone's head in the Trooper, game space (x on the right; .l is x < 0). The arm
    bones are laid out from the shoulder, in the T."""
    h = {'root': (0, 0, 0), 'hips': (0, 0.64, 0), 'spine': (0, 0.94, 0),
         'chest': (0, 1.18, -0.01), 'head': (0, 1.64, 0)}
    for n, s in (('l', -1), ('r', 1)):
        h['upperleg.' + n] = (0.27 * s, 0.80, 0.00)
        h['lowerleg.' + n] = (0.27 * s, 0.40, 0.00)
        h['foot.' + n] = (0.27 * s, 0.13, 0.02)
        h['toes.' + n] = (0.27 * s, 0.04, 0.20)
    return h


# ------------------------------------------------------------ the rifle's holds
def hold(grip=(0.14, 0.0, 0.30), yaw=0.0, pitch=0.0, follow=1.0, twist=0.0, right='grip', left='fore'):
    """`grip`: right hand's place on the rifle against the chest (right, up, forward), or
    'hand.r' to go where the source's right hand goes. `yaw`/`pitch`: where it points,
    degrees right and up. `follow`: how much it sways with the chest (0 stays level on the
    target). `twist`: degrees the trunk turns toward the rifle, the head staying ahead.
    `right`/`left`: 'grip', 'fore' or 'mapped' (the source's hand, scaled)."""
    return dict(grip=grip, yaw=yaw, pitch=pitch, follow=follow, twist=twist, right=right, left=left)


READY = hold((0.00, -0.08, 0.26), yaw=-26, pitch=-20, twist=20)
# Bent forward the chest takes the rifle with it: most of that is left out, or it digs the ground.
LOW_READY = hold((0.08, -0.04, 0.32), yaw=-22, pitch=-10, follow=0.3, twist=18)
PORT = hold((0.18, -0.06, 0.28), yaw=-46, pitch=18, twist=6)
AIM = hold((0.08, 0.10, 0.34), follow=0.0, twist=36)
CRAWL = hold('hand.r', pitch=-4, follow=0.0, left='mapped')
PRONE = hold((0.06, 0.30, 0.12), follow=0.0, twist=0.0)
CARRY = hold((-0.34, -0.42, 0.16), yaw=-24, pitch=-58, right='mapped', left='grip')
PISTOL = dict(CARRY, right='pistol', recoil=0.0)


def fire(u):
    k = max(0.0, 1.0 - u * 2.6)
    return hold((0.08, 0.10 + 0.012 * k, 0.34 - 0.07 * k), pitch=4.0 * k, follow=0.0, twist=36)


def prone_reload(u):
    h = dict(PRONE)
    h['left_path'] = u
    return h


def pistol_fire(u):
    return dict(PISTOL, recoil=max(0.0, 1.0 - u * 2.6))


def prone_fire(u):
    k = max(0.0, 1.0 - u * 2.6)
    return hold((0.06, 0.30 + 0.010 * k, 0.12 - 0.05 * k), pitch=3.0 * k, follow=0.0, twist=0.0)


def reload_(u):
    h = hold((0.12, 0.0, 0.26), yaw=-12, pitch=28, twist=18)
    h['left_path'] = u
    return h


# Name, source of the legs and trunk, the hold, seconds (None: the source's), source held at
# that point of its cycle (None: it plays; 'feet': where the feet are closest), loops.
SPECS = [
    ('Idle',       'Idle_A',               READY, None, None, True),
    ('Walk',       'Walking_A',            READY, None, None, True),
    ('Run',        'Running_A',            PORT,  None, None, True),
    ('Sneak',      'Sneaking',             LOW_READY, None, None, True),
    ('CrouchWalk', 'Crouching',            LOW_READY, None, None, True),
    ('CrouchIdle', 'Crouching',            LOW_READY, 1.0, 'feet', True),
    ('CrouchAim',  'Crouching',            AIM,   1.0,  'feet', True),
    ('Crawl',      'Crawling',             CRAWL, None, None, True),
    ('ProneAim',   'Crawling',             PRONE, 1.0,  0.0,  True),
    ('Aim',        'Idle_A',               AIM,   None, None, True),
    ('Fire',       'Idle_A',               fire,  0.35, 0.0,  False),
    ('CrouchFire', 'Crouching',            fire,  0.35, 'feet', False),
    ('ProneFire',  'Crawling',             prone_fire, 0.35, 0.0, False),
    ('Reload',     'Idle_A',               reload_, 2.0, 0.0, False),
    ('Throw',      'Throw',                CARRY, None, None, False),
    ('Stab',       'Melee_1H_Attack_Stab', CARRY, None, None, False),
    ('Dive',       'Dodge_Forward',        PORT,  None, None, False),
    ('Vault',      'Jump_Full_Short',      PORT,  None, None, False),
    ('Hit',        'Hit_A',                READY, None, None, False),
    # Reloading low, and the pistol: the rifle in the left hand, the right arm held out to the line of fire.
    ('CrouchReload', 'Crouching',          reload_, 2.0, 'feet', False),
    ('ProneReload',  'Crawling',           prone_reload, 2.0, 0.0, False),
    ('PistolAim',    'Idle_A',             PISTOL, None, None, True),
    ('PistolFire',   'Idle_A',             pistol_fire, 0.4, 0.0, False),
    ('CrouchPistol', 'Crouching',          PISTOL, 1.0, 'feet', True),
    ('RunPistol',    'Running_A',          PISTOL, None, None, True),
]


# ------------------------------------------------------------ the source
def load_sources():
    """Import the packs; keep one armature and every action. Returns (armature, actions)."""
    arm = None
    for pack in PACKS:
        bpy.ops.import_scene.gltf(filepath=os.path.join(KAYKIT, 'Rig_Medium_%s.glb' % pack))
        for o in list(bpy.data.objects):
            if o.type == 'ARMATURE' and arm is None:
                arm = o
            elif o is not arm:
                bpy.data.objects.remove(o, do_unlink=True)
    arm.name = 'Soldier'
    arm.data.name = 'Soldier'
    arm.animation_data_clear()
    return arm, {a.name: a for a in bpy.data.actions}


def curves(action):
    out = {}
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    out[(fc.data_path, fc.array_index)] = fc
    return out


class Clip:
    """A source action read through its curves, at any time in its cycle."""

    def __init__(self, action):
        self.curves = curves(action)
        self.start, self.end = action.frame_range
        self.seconds = max(1.0, self.end - self.start) / FPS

    def rot(self, bone, u):
        path = 'pose.bones["%s"].rotation_quaternion' % bone
        f = self.start + (self.end - self.start) * u
        q = [(self.curves[(path, i)].evaluate(f) if (path, i) in self.curves else (1.0 if i == 0 else 0.0)) for i in range(4)]
        return Quaternion(q).normalized()

    def loc(self, bone, u):
        path = 'pose.bones["%s"].location' % bone
        f = self.start + (self.end - self.start) * u
        return Vector([(self.curves[(path, i)].evaluate(f) if (path, i) in self.curves else 0.0) for i in range(3)])


# ------------------------------------------------------------ the rig
def fit_skeleton(arm):
    """Move KayKit's bones to the Trooper, directions and rolls untouched; add the rifle's."""
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    dirs = {b.name: (b.tail - b.head).normalized() for b in eb}
    lens = {b.name: (b.tail - b.head).length for b in eb}
    for b in eb:
        b.use_connect = False
    for name, p in bone_heads().items():
        eb[name].head = g2b(p)
        eb[name].tail = eb[name].head + dirs[name] * lens[name] * LEG_RATIO
    upper = (Vector(ELBOW) - Vector(SHOULDER)).length
    for n, s in (('l', -1), ('r', 1)):
        at = g2b((SHOULDER[0] * s, SHOULDER[1], SHOULDER[2]))
        for bone, length in (('upperarm.', upper), ('lowerarm.', FOREARM * 0.75), ('wrist.', FOREARM * 0.25), ('hand.', 0.07)):
            b = eb[bone + n]
            b.head = at
            b.tail = at + dirs[bone + n] * length
            at = b.tail.copy()
    for b in [b for b in eb if b.name.startswith('handslot')]:
        eb.remove(b)
    gun = eb.new('Gun')
    gun.head = g2b(GRIP)
    gun.tail = gun.head + Vector((0.0, -0.3, 0.0))
    gun.parent = eb['chest']
    # What he carries besides the rifle: each its own bone under the chest, keyed in every clip.
    for name, at in SIDE_ARMS.items():
        b = eb.new(name)
        b.head = g2b(at)
        b.tail = b.head + Vector((0.0, 0.0, -0.12))
        b.parent = eb['chest']
    bpy.ops.object.mode_set(mode='OBJECT')


NO_PALETTE = ('crystal', 'ore_glow', 'rock', 'bark', 'leaf', 'needle')   # each needs its own material


def part_bone(c, group):
    """Which bone a part rides, by where it is modelled (its centre, game space)."""
    if group == 'Gun' or group in SIDE_ARMS:
        return group
    if group in ('LegL', 'LegR'):
        n = 'l' if group == 'LegL' else 'r'
        return ('upperleg.' if c.y >= 0.47 else 'lowerleg.' if c.y >= 0.14 else 'foot.') + n
    n = 'l' if c.x < 0 else 'r'
    if abs(c.x) >= 0.33 and c.y < 1.30:
        return ('upperarm.' if c.y >= 1.08 else 'lowerarm.') + n
    if c.y >= 1.66 and c.z > -0.2:
        return 'head'
    return 'hips' if c.y < 1.08 else 'chest'


def centroid(part):
    c = Vector()
    for v in part.verts:
        c += v.co
    return c / max(1, len(part.verts))


def about(pivot, rot, to=None):
    """A turn about `pivot` that also carries it to `to`."""
    return Matrix.Translation(to if to is not None else pivot) @ rot.to_matrix().to_4x4() @ Matrix.Translation(-pivot)


def stretch(pivot, axis, k):
    """Scale by `k` along `axis` through `pivot`."""
    a = axis.normalized()
    m = Matrix.Identity(3)
    for i in range(3):
        for j in range(3):
            m[i][j] += (k - 1.0) * a[i] * a[j]
    return Matrix.Translation(pivot) @ m.to_4x4() @ Matrix.Translation(-pivot)


def add_side_arms(m):
    """Pistol at the right hip and knife on the chest, both hanging point down (the way each is held in
    the hand is worked out from that: `Retarget`), grenades on the belt, and the one that gets thrown
    out of sight inside the chest until it is in his hand. Bigger than life, or they do not read at
    this camera."""
    x, y, z = SIDE_ARMS['Pistol']
    m.group('Pistol', (x, y, z))
    m.add(box((x, y - 0.13, z + 0.02), (0.026, 0.15, 0.036), 'dark', bevel_w=0.008))
    m.add(box((x, y - 0.12, z + 0.02), (0.018, 0.13, 0.042), 'steel', bevel_w=0.006))
    m.add(box((x, y + 0.01, z - 0.05), (0.022, 0.03, 0.05), 'dark', bevel_w=0.006))
    x, y, z = SIDE_ARMS['Knife']
    m.group('Knife', (x, y, z))
    m.add(box((x, y - 0.20, z), (0.009, 0.17, 0.034), 'steel', bevel_w=0.003))
    m.add(box((x, y - 0.03, z), (0.016, 0.012, 0.05), 'armor_dark', bevel_w=0.003))
    m.add(box((x, y + 0.04, z), (0.018, 0.06, 0.022), 'dark', bevel_w=0.006))
    x, y, z = SIDE_ARMS['Grenade']
    m.group('Grenade', (x, y, z))
    m.add(sphere((x, y, z), 0.085, 8, 6, 'armor_dark'))
    m.group('')
    for side in (1, -1):
        m.add(sphere((0.10 * side, 1.10, 0.26), 0.07, 8, 6, 'armor_dark'))


def build_soldier(arm, model, kind='TROOPER'):
    """Merge the model's parts on the skeleton, each bound whole to its bone."""
    add_side_arms(model)
    bones = [part_bone(centroid(p), g) for p, g in zip(model.parts, model.part_groups)]
    slots, rad, _ = X.prepare(model)
    X.bake_ao(model, rad)
    turn = Matrix.Rotation(math.pi, 4, 'Z')
    data = arm.data.bones
    moves = {}

    def along(bone):
        # In the armature's space: a Bone's own `vector` is against its parent, which bound the forearm
        # 90 degrees off the bone that moves it, so it never reached the hand.
        return data[bone].tail_local - data[bone].head_local

    down = g2b((0.0, -1.0, 0.0))
    for n, s in (('l', -1), ('r', 1)):
        sh, el = (g2b((p[0] * s, p[1], p[2])) for p in (SHOULDER, ELBOW))
        top = g2b((FOREARM_TOP[0] * s, FOREARM_TOP[1], FOREARM_TOP[2]))
        moves['upperarm.' + n] = about(sh, (el - sh).rotation_difference(along('upperarm.' + n)))
        # Stretched along the block's own axis, or it shears into a tray.
        moves['lowerarm.' + n] = (about(top, down.rotation_difference(along('lowerarm.' + n)),
                                        data['lowerarm.' + n].head_local.copy())
                                  @ stretch(top, down, FOREARM_STRETCH))
    ranges = {}
    first = 0
    for part, bone in zip(model.parts, bones):
        bmesh.ops.transform(part, matrix=turn, verts=part.verts[:])
        if bone in moves:
            bmesh.ops.transform(part, matrix=moves[bone], verts=part.verts[:])
        part.normal_update()
        ranges.setdefault(bone, []).append((first, first + len(part.verts)))
        first += len(part.verts)

    obj, tris = X.make_object(kind, model.parts, slots)
    for bone, spans in ranges.items():
        vg = obj.vertex_groups.new(name=bone)
        for a, b in spans:
            vg.add(list(range(a, b)), 1.0, 'REPLACE')
    obj.parent = arm
    obj.modifiers.new('Armature', 'ARMATURE').object = arm
    return obj, tris


def set_pose(arm, pose):
    for b, (q, l) in pose.items():
        pb = arm.pose.bones[b]
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = q
        pb.location = l


def capture_rest(arm):
    """Bone -> (rest matrix, parent name), parents first."""
    out = {}

    def walk(b):
        out[b.name] = (b.matrix_local.copy(), b.parent.name if b.parent else None)
        for c in b.children:
            walk(c)
    for b in arm.data.bones:
        if b.parent is None:
            walk(b)
    return out


def fk(rest, pose):
    """Bone -> pose matrix in the armature's space, from local (rotation, location)."""
    out = {}
    for name, (m, parent) in rest.items():
        q, l = pose.get(name, (Quaternion(), Vector()))
        local = (rest[parent][0].inverted() @ m) if parent else m
        at = (out[parent] @ local) if parent else local
        out[name] = at @ Matrix.Translation(l) @ q.to_matrix().to_4x4()
    return out


def arm_lengths(rest, side):
    n = '.' + side
    return ((rest['lowerarm' + n][0].to_translation() - rest['upperarm' + n][0].to_translation()).length,
            (rest['hand' + n][0].to_translation() - rest['lowerarm' + n][0].to_translation()).length)


def within_reach(rest, P, side, at):
    """Where the hand really gets to, going for `at`: past SOFT_REACH eased toward straight."""
    shoulder = P['upperarm.' + side].to_translation()
    length = sum(arm_lengths(rest, side))
    to = at - shoulder
    r = to.length / length
    if r > SOFT_REACH:
        r = SOFT_REACH + (0.999 - SOFT_REACH) * (1.0 - math.exp(-(r - SOFT_REACH) / (0.999 - SOFT_REACH)))
    return shoulder + to.normalized() * (r * length)


def solve_arm(rest, pose, P, side, hand_at, pole, eased=False):
    """Two bones from the shoulder toward `hand_at`, the elbow toward `pole`. Writes into
    `pose`. `P`: the pose matrices, for the chest. `eased`: `hand_at` is within reach already."""
    n = '.' + side
    shoulder = P['upperarm' + n].to_translation()
    a, b = arm_lengths(rest, side)
    to = (hand_at if eased else within_reach(rest, P, side, hand_at)) - shoulder
    d = max(1e-4, to.length)
    u = to.normalized()
    cos_a = max(-1.0, min(1.0, (a * a + d * d - b * b) / (2.0 * a * d)))
    off = pole - u * pole.dot(u)
    off = off.normalized() if off.length > 1e-4 else u.orthogonal().normalized()
    elbow = shoulder + u * (a * cos_a) + off * (a * math.sqrt(max(0.0, 1.0 - cos_a * cos_a)))

    def aim(bone, parent_matrix, want):
        m, parent = rest[bone]
        base = parent_matrix @ (rest[parent][0].inverted() @ m)
        y = base.to_3x3() @ Vector((0.0, 1.0, 0.0))
        q = base.to_quaternion().inverted() @ y.rotation_difference(want) @ base.to_quaternion()
        pose[bone] = (q, Vector())
        return base @ q.to_matrix().to_4x4()

    upper = aim('upperarm' + n, P['chest'], elbow - shoulder)
    aim('lowerarm' + n, upper, shoulder + u * d - elbow)
    pose['wrist' + n] = (Quaternion(), Vector())
    pose['hand' + n] = (Quaternion(), Vector())


class Retarget:
    def __init__(self, rest_src, rest, clips):
        self.src, self.rest, self.clips = rest_src, rest, clips
        self.chest0 = rest['chest'][0].to_quaternion()
        self.grip = g2b(GRIP)
        self.feet = {}

    def feet_together(self, clip):
        """The point of a walking cycle to stand on: the feet closest and both down."""
        if clip not in self.feet:
            def apart(u):
                P = fk(self.src, {b: (clip.rot(b, u), Vector()) for b in BONES})
                d = P['foot.l'].to_translation() - P['foot.r'].to_translation()
                return d.y ** 2 + (2.0 * d.z) ** 2
            self.feet[clip] = min((i / 24.0 for i in range(24)), key=apart)
        return self.feet[clip]

    def __call__(self, spec, u):
        """The soldier in `spec` at `u` (0-1) of its length: bone -> (rotation, location)."""
        name, source, held, _, freeze, _ = spec
        clip = self.clips[source]
        cu = self.feet_together(clip) if freeze == 'feet' else freeze if freeze is not None else u
        raw = {b: (clip.rot(b, cu), clip.loc(b, cu) if b in ('root', 'hips') else Vector()) for b in BONES}
        pose = {b: (q, l * LEG_RATIO) for b, (q, l) in raw.items()}
        h = held(u) if callable(held) else held

        # The trunk turns toward the rifle about its own axis, the head staying ahead.
        if h['twist']:
            a = -math.radians(h['twist'])
            turn = Quaternion((0.0, 1.0, 0.0), a * 0.5)
            for b in ('spine', 'chest'):
                pose[b] = (pose[b][0] @ turn, pose[b][1])
            pose['head'] = (pose['head'][0] @ Quaternion((0.0, 1.0, 0.0), -a), Vector())

        Ps = fk(self.src, raw)
        Pt = fk(self.rest, pose)
        chest = Pt['chest']
        chest_at = chest.to_translation()
        chest_turn = chest.to_quaternion() @ self.chest0.inverted()

        def mapped(side):
            n = '.' + side
            src_turn = Ps['chest'].to_quaternion() @ self.src['chest'][0].to_quaternion().inverted()
            rel = src_turn.inverted() @ (Ps['hand' + n].to_translation() - Ps['chest'].to_translation())
            return chest_at + chest_turn @ Vector((rel.x * HAND_SCALE[0], rel.y * HAND_SCALE[1], rel.z * HAND_SCALE[2]))

        def against_chest(p):
            return chest_at + chest_turn @ Vector((-p[0], -p[2], p[1]))

        follows_hand = h['grip'] == 'hand.r'
        grip_at = within_reach(self.rest, Pt, 'r', mapped('r')) if follows_hand else against_chest(h['grip'])
        aim_turn = Matrix.Rotation(-math.radians(h['yaw']), 3, 'Z') @ Matrix.Rotation(-math.radians(h['pitch']), 3, 'X')
        sway = Quaternion().slerp(chest_turn, h['follow']).to_matrix()
        rifle = Matrix.Translation(grip_at) @ (sway @ aim_turn).to_4x4() @ Matrix.Translation(-self.grip)

        # The rifle's bone: its place in the armature, against the chest it hangs from.
        rg = self.rest['Gun'][0]
        local = (chest @ (self.rest['chest'][0].inverted() @ rg)).inverted() @ (rifle @ rg)
        pose['Gun'] = (local.to_quaternion(), local.to_translation())

        fore_at = rifle @ g2b(FOREGRIP)
        for side, want in (('r', h['right']), ('l', h['left'])):
            n = '.' + side
            mid = (Ps['upperarm' + n].to_translation() + Ps['hand' + n].to_translation()) * 0.5
            # The source's elbow says which way the arm bends, until its arm is nearly straight
            # and says nothing: the fixed lean takes over there, or the elbow flips sides.
            source_pole = Ps['lowerarm' + n].to_translation() - mid + g2b(POLE[side]).normalized() * POLE_LEAN
            if want == 'mapped':
                solve_arm(self.rest, pose, Pt, side, mapped(side), source_pole)
                continue
            if want == 'pistol':
                k = h['recoil']
                at = chest_at + g2b((PISTOL_AT[0], PISTOL_AT[1] + 0.04 * k, PISTOL_AT[2] - 0.06 * k))
                solve_arm(self.rest, pose, Pt, side, at, g2b(POLE[side]))
                continue
            if side == 'r' and follows_hand:
                solve_arm(self.rest, pose, Pt, side, grip_at, source_pole, eased=True)
                continue
            at = grip_at if want == 'grip' else fore_at
            if side == 'l' and 'left_path' in h:
                # A reload: the left hand off the rifle to the belt and back with a magazine.
                t = h['left_path']
                mag = rifle @ g2b((GRIP[0], GRIP[1] - 0.14, GRIP[2] + 0.14))
                belt = against_chest((-0.26, -0.40, 0.14))
                if t < 0.2:
                    at = fore_at.lerp(mag, t / 0.2)
                elif t < 0.45:
                    at = mag.lerp(belt, (t - 0.2) / 0.25)
                elif t < 0.7:
                    at = belt.lerp(mag, (t - 0.45) / 0.25)
                elif t < 0.85:
                    at = mag
                else:
                    at = mag.lerp(fore_at, (t - 0.85) / 0.15)
            solve_arm(self.rest, pose, Pt, side, at, g2b(POLE[side]))

        # The side arms hang where they are modelled unless this clip has one in his hand.
        for b in SIDE_ARMS:
            pose[b] = (Quaternion(), Vector())
        prop = IN_HAND.get(name)
        if prop and (name != 'Throw' or u < THROW_RELEASE):
            Pf = fk(self.rest, pose)
            hand = Pf['hand.r'].to_translation()
            fore = (hand - Pf['lowerarm.r'].to_translation()).normalized()
            if 'recoil' in h:
                # The pistol lies along the line of fire, not the forearm, and kicks up.
                fore = (g2b((0.0, 0.0, 1.0)) + g2b((0.0, 1.0, 0.0)) * (0.25 * h['recoil'])).normalized()
            side_ = fore.cross(Vector((0.0, 0.0, 1.0)))
            side_ = side_.normalized() if side_.length > 1e-3 else Vector((1.0, 0.0, 0.0))
            up = side_.cross(fore).normalized()
            rest_along, rest_up = g2b((0.0, -1.0, 0.0)), g2b((0.0, 0.0, 1.0))
            to = Matrix((fore, up, fore.cross(up))).transposed()
            frm = Matrix((rest_along, rest_up, rest_along.cross(rest_up))).transposed()
            rg = self.rest[prop][0]
            placed = Matrix.Translation(hand + fore * 0.05) @ (to @ frm.inverted()).to_4x4() @ Matrix.Translation(-rg.to_translation())
            local = (chest @ (self.rest['chest'][0].inverted() @ rg)).inverted() @ (placed @ rg)
            pose[prop] = (local.to_quaternion(), local.to_translation())
        return pose


# ------------------------------------------------------------ the actions
def spec_seconds(spec, clips):
    return spec[3] if spec[3] is not None else clips[spec[1]].seconds


def write_action(arm, retarget, spec, clips):
    name = spec[0]
    frames = max(2, int(round(spec_seconds(spec, clips) * FPS)))
    keys = {}
    for i in range(frames + 1):
        pose = retarget(spec, i / float(frames))
        for b, (q, l) in pose.items():
            k = keys.setdefault(b, ([], []))
            if k[0] and k[0][-1].dot(q) < 0.0:
                q = Quaternion((-q.w, -q.x, -q.y, -q.z))
            k[0].append(q)
            k[1].append(l)
    act = bpy.data.actions.new('new_' + name)   # a source action may hold the name yet
    act.use_fake_user = True
    slot = act.slots.new(id_type='OBJECT', name=arm.name)
    strip = act.layers.new('Layer').strips.new(type='KEYFRAME')
    bag = strip.channelbag(slot, ensure=True)

    def curve(path, index, group, values):
        fc = bag.fcurves.new(path, index=index, group_name=group)
        fc.keyframe_points.add(len(values))
        flat = []
        for f, v in enumerate(values):
            flat += [f, v]
        fc.keyframe_points.foreach_set('co', flat)
        for kp in fc.keyframe_points:
            kp.interpolation = 'LINEAR'
        fc.update()

    for b, (qs, ls) in keys.items():
        for i in range(4):
            curve('pose.bones["%s"].rotation_quaternion' % b, i, b, [q[i] for q in qs])
        if b in ('root', 'hips', 'Gun') or b in SIDE_ARMS:
            for i in range(3):
                curve('pose.bones["%s"].location' % b, i, b, [l[i] for l in ls])
    return act, slot


def merge_materials(soldier, slots):
    """One material for the whole soldier, so a soldier is one draw in each pass: colour G says
    which palette slot a face is (SF_Unit's _UsePalette, SFMaterialLibrary.BuildPalette). Corners,
    not vertices: a part's faces can share a vertex and still differ in slot."""
    me = soldier.data
    for slot in slots:
        if slot in NO_PALETTE:
            raise RuntimeError('the palette cannot draw slot %s' % slot)
    index = [X.S.MAT_INDEX[s] / 255.0 for s in slots]
    old = me.color_attributes['Col']
    col = [tuple(old.data[v].color) for v in range(len(me.vertices))]
    new = me.color_attributes.new('Pal', 'FLOAT_COLOR', 'CORNER')
    for poly in me.polygons:
        for li in poly.loop_indices:
            c = col[me.loops[li].vertex_index]
            new.data[li].color = (c[0], index[poly.material_index], c[2], c[3])
    me.color_attributes.remove(me.color_attributes['Col'])
    me.color_attributes['Pal'].name = 'Col'
    me.color_attributes.active_color = me.color_attributes['Col']
    me.materials.clear()
    me.materials.append(bpy.data.materials.new('palette'))
    me.polygons.foreach_set('material_index', [0] * len(me.polygons))
    me.update()


def export(arm, soldier, retarget, clips, out_dir, kind='TROOPER'):
    # Only the Trooper's file carries clips: the other infantry stand on the same skeleton and play them
    # (SoldierRigBuilder gives them all one controller), so five sets of the same clips are never loaded.
    scene = bpy.context.scene
    scene.render.fps = FPS
    merge_materials(soldier, [m.name for m in soldier.data.materials])
    source = list(bpy.data.actions)
    made = [write_action(arm, retarget, s, clips) for s in SPECS] if kind == 'TROOPER' else []
    for a in source:
        bpy.data.actions.remove(a)
    for a, _ in made:
        a.name = a.name[len('new_'):]
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    if made:
        arm.animation_data_create()
        arm.animation_data.action = made[0][0]
        arm.animation_data.action_slot = made[0][1]
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    soldier.select_set(True)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(out_dir, 'SF_%s_RIG.fbx' % kind)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=False,
        mesh_smooth_type='FACE', use_mesh_modifiers=False, colors_type='LINEAR',
        add_leaf_bones=False, use_armature_deform_only=False,
        bake_anim=bool(made), bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=1.0,
        path_mode='STRIP')
    print('EXPORTED %s  %.2f MB, %d clips' % (path, os.path.getsize(path) / 1e6, len(made)))


# ------------------------------------------------------------ the picture
ROW = 5.4  # metres between a sheet's rows, so no figure stands on the label behind it


def label(scene, text, at, yaw):
    cu = bpy.data.curves.new(text, 'FONT')
    cu.body = text
    cu.size = 0.3
    cu.align_x = 'CENTER'
    ob = bpy.data.objects.new(text, cu)
    ob.location = at
    ob.rotation_euler = (math.radians(60), 0.0, yaw)
    scene.collection.objects.link(ob)
    if 'label' not in bpy.data.materials:
        mat = bpy.data.materials.new('label')
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes['Principled BSDF']
        bsdf.inputs['Base Color'].default_value = (1.0, 1.0, 0.8, 1.0)
        bsdf.inputs['Emission Color'].default_value = (1.0, 1.0, 0.8, 1.0)
        bsdf.inputs['Emission Strength'].default_value = 1.0
    cu.materials.append(bpy.data.materials['label'])


def preview(arm, soldier, poses, path, yaw_deg=25.0, columns=6):
    """`poses`: (label, pose). A sheet of the soldier, one figure a pose."""
    scene = bpy.context.scene
    yaw = math.radians(yaw_deg)
    across = Vector((math.cos(yaw), math.sin(yaw), 0.0))
    back = Vector((-math.sin(yaw), math.cos(yaw), 0.0))
    for i, (name, pose) in enumerate(poses):
        a = arm.copy()
        m = soldier.copy()
        scene.collection.objects.link(a)
        scene.collection.objects.link(m)
        m.parent = a
        m.modifiers['Armature'].object = a
        set_pose(a, pose)
        a.location = across * ((i % columns) * 3.1) + back * ((i // columns) * ROW)
        label(scene, name, a.location - back * 1.25 + Vector((0.0, 0.0, 0.02)), yaw)
    arm.hide_render = soldier.hide_render = True
    rows = (len(poses) + columns - 1) // columns
    mid = across * ((columns - 1) * 1.55) + back * ((rows - 1) * ROW * 0.5 - 0.3) + Vector((0.0, 0.0, 0.5))

    if 'team' in bpy.data.materials:
        bsdf = bpy.data.materials['team'].node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Base Color'].default_value = (0.08, 0.28, 0.75, 1.0)
    bpy.ops.mesh.primitive_plane_add(size=120.0, location=(mid.x, mid.y, 0.0))
    mat = bpy.data.materials.new('ground')
    mat.use_nodes = True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (0.30, 0.32, 0.26, 1.0)
    bpy.context.active_object.data.materials.append(mat)

    sun = bpy.data.objects.new('sun', bpy.data.lights.new('sun', 'SUN'))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(50), 0.0, math.radians(35) + yaw)
    scene.collection.objects.link(sun)
    world = bpy.data.worlds.new('w')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs[0].default_value = (0.5, 0.55, 0.65, 1.0)
    scene.world = world

    cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = columns * 3.1 * 1.02
    pitch = math.radians(33)
    view = -back * math.cos(pitch) + Vector((0.0, 0.0, math.sin(pitch)))
    cam.location = mid + view * 60.0
    cam.rotation_mode = 'QUATERNION'
    cam.rotation_quaternion = (-view).to_track_quat('-Z', 'Y')
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 12
    scene.render.resolution_x = 1800
    scene.render.resolution_y = int(1800 * (rows * ROW * math.sin(pitch) + 1.8) / (columns * 3.1 * 1.02))
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def stride(retarget, rest, clips):
    """How fast the feet move over the ground in the walking clips, metres a second at the clip's own
    speed: the sole furthest down its cycle, while it is planted (SoldierRigView follows it)."""
    for spec in SPECS:
        if spec[0] not in ('Walk', 'Run', 'Sneak', 'CrouchWalk', 'Crawl'):
            continue
        seconds = spec_seconds(spec, clips)
        frames = max(8, int(round(seconds * FPS)))
        path = {'l': [], 'r': []}
        for i in range(frames):
            P = fk(rest, retarget(spec, i / float(frames)))
            for n in ('l', 'r'):
                f = P['foot.' + n].to_translation()
                path[n].append((f.y, f.z))
        speeds = []
        for n in ('l', 'r'):
            low = min(z for _, z in path[n])
            for i in range(frames):
                y0, z0 = path[n][i]
                y1, _ = path[n][(i + 1) % frames]
                if z0 < low + 0.03:
                    speeds.append(abs(y1 - y0) * frames / seconds)
        speeds.sort()
        print('STRIDE %-11s %.2f s a cycle, feet planted %2d%% of it, %.2f m/s (median), %.2f (upper quarter)' % (
            spec[0], seconds, 50 * len(speeds) / frames, speeds[len(speeds) // 2], speeds[3 * len(speeds) // 4]))


def check(retarget, rest, clips):
    """What can be read from numbers, per clip over its length: how far each hand on the rifle
    misses it (target under 2 cm), how much of each arm's length it uses (target under 0.95: no
    arm at full stretch), the largest step an elbow takes in one frame, the seam between a
    looping clip's ends, the rifle's pitch and yaw at a quarter of the clip, and the soles' height."""
    print('CHECK clip        r miss l miss  r reach l reach  elbow step  seam  pitch   yaw  soles low..high')
    grip, fore = g2b(GRIP), g2b(FOREGRIP)
    rg = rest['Gun'][0]
    for spec in SPECS:
        held = spec[2]
        frames = max(2, int(round(spec_seconds(spec, clips) * FPS)))
        miss = {'r': 0.0, 'l': 0.0}
        reach = {'r': 0.0, 'l': 0.0}
        soles = []
        step = seam = pitch = yaw = 0.0
        elbows = first = None
        for i in range(frames + 1):
            u = i / float(frames)
            P = fk(rest, retarget(spec, u))
            rifle = P['Gun'] @ rg.inverted()
            h = held(u) if callable(held) else held
            for side, want in (('r', h['right']), ('l', h['left'])):
                hand = P['hand.' + side].to_translation()
                if want == 'grip' or want == 'fore':
                    if not (side == 'l' and 'left_path' in h):
                        miss[side] = max(miss[side], (hand - rifle @ (grip if want == 'grip' else fore)).length)
                length = sum(arm_lengths(rest, side))
                reach[side] = max(reach[side], (hand - P['upperarm.' + side].to_translation()).length / length)
            now = (P['lowerarm.l'].to_translation(), P['lowerarm.r'].to_translation())
            if elbows is not None:
                step = max(step, (now[0] - elbows[0]).length, (now[1] - elbows[1]).length)
            else:
                first = now
            elbows = now
            for n in ('l', 'r'):
                soles.append(P['foot.' + n].to_translation().z - rest['foot.' + n][0].to_translation().z)
            if i == frames // 4:
                d = rifle.to_3x3() @ Vector((0.0, -1.0, 0.0))
                pitch = math.degrees(math.asin(max(-1.0, min(1.0, d.z))))
                yaw = math.degrees(math.atan2(-d.x, -d.y))
        if spec[5]:
            seam = max((elbows[0] - first[0]).length, (elbows[1] - first[1]).length)
        print('CHECK %-11s %6.3f %6.3f  %7.2f %7.2f  %10.3f %5.3f %6.1f %5.1f  %5.2f..%.2f' % (
            spec[0], miss['r'], miss['l'], reach['r'], reach['l'], step, seam, pitch, yaw, min(soles), max(soles)))


INFANTRY = {'TROOPER': B.build_trooper, 'MARKSMAN': R.build_marksman, 'THUMPER': R.build_thumper,
            'SCORCHER': R.build_scorcher, 'ROCKETEER': R.build_rocketeer, 'GUNNER': R.build_gunner}


def main():
    argv = sys.argv[sys.argv.index('--') + 1:]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm, actions = load_sources()
    rest_src = capture_rest(arm)
    fit_skeleton(arm)
    rest = capture_rest(arm)
    clips = {name: Clip(a) for name, a in actions.items()}
    retarget = Retarget(rest_src, rest, clips)
    # The type goes last: preview <png> [view] [TYPE], export <dir> [TYPE].
    kind = argv[-1] if argv[-1] in INFANTRY else 'TROOPER'
    soldier, tris = build_soldier(arm, INFANTRY[kind](), kind)
    print('SOLDIER %d triangles, %d bones' % (tris, len(arm.data.bones)))
    if argv[0] == 'preview':
        at = {'Walk': 0.25, 'Run': 0.25, 'Sneak': 0.25, 'CrouchWalk': 0.25, 'Crawl': 0.25, 'Fire': 0.05,
              'Reload': 0.35, 'Throw': 0.45, 'Stab': 0.45, 'Dive': 0.5, 'Vault': 0.4, 'Hit': 0.3}
        poses = [(s[0], retarget(s, at.get(s[0], 0.0))) for s in SPECS]
        view = argv[2] if len(argv) > 2 and argv[2] in ('front', 'side', 'back') else 'front'
        preview(arm, soldier, poses, os.path.abspath(argv[1]), {'front': 25.0, 'side': 80.0, 'back': 155.0}[view])
    elif argv[0] == 'export':
        export(arm, soldier, retarget, clips, os.path.abspath(argv[1]), kind)
    elif argv[0] == 'stride':
        stride(retarget, rest, clips)
    elif argv[0] == 'check':
        check(retarget, rest, clips)


if __name__ == '__main__':
    main()
