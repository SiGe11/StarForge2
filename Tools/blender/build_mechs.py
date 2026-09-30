"""build_mechs.py -- the Mech kit: the modular parts every Mech is assembled from.

    Blender --background --factory-startup --python Tools/blender/build_mechs.py -- <out_dir> [<blend_path>]

Same authoring rules as build_models.py (game space, +Y up, +Z forward, the
house palette, bevels on every silhouette edge) and the same exporter
(export_fbx.py), but its own entry point: it writes only the SF_MECH_*.fbx files
and Art/Models/mechs.json, so the rest of the models are not re-exported (the FBX
writer changes every file byte for byte).

A Mech is a locomotion module, a frame on its waist and weapons on the frame's
mounts; the game assembles them at runtime (View/MechView) and the Mech Bay's
upgrades switch on extra parts in the frame (armour plates, the uplink dish).

    MECH_LEGS_BIPED    reverse-jointed walker; groups ThighX, ShinX, FootX
    MECH_LEGS_QUAD     four-legged strider;   groups LegXY, ShinXY

The leg segments are exported side by side under the root, not nested: the FBX
writer (with bake_space_transform) mangles the transform of a grandchild -- a
shin under a thigh came into Unity turned 270 degrees and metres out of place.
MechView chains them (thigh > shin > foot) as it rigs the legs.
    MECH_TRACKS        twin track units under a skirt
    MECH_HOVER         grav skirt on four thruster nacelles; groups FanXY
    MECH_FRAME_LIGHT / _MEDIUM / _HEAVY
                       torsos, origin at the waist (the torso turns about it); groups
                       Plates1-3, Uplink, Mast, Emitters, Nanites, Reactive (shown when
                       the Mech has them)
    MECH_W_*           weapons, origin at the mount; arm guns point along +Z
    MECH_BAY, MECH_BAY_GUN   the Mech Bay and its tower's gun head

Points the game needs (the waist, every mount, each weapon's muzzle, the leg
joints at rest) are written to mechs.json beside the models; the prefab builder
bakes them into the MechKit asset, and MechCore/MechView read them from there.
"""

import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy
from mathutils import Matrix, Vector

import sf_model as S
from sf_model import Model, box, frustum, cyl, tube, sphere, ring_flat, inset, face_plate, xform, track_loop
import build_models as B
import export_fbx as E

POINTS = {}   # model name -> {point name: (x, y, z)}


def point(model, name, p):
    POINTS.setdefault(model.name, {})[name] = tuple(round(c, 4) for c in p)


def align(bm, d, at):
    """Turn a part built along +Y to run along `d`, then move its origin to `at`."""
    d = Vector(d)
    if d.length < 1e-6:
        return bm
    q = Vector((0.0, 1.0, 0.0)).rotation_difference(d.normalized())
    xform(bm, q.to_matrix().to_4x4())
    xform(bm, Matrix.Translation(Vector(at)))
    return bm


def strut(p0, p1, hw, hd, mat='armor', taper=(1.0, 1.0), bevel_w=0.04, twist=0.0):
    """A box-section member from p0 to p1: half width hw (across), half depth hd."""
    p0, p1 = Vector(p0), Vector(p1)
    L = (p1 - p0).length
    bm = frustum((0.0, 0.0, 0.0), (hw, L * 0.5, hd), top=taper, mat=mat, bevel_w=bevel_w)
    if twist:
        xform(bm, Matrix.Rotation(twist, 4, 'Y'))
    return align(bm, p1 - p0, (p0 + p1) * 0.5)


def rod(p0, p1, r0, r1=None, seg=10, mat='steel', bevel_w=0.015):
    p0, p1 = Vector(p0), Vector(p1)
    L = (p1 - p0).length
    bm = cyl((0.0, 0.0, 0.0), r0, r1 if r1 is not None else r0, L, seg, mat, axis='y', bevel_w=bevel_w)
    return align(bm, p1 - p0, p0)


def piston(m, p0, p1, r=0.09):
    """A hydraulic ram: a sleeve from p0 most of the way, a polished rod the rest."""
    p0, p1 = Vector(p0), Vector(p1)
    mid = p0.lerp(p1, 0.62)
    m.add(rod(p0, mid, r, r, 10, 'armor_dark'))
    m.add(rod(p0.lerp(p1, 0.55), p1, r * 0.55, r * 0.55, 8, 'steel', bevel_w=0.0))
    m.add(rod(p0 - (p1 - p0).normalized() * 0.05, p0 + (p1 - p0).normalized() * 0.12, r * 1.25, r * 1.25, 10, 'dark'))


def joint(m, c, r, w, mat='armor_dark', axis='x'):
    """A pin joint: a drum across the joint with a lit cap each end."""
    c = Vector(c)
    if axis == 'x':
        m.add(cyl((c.x - w * 0.5, c.y, c.z), r, r, w, 14, mat, axis='x', bevel_w=0.03))
        for s in (-1, 1):
            m.add(cyl((c.x + s * w * 0.5 - (0.04 if s > 0 else 0.0), c.y, c.z), r * 0.55, r * 0.55, 0.04, 12, 'steel', axis='x', bevel_w=0.0))
    else:
        m.add(cyl((c.x, c.y - w * 0.5, c.z), r, r, w, 14, mat, axis='y', bevel_w=0.03))


# ================================================================ locomotion
def build_legs_biped():
    """Strider: a reverse-jointed walker, the classic war-machine silhouette.

    The thigh runs forward and down from the hip to a knee well in front of the
    body, the shin back down to an ankle under the hip, and a three-toed foot with
    a heel spur spreads the weight. Big pistons behind each joint and armour over
    the thighs make the legs read as load-bearing at RTS distance."""
    m = Model(40, 'MECH_LEGS_BIPED')
    waist = 5.05
    point(m, 'Waist', (0.0, waist, 0.0))

    # Pelvis: a wide armoured block with a turntable on top.
    pel = frustum((0, 4.50, -0.05), (0.78, 0.36, 0.72), top=(1.10, 0.95), mat='armor_dark', bevel_w=0.06)
    B.panel(pel, 'z', 1, 0.08, -0.03, 'shadowed')
    m.add(pel)
    m.add(frustum((0, 4.05, -0.05), (0.52, 0.12, 0.52), top=(1.35, 1.25), mat='dark', bevel_w=0.04))
    m.add(cyl((0, 4.84, 0), 0.98, 0.92, 0.14, 20, 'steel', bevel_w=0.03))
    m.add(cyl((0, 4.98, 0), 0.82, 0.82, 0.07, 20, 'dark', bevel_w=0.015))
    m.add(ring_flat((0, 4.91, 0), 0.99, 1.06, 24, 'team', thickness=0.05))
    # Groin armour and a lamp.
    m.add(frustum((0, 4.28, 0.62), (0.34, 0.26, 0.10), top=(0.8, 1.0), shift=(0, 0.05), mat='armor', bevel_w=0.03))
    m.add(box((0, 4.40, 0.74), (0.14, 0.035, 0.02), 'glow_amber', bevel_w=0.0))
    # Rear heat sinks.
    for k in range(4):
        m.add(box((-0.42 + k * 0.28, 4.52, -0.80), (0.08, 0.26, 0.05), 'shadowed', bevel_w=0.0))

    for side, tag in ((1, 'R'), (-1, 'L')):
        x = 1.12 * side
        H = Vector((x, 4.50, 0.0))
        K = Vector((1.24 * side, 2.82, 0.95))
        A = Vector((1.24 * side, 0.74, -0.24))
        point(m, 'Hip' + tag, H)
        point(m, 'Knee' + tag, K)
        point(m, 'Ankle' + tag, A)

        # Hip housing on the pelvis (body): a big drum the thigh swings on.
        m.add(cyl((min(0.62 * side, 1.02 * side), 4.50, 0), 0.46, 0.46, 0.40, 16, 'armor', axis='x', bevel_w=0.04))
        m.add(box((0.98 * side, 4.50, 0), (0.05, 0.30, 0.30), 'dark', bevel_w=0.02))

        # ---- thigh
        m.group('Thigh' + tag, tuple(H))
        joint(m, H, 0.36, 0.52)
        # The thigh armour: a heavy shell front-top, wider at the hip.
        m.add(strut(H + Vector((0, 0.05, 0.10)), K + Vector((0, 0.25, 0.10)), 0.34, 0.40, 'armor', taper=(0.75, 0.70), bevel_w=0.05))
        m.add(strut(H + Vector((0.20 * side, 0.12, 0.18)), K + Vector((0.20 * side, 0.32, 0.12)), 0.10, 0.30, 'team', taper=(0.8, 0.7), bevel_w=0.02))
        m.add(strut(H + Vector((0, -0.10, -0.08)), K + Vector((0, 0.0, -0.14)), 0.22, 0.20, 'dark', taper=(0.8, 0.8), bevel_w=0.03))
        # A ram behind the thigh from the hip to the knee.
        piston(m, H + Vector((-0.28 * side, 0.30, -0.36)), K + Vector((-0.28 * side, 0.16, -0.30)), 0.10)
        # Lit actuator band.
        mid = H.lerp(K, 0.5)
        m.add(box((mid.x + 0.35 * side, mid.y + 0.12, mid.z + 0.05), (0.03, 0.18, 0.05), 'glow_cyan', bevel_w=0.0))

        # ---- shin
        m.group('Shin' + tag, tuple(K))
        joint(m, K, 0.32, 0.62)
        # Knee cap.
        m.add(frustum((K.x, K.y + 0.08, K.z + 0.28), (0.30, 0.34, 0.16), top=(0.7, 0.5), shift=(0, 0.06), mat='armor_lit', bevel_w=0.04))
        # The shin: a slimmer box beam, then a spring-steel lower that meets the ankle.
        m.add(strut(K, K.lerp(A, 0.62), 0.24, 0.28, 'armor', taper=(0.85, 0.85), bevel_w=0.04))
        m.add(strut(K.lerp(A, 0.55), A, 0.17, 0.20, 'armor_dark', taper=(0.9, 0.9), bevel_w=0.03))
        # Twin rams down the back of the shin.
        for dx in (-0.14, 0.14):
            piston(m, K + Vector((dx, -0.10, -0.34)), A + Vector((dx, 0.30, -0.26)), 0.07)
        m.add(strut(K.lerp(A, 0.18) + Vector((0.26 * side, 0, 0)), K.lerp(A, 0.5) + Vector((0.26 * side, 0, 0)), 0.03, 0.12, 'team', bevel_w=0.01))

        # ---- foot
        m.group('Foot' + tag, tuple(A))
        joint(m, A, 0.24, 0.48)
        # Ankle housing, then three splayed toes and a heel spur, soles on the ground.
        m.add(frustum((A.x, 0.40, A.z + 0.05), (0.34, 0.26, 0.34), top=(0.7, 0.7), mat='armor_dark', bevel_w=0.04))
        for k, ang in enumerate((-26.0, 0.0, 26.0)):
            a = math.radians(ang)
            d = Vector((math.sin(a), 0.0, math.cos(a)))
            base = Vector((A.x, 0.0, A.z + 0.10)) + d * 0.18
            tip = Vector((A.x, 0.0, A.z + 0.10)) + d * (1.15 if k == 1 else 0.95)
            toe = strut(base + Vector((0, 0.20, 0)), tip + Vector((0, 0.10, 0)), 0.13, 0.12, 'armor' if k == 1 else 'armor_dark', taper=(0.65, 0.55), bevel_w=0.03)
            m.add(toe)
            m.add(box((tip.x, 0.05, tip.z), (0.14, 0.05, 0.10), 'dark', bevel_w=0.02))
        heel = Vector((A.x, 0.0, A.z - 0.62))
        m.add(strut(Vector((A.x, 0.25, A.z - 0.05)), heel + Vector((0, 0.10, 0)), 0.12, 0.11, 'armor_dark', taper=(0.6, 0.6), bevel_w=0.03))
        m.add(box((heel.x, 0.05, heel.z), (0.12, 0.05, 0.10), 'dark', bevel_w=0.02))
        m.group('')
    return m


def build_legs_quad():
    """Arachnid: four legs from a squat carapace, knees high, feet planted wide.

    The steadiest platform in the kit; the long spread of the legs is the
    silhouette, so each leg is a tall arch: femur up and out, tibia a long
    tapering spike down to a round footpad."""
    m = Model(41, 'MECH_LEGS_QUAD')
    waist = 3.9
    point(m, 'Waist', (0.0, waist, 0.0))

    hull = frustum((0, 3.30, 0), (1.10, 0.40, 1.30), top=(0.86, 0.84), mat='armor_dark', bevel_w=0.07)
    B.panel(hull, 'y', 1, 0.14, -0.03, 'armor')
    m.add(hull)
    m.add(frustum((0, 2.78, 0), (0.80, 0.14, 0.96), top=(1.35, 1.34), mat='dark', bevel_w=0.04))
    # Belly sensor and lamps.
    m.add(cyl((0, 2.54, 0.4), 0.22, 0.30, 0.10, 12, 'armor', bevel_w=0.02))
    m.add(cyl((0, 2.50, 0.4), 0.14, 0.14, 0.04, 12, 'glow_amber', bevel_w=0.0))
    m.add(cyl((0, 3.70, 0), 0.94, 0.90, 0.14, 20, 'steel', bevel_w=0.03))
    m.add(cyl((0, 3.84, 0), 0.80, 0.80, 0.06, 20, 'dark', bevel_w=0.015))
    m.add(ring_flat((0, 3.72, 0), 0.97, 1.04, 24, 'team', thickness=0.05))
    for side in (1, -1):
        m.add(box((1.02 * side, 3.34, 0), (0.05, 0.14, 0.62), 'team', bevel_w=0.015))

    for fz, zt in ((1, 'F'), (-1, 'B')):
        for side, xt in ((1, 'R'), (-1, 'L')):
            tag = zt + xt
            P = Vector((1.00 * side, 3.12, 1.00 * fz))
            d = Vector((side, 0.0, fz)).normalized()
            K = P + d * 1.45 + Vector((0, 0.95, 0))
            F = P + d * 2.80
            F.y = 0.0
            point(m, 'Hip' + tag, P)
            point(m, 'Knee' + tag, K)
            point(m, 'Foot' + tag, F)
            # Hip socket on the carapace.
            m.add(cyl((P.x, P.y - 0.34, P.z), 0.44, 0.40, 0.62, 14, 'armor', bevel_w=0.04))
            m.group('Leg' + tag, tuple(P))
            m.add(cyl((P.x, P.y - 0.30, P.z), 0.34, 0.34, 0.60, 14, 'dark', bevel_w=0.02))
            m.add(strut(P, K, 0.26, 0.32, 'armor', taper=(0.8, 0.75), bevel_w=0.05))
            m.add(strut(P + Vector((0, 0.22, 0)), K + Vector((0, 0.22, 0)), 0.08, 0.26, 'team', taper=(0.8, 0.7), bevel_w=0.02))
            piston(m, P + Vector((0, -0.28, 0)) + d * 0.2, K + Vector((0, -0.25, 0)) - d * 0.15, 0.09)
            m.group('Shin' + tag, tuple(K))
            joint(m, K, 0.30, 0.52, axis='x')
            # The tibia: a long wedge, broad at the knee, a spike at the foot.
            m.add(strut(K, K.lerp(F, 0.6), 0.22, 0.26, 'armor_dark', taper=(0.8, 0.8), bevel_w=0.04))
            m.add(strut(K.lerp(F, 0.55), F + Vector((0, 0.28, 0)), 0.15, 0.17, 'armor', taper=(0.55, 0.55), bevel_w=0.03))
            m.add(frustum((K.x, K.y + 0.22, K.z), (0.26, 0.20, 0.26), top=(0.5, 0.5), mat='armor_lit', bevel_w=0.04))
            m.add(box(tuple(K.lerp(F, 0.25) + d * 0.18), (0.04, 0.18, 0.04), 'glow_cyan', bevel_w=0.0))
            # Footpad.
            m.add(cyl((F.x, 0.0, F.z), 0.36, 0.28, 0.14, 12, 'dark', bevel_w=0.03))
            m.add(cyl((F.x, 0.14, F.z), 0.24, 0.16, 0.18, 10, 'armor_dark', bevel_w=0.02))
            m.group('')
    return m


def build_tracks():
    """Juggernaut: two broad track units under an armoured skirt, a ram at the bow
    and a turret ring the frame turns on. Built like the Mauler, twice its size."""
    m = Model(42, 'MECH_TRACKS')
    waist = 2.75
    point(m, 'Waist', (0.0, waist, 0.0))
    B.track_unit(m, 1.55, 0.46, 6, 2.30, 0.58, 0.92)
    for side in (1, -1):
        sh = frustum((1.55 * side, 1.34, -0.05), (0.54, 0.24, 2.10), top=(1.2, 0.94), shift=(0.08 * side, 0), mat='armor_dark', bevel_w=0.06)
        m.add(sh)
        for zc in (-1.25, -0.20, 0.85):
            m.add(frustum((1.94 * side, 1.20, zc), (0.06, 0.30, 0.46), top=(1.0, 0.9), shift=(0.07 * side, 0), mat='team', bevel_w=0.025))
    low = frustum((0, 1.10, 0), (1.10, 0.40, 2.10), top=(1.05, 0.95), mat='dark', bevel_w=0.07)
    m.add(low)
    up = frustum((0, 1.78, -0.20), (1.12, 0.30, 1.70), top=(0.92, 0.86), mat='armor', bevel_w=0.07)
    B.panel(up, 'y', 1, 0.18, -0.035, 'armor_lit')
    m.add(up)
    # The ram: a heavy wedge across the bow.
    ram = frustum((0, 0.92, 2.28), (1.30, 0.42, 0.20), top=(0.9, 0.6), shift=(0, -0.12), mat='armor_lit', bevel_w=0.06)
    B.rot(ram, math.radians(-22), 'X', (0, 0.92, 2.28))
    m.add(ram)
    for k in range(-2, 3):
        m.add(box((k * 0.46, 0.80, 2.40), (0.06, 0.24, 0.10), 'steel', bevel_w=0.02))
    # Turret ring.
    m.add(cyl((0, 2.08, 0), 1.10, 1.02, 0.40, 22, 'armor_dark', bevel_w=0.05))
    m.add(cyl((0, 2.48, 0), 0.96, 0.92, 0.18, 22, 'steel', bevel_w=0.03))
    m.add(cyl((0, 2.66, 0), 0.82, 0.82, 0.09, 22, 'dark', bevel_w=0.015))
    m.add(ring_flat((0, 2.48, 0), 1.03, 1.10, 24, 'team', thickness=0.05))
    # Exhausts and lights.
    for side in (1, -1):
        st = tube((0.70 * side, 1.90, -1.72), 0.16, 0.11, 0.55, 10, 'dark', axis='y')
        B.rot(st, math.radians(-14), 'X', (0.70 * side, 2.0, -1.72))
        m.add(st)
        m.add(cyl((0.82 * side, 1.62, 1.82), 0.13, 0.13, 0.07, 10, 'dark', axis='z', bevel_w=0.015))
        m.add(cyl((0.82 * side, 1.62, 1.88), 0.10, 0.10, 0.03, 10, 'glow_dim', axis='z', bevel_w=0.0))
        point(m, 'Stack' + ('R' if side > 0 else 'L'), (0.70 * side, 2.50, -1.86))
    return m


def build_hover():
    """Wraith: a grav skirt -- a broad armoured disc on four thruster nacelles, its
    underside lit, floating a metre off the ground. Fans turn in each nacelle."""
    m = Model(43, 'MECH_HOVER')
    waist = 2.7
    point(m, 'Waist', (0.0, waist, 0.0))
    skirt = frustum((0, 1.30, 0), (1.85, 0.40, 2.05), top=(0.78, 0.76), mat='armor', bevel_w=0.08)
    B.panel(skirt, 'y', 1, 0.20, -0.04, 'armor_lit')
    m.add(skirt)
    m.add(frustum((0, 0.86, 0), (1.55, 0.08, 1.70), top=(1.15, 1.16), mat='dark', bevel_w=0.04))
    # The glowing underside: the lift field.
    m.add(cyl((0, 0.80, 0), 1.30, 1.30, 0.04, 24, 'glow_cyan', bevel_w=0.0))
    for side in (1, -1):
        m.add(box((1.74 * side, 1.28, 0.1), (0.06, 0.16, 1.30), 'team', bevel_w=0.02))
    # Bow blade.
    bow = frustum((0, 1.20, 2.18), (1.10, 0.20, 0.22), top=(0.6, 0.5), shift=(0, -0.1), mat='armor_dark', bevel_w=0.05)
    m.add(bow)
    for fz, zt in ((1, 'F'), (-1, 'B')):
        for side, xt in ((1, 'R'), (-1, 'L')):
            tag = zt + xt
            c = Vector((1.72 * side, 1.00, 1.55 * fz))
            m.add(cyl((c.x, 0.62, c.z), 0.62, 0.56, 0.62, 16, 'armor_dark', bevel_w=0.05))
            m.add(tube((c.x, 1.22, c.z), 0.60, 0.50, 0.08, 16, 'steel', axis='y'))
            m.add(cyl((c.x, 0.56, c.z), 0.50, 0.50, 0.03, 16, 'glow_cyan', bevel_w=0.0))
            m.add(strut((c.x * 0.55, 1.30, c.z * 0.55), (c.x, 1.05, c.z), 0.16, 0.22, 'armor', bevel_w=0.03))
            m.add(box((c.x, 1.00, c.z + 0.58 * fz), (0.22, 0.06, 0.04), 'glow_amber', bevel_w=0.0))
            point(m, 'Jet' + tag, (c.x, 0.50, c.z))
            m.group('Fan' + tag, (c.x, 1.00, c.z))
            for k in range(5):
                a = 2 * math.pi * k / 5
                bl = box((c.x + math.cos(a) * 0.24, 1.00, c.z + math.sin(a) * 0.24), (0.20, 0.02, 0.07), 'steel', bevel_w=0.0)
                B.rot(bl, -a, 'Y', (c.x, 1.00, c.z))
                m.add(bl)
            m.add(cyl((c.x, 0.96, c.z), 0.10, 0.10, 0.10, 10, 'dark', bevel_w=0.0))
            m.group('')
    m.add(cyl((0, 1.70, 0), 1.04, 0.96, 0.44, 22, 'armor_dark', bevel_w=0.05))
    m.add(cyl((0, 2.14, 0), 0.92, 0.88, 0.40, 22, 'steel', bevel_w=0.03))
    m.add(cyl((0, 2.54, 0), 0.82, 0.82, 0.16, 22, 'dark', bevel_w=0.015))
    m.add(ring_flat((0, 2.14, 0), 0.99, 1.06, 24, 'team', thickness=0.05))
    return m


# ================================================================ frames
FRAME_MOUNTS = {
    # name: [(mount, kind, (x, y, z)) ...] at scale 1; the frame's scale multiplies them.
    'LIGHT': [('ArmR', 'arm', (1.95, 0.95, 0.30)), ('ArmL', 'arm', (-1.95, 0.95, 0.30)), ('Back', 'top', (0.0, 2.15, -1.05)),
              ('ShoulderR', 'top', (1.30, 2.30, -0.25)), ('ShoulderL', 'top', (-1.30, 2.30, -0.25))],
    'MEDIUM': [('ArmR', 'arm', (1.95, 0.95, 0.30)), ('ArmL', 'arm', (-1.95, 0.95, 0.30)), ('ShoulderR', 'top', (1.30, 2.30, -0.25)),
               ('ShoulderL', 'top', (-1.30, 2.30, -0.25)), ('Back', 'top', (0.0, 2.15, -1.05)), ('Chest', 'arm', (0.0, 0.85, 1.25))],
    'HEAVY': [('ArmR', 'arm', (1.95, 0.95, 0.30)), ('ArmL', 'arm', (-1.95, 0.95, 0.30)), ('ShoulderR', 'top', (1.30, 2.30, -0.25)),
              ('ShoulderL', 'top', (-1.30, 2.30, -0.25)), ('Back', 'top', (0.0, 2.15, -1.05)), ('Chest', 'arm', (0.0, 0.85, 1.25)),
              ('Crown', 'top', (0.0, 3.05, -0.15))],
}
FRAME_SCALE = {'LIGHT': 0.86, 'MEDIUM': 1.0, 'HEAVY': 1.14}


def build_frame(kind):
    """A Mech's torso, built round the waist it turns on.

    All three share a plan -- a wedge of a chest leaning forward over the waist, a
    cockpit nose, pauldrons over the shoulders, arms hanging to the gun mounts, a
    reactor on the back -- and differ in bulk and in what they wear: the light
    frame a bubble canopy and swept fins, the heavy a sensor dome, a raised spine
    for a crown mount and tall exhaust stacks. The groups at the end are the
    upgrades and utility modules, hidden until the Mech has them."""
    s = FRAME_SCALE[kind]
    m = Model(50 + ['LIGHT', 'MEDIUM', 'HEAVY'].index(kind), 'MECH_FRAME_' + kind)
    light, heavy = kind == 'LIGHT', kind == 'HEAVY'

    def P(x, y, z):
        return (x * s, y * s, z * s)

    def Hh(x, y, z):
        return (x * s, y * s, z * s)

    wide = 0.86 if light else (1.12 if heavy else 1.0)     # extra girth across

    for name, _, (x, y, z) in FRAME_MOUNTS[kind]:
        point(m, name, P(x, y, z))

    # Waist collar and abdomen.
    m.add(cyl((0, 0, 0), 0.86 * s, 0.80 * s, 0.30 * s, 20, 'dark', bevel_w=0.03))
    ab = frustum(P(0, 0.56, -0.05), Hh(0.80 * wide, 0.27, 0.72), top=(1.25, 1.18), mat='armor_dark', bevel_w=0.05)
    m.add(ab)
    for k in range(5):
        m.add(box(P(-0.48 + k * 0.24, 0.56, 0.80), Hh(0.07, 0.18, 0.03), 'shadowed', bevel_w=0.0))

    # The chest: a wedge that leans forward over the waist.
    chest = frustum(P(0, 1.40, -0.10), Hh(1.18 * wide, 0.62, 1.00), top=(0.84, 0.78), shift=(0, -0.10 * s), mat='armor', bevel_w=0.07)
    top = face_plate(chest, 'y', 1)
    if top:
        inner = inset(chest, top, 0.16 * s, -0.03 * s, 'armor_lit')
        if inner:
            inset(chest, inner, 0.22 * s, -0.035 * s, 'shadowed')
    B.panel(chest, 'z', 1, 0.14 * s, -0.03 * s, 'armor_lit')
    m.add(chest)
    # Team colours down the chest's flanks.
    m.add_mirrored(frustum(P(1.02 * wide, 1.38, -0.05), Hh(0.07, 0.46, 0.72), top=(1.0, 0.8), shift=(-0.1 * s, -0.08 * s), mat='team', bevel_w=0.02))

    # The cockpit.
    if light:
        m.add(frustum(P(0, 1.28, 1.00), Hh(0.52, 0.36, 0.40), top=(0.72, 0.62), shift=(0, 0.10 * s), mat='armor_lit', bevel_w=0.05))
        can = S.sphere(P(0, 1.62, 1.02), 0.42 * s, 14, 8, 'glow_cyan')
        xform(can, Matrix.Translation(Vector((0, 0, 0))))
        m.add(can)
        m.add(ring_flat(P(0, 1.52, 1.02), 0.36 * s, 0.46 * s, 16, 'dark', thickness=0.05 * s))
    else:
        nose = frustum(P(0, 1.30, 1.04), Hh(0.58 * wide, 0.42, 0.40), top=(0.62, 0.55), shift=(0, 0.12 * s), mat='armor_lit', bevel_w=0.05)
        m.add(nose)
        m.add(frustum(P(0, 1.76, 1.12), Hh(0.34 * wide, 0.05, 0.26), top=(0.72, 0.6), mat='glow_cyan', bevel_w=0.0))
        m.add(box(P(0, 1.08, 1.46), Hh(0.42, 0.03, 0.02), 'glow_amber', bevel_w=0.0))
        for side in (1, -1):
            m.add(box(P(0.44 * side * wide, 1.46, 1.40), Hh(0.05, 0.14, 0.05), 'dark', bevel_w=0.01))
    if heavy:
        dome = cyl(P(0, 2.02, 0.42), 0.44 * s, 0.30 * s, 0.34 * s, 16, 'armor_dark', bevel_w=0.04)
        m.add(dome)
        m.add(cyl(P(0, 2.36, 0.42), 0.28 * s, 0.12 * s, 0.12 * s, 16, 'armor', bevel_w=0.02))
        m.add(box(P(0, 2.20, 0.72), Hh(0.22, 0.04, 0.03), 'glow_cyan', bevel_w=0.0))
        # A raised spine carrying the crown mount.
        m.add(frustum(P(0, 2.30, -0.30), Hh(0.34, 0.50, 0.62), top=(0.7, 0.8), mat='armor_dark', bevel_w=0.04))
        m.add(box(P(0, 2.98, -0.15), Hh(0.30, 0.05, 0.30), 'dark', bevel_w=0.02))

    # Pauldrons, with a team band and a lamp.
    for side in (1, -1):
        pd = frustum(P(1.32 * side * wide, 1.78, -0.12), Hh(0.46, 0.42, 0.62), top=(0.78, 0.85), shift=(0.05 * side * s, 0), mat='armor_dark', bevel_w=0.06)
        B.panel(pd, 'y', 1, 0.10 * s, -0.025 * s, 'armor')
        m.add(pd)
        m.add(frustum(P(1.76 * side * wide, 1.72, -0.12), Hh(0.05, 0.30, 0.50), top=(1.0, 0.8), mat='team', bevel_w=0.02))
        m.add(box(P(1.30 * side * wide, 1.70, 0.52), Hh(0.16, 0.05, 0.02), 'glow_amber', bevel_w=0.0))
        # Top-mount plate.
        m.add(cyl(P(1.30 * side, 2.20, -0.25), 0.30 * s, 0.30 * s, 0.10 * s, 12, 'steel', bevel_w=0.02))

    # Arms: a shoulder drum, an upper arm, an elbow housing the gun attaches to.
    for side in (1, -1):
        sh = Vector(P(1.64 * side * wide, 1.52, 0.02))
        mount = Vector(P(1.95 * side, 0.95, 0.30))
        m.add(cyl((sh.x - 0.22 * s if side > 0 else sh.x - 0.22 * s, sh.y, sh.z), 0.30 * s, 0.30 * s, 0.44 * s, 14, 'dark', axis='x', bevel_w=0.03))
        m.add(strut(sh, mount + Vector((0, 0.18 * s, -0.12 * s)), 0.20 * s, 0.22 * s, 'armor', taper=(0.85, 0.9), bevel_w=0.04))
        piston(m, sh + Vector((0.10 * side * s, 0.10 * s, 0.26 * s)), mount + Vector((0.10 * side * s, 0.30 * s, 0.10 * s)), 0.06 * s)
        m.add(box(tuple(mount + Vector((0, 0.10 * s, -0.10 * s))), Hh(0.24, 0.26, 0.30), 'armor_dark', bevel_w=0.04))

    # The reactor on the back, with vents and stacks.
    rea = frustum(P(0, 1.55, -1.02), Hh(0.70 * wide, 0.55, 0.40), top=(0.9, 0.85), mat='armor_dark', bevel_w=0.05)
    m.add(rea)
    m.add(cyl(P(0, 2.06, -1.05), 0.32 * s, 0.32 * s, 0.09 * s, 12, 'steel', bevel_w=0.02))
    for k in range(4):
        m.add(box(P(-0.39 + k * 0.26, 1.50, -1.43), Hh(0.08, 0.36, 0.03), 'shadowed', bevel_w=0.0))
    m.add(box(P(0, 1.10, -1.43), Hh(0.44, 0.04, 0.02), 'glow_warm', bevel_w=0.0))
    stack_h = 1.3 if heavy else 0.8
    for side in (1, -1):
        st = tube(P(0.48 * side * wide, 1.50, -1.30), 0.14 * s, 0.10 * s, stack_h * s, 10, 'dark', axis='y')
        B.rot(st, math.radians(-12), 'X', P(0.48 * side * wide, 1.5, -1.30))
        m.add(st)
        point(m, 'Stack' + ('R' if side > 0 else 'L'), P(0.48 * side * wide, 1.5 + stack_h * 0.98, -1.30 - stack_h * 0.2))
    if light:
        for side in (1, -1):
            fin = frustum(P(0.62 * side, 2.08, -0.62), Hh(0.04, 0.30, 0.34), top=(1.0, 0.4), shift=(0, -0.25 * s), mat='team', bevel_w=0.015)
            m.add(fin)
    # An antenna and a lit tip.
    m.add(rod(P(-0.62 * wide, 1.98, -0.52), P(-0.62 * wide, 2.78, -0.60), 0.025 * s, 0.018 * s, 6, 'steel', bevel_w=0.0))
    m.add(box(P(-0.62 * wide, 2.80, -0.60), Hh(0.035, 0.035, 0.035), 'glow_amber', bevel_w=0.0))
    point(m, 'Cockpit', P(0, 1.60, 1.25))
    point(m, 'Reactor', P(0, 1.55, -1.45))

    # The order's banner: a pole off the back with the standard hanging from a
    # crossbar -- the side's colour, a lit sigil, dark trim. It hangs from the top
    # of the pole (its group's pivot), so MechView can let it swing as the Mech walks.
    bx = -0.62 * wide
    m.add(rod(P(bx, 1.60, -1.28), P(bx, 3.55, -1.34), 0.035 * s, 0.03 * s, 8, 'steel', bevel_w=0.0))
    m.add(box(P(bx, 3.58, -1.34), Hh(0.05, 0.05, 0.05), 'glow_amber', bevel_w=0.0))
    m.group('Banner', P(bx, 3.40, -1.34))
    m.add(box(P(bx, 3.40, -1.34), Hh(0.34, 0.025, 0.025), 'dark', bevel_w=0.0))
    m.add(box(P(bx, 2.82, -1.35), Hh(0.30, 0.56, 0.018), 'team', bevel_w=0.008))
    m.add(box(P(bx, 2.24, -1.35), Hh(0.30, 0.04, 0.02), 'dark', bevel_w=0.0))
    for side in (1, -1):
        m.add(box(P(bx + 0.31 * side, 2.82, -1.35), Hh(0.02, 0.58, 0.02), 'dark', bevel_w=0.0))
    em = box(P(bx, 2.90, -1.37), Hh(0.11, 0.11, 0.012), 'glow_warm', bevel_w=0.0)
    B.rot(em, math.radians(45), 'Z', P(bx, 2.90, -1.37))
    m.add(em)
    m.add(box(P(bx, 2.90, -1.33), Hh(0.14, 0.14, 0.01), 'armor_lit', bevel_w=0.0))
    m.group('')
    point(m, 'Banner', P(bx, 3.40, -1.34))

    # ---- upgrades and modules, hidden until the Mech has them
    m.group('Plates1')     # Ablative Armour I: slabs over the chest's front corners
    for side in (1, -1):
        pl = frustum(P(0.74 * side * wide, 1.30, 0.92), Hh(0.34, 0.42, 0.08), top=(0.8, 0.9), mat='armor_lit', bevel_w=0.03)
        B.rot(pl, math.radians(20 * side), 'Y', P(0.74 * side * wide, 1.30, 0.92))
        m.add(pl)
        m.add(box(P(0.74 * side * wide, 1.62, 1.00), Hh(0.05, 0.05, 0.05), 'steel', bevel_w=0.0))
    m.group('Plates2')     # II: over-plates on the pauldrons
    for side in (1, -1):
        m.add(frustum(P(1.84 * side * wide, 1.84, -0.12), Hh(0.08, 0.44, 0.58), top=(1.0, 0.85), shift=(0.03 * side * s, 0), mat='armor_lit', bevel_w=0.03))
    m.group('Plates3')     # III: a skirt round the abdomen
    for k in range(-2, 3):
        a = math.radians(k * 26)
        pl = box(P(math.sin(a) * 1.04 * wide, 0.46, math.cos(a) * 0.90), Hh(0.20, 0.24, 0.05), 'armor', bevel_w=0.02)
        B.rot(pl, a, 'Y', P(math.sin(a) * 1.04 * wide, 0.46, math.cos(a) * 0.90))
        m.add(pl)
    m.group('Uplink')      # Targeting Uplink: a dish on the back
    m.add(rod(P(0.64 * wide, 1.98, -0.70), P(0.64 * wide, 2.62, -0.72), 0.05 * s, 0.05 * s, 8, 'steel'))
    dish = cyl(P(0.64 * wide, 2.62, -0.72), 0.06 * s, 0.46 * s, 0.20 * s, 16, 'armor_lit', bevel_w=0.0)
    B.rot(dish, math.radians(-35), 'X', P(0.64 * wide, 2.62, -0.72))
    m.add(dish)
    m.add(box(P(0.64 * wide, 2.75, -0.58), Hh(0.04, 0.04, 0.04), 'glow_cyan', bevel_w=0.0))
    m.group('Mast')        # Sensor Mast
    m.add(rod(P(0.30, 2.00, -0.84), P(0.30, 3.30, -0.86), 0.05 * s, 0.03 * s, 8, 'steel', bevel_w=0.0))
    for k in range(3):
        m.add(box(P(0.30, 2.50 + k * 0.30, -0.85), Hh(0.26 - k * 0.06, 0.02, 0.02), 'armor_dark', bevel_w=0.0))
    m.add(box(P(0.30, 3.33, -0.86), Hh(0.05, 0.05, 0.05), 'glow_cyan', bevel_w=0.0))
    m.group('Emitters')    # Shield Projector: two emitter horns
    for side in (1, -1):
        m.add(rod(P(0.88 * side * wide, 2.00, -0.80), P(1.02 * side * wide, 2.62, -0.95), 0.07 * s, 0.05 * s, 8, 'armor_dark'))
        m.add(S.sphere(P(1.03 * side * wide, 2.68, -0.96), 0.12 * s, 10, 6, 'glow_cyan'))
    m.group('Nanites')     # Repair Nanites: canisters on the reactor
    for k in range(3):
        m.add(cyl(P(-0.40 + k * 0.40, 1.10, -1.52), 0.12 * s, 0.12 * s, 0.50 * s, 10, 'steel', bevel_w=0.02))
        m.add(cyl(P(-0.40 + k * 0.40, 1.28, -1.52), 0.125 * s, 0.125 * s, 0.10 * s, 10, 'glow_cyan', bevel_w=0.0))
    m.group('Reactive')    # Reactive Armour: tiles over the chest
    for i in range(4):
        for j in range(2):
            m.add(box(P(-0.54 + i * 0.36, 1.98, -0.10 + j * 0.42), Hh(0.15, 0.04, 0.18), 'armor_dark', bevel_w=0.015))
    m.group('')
    return m


# ================================================================ weapons
def mount_block(m, depth=0.30):
    m.add(box((0, 0.10, 0.02), (0.24, 0.26, depth), 'armor_dark', bevel_w=0.04))


def build_w_autocannon():
    m = Model(60, 'MECH_W_AUTOCANNON')
    mount_block(m)
    hs = box((0, 0.0, 0.55), (0.30, 0.27, 0.50), 'armor', bevel_w=0.05)
    B.panel(hs, 'y', 1, 0.07, -0.025, 'armor_lit')
    m.add(hs)
    m.add(box((0.31, 0.02, 0.55), (0.02, 0.10, 0.34), 'team', bevel_w=0.01))
    m.add(cyl((0.30, -0.08, 0.35), 0.24, 0.24, 0.26, 14, 'armor_dark', axis='x', bevel_w=0.03))
    m.add(box((0.30, 0.14, 0.60), (0.05, 0.08, 0.20), 'dark', bevel_w=0.01))
    m.group('Barrel', (0, 0, 1.0))
    m.add(cyl((0, 0, 1.00), 0.19, 0.19, 0.34, 12, 'armor_dark', axis='z', bevel_w=0.03))
    m.add(cyl((0, 0, 1.30), 0.12, 0.10, 0.80, 12, 'steel', axis='z', bevel_w=0.015))
    m.add(cyl((0, 0, 2.06), 0.17, 0.17, 0.26, 12, 'dark', axis='z', bevel_w=0.025))
    for side in (1, -1):
        m.add(box((0.15 * side, 0, 2.14), (0.04, 0.07, 0.035), 'shadowed', bevel_w=0.0))
    m.group('')
    point(m, 'Muzzle', (0, 0, 2.34))
    return m


def build_w_gatling():
    m = Model(61, 'MECH_W_GATLING')
    mount_block(m)
    m.add(cyl((0, 0, 0.10), 0.32, 0.30, 0.78, 14, 'armor', axis='z', bevel_w=0.04))
    m.add(box((0, 0.30, 0.40), (0.16, 0.10, 0.34), 'armor_dark', bevel_w=0.02))
    m.add(box((0.30, 0.0, 0.42), (0.03, 0.12, 0.28), 'team', bevel_w=0.01))
    m.add(cyl((0, 0, 0.86), 0.20, 0.20, 0.10, 12, 'dark', axis='z', bevel_w=0.02))
    m.group('Spin', (0, 0, 0))
    for k in range(6):
        a = 2 * math.pi * k / 6
        m.add(cyl((math.cos(a) * 0.13, math.sin(a) * 0.13, 0.92), 0.048, 0.048, 1.12, 8, 'steel', axis='z', bevel_w=0.0))
    for z in (1.30, 1.92):
        m.add(cyl((0, 0, z), 0.215, 0.215, 0.08, 12, 'dark', axis='z', bevel_w=0.01))
    m.add(cyl((0, 0, 0.92), 0.07, 0.07, 1.10, 8, 'armor_dark', axis='z', bevel_w=0.0))
    m.group('')
    point(m, 'Muzzle', (0, 0, 2.06))
    return m


def build_w_missiles():
    m = Model(62, 'MECH_W_MISSILES')
    m.add(cyl((0, 0, 0), 0.30, 0.28, 0.14, 12, 'dark', bevel_w=0.02))
    body = frustum((0, 0.42, 0), (0.50, 0.30, 0.58), top=(0.96, 0.92), mat='armor_dark', bevel_w=0.05)
    m.add(body)
    for side in (1, -1):
        m.add(box((0.51 * side, 0.42, 0), (0.02, 0.18, 0.44), 'team', bevel_w=0.01))
    m.add(box((0, 0.73, 0), (0.46, 0.02, 0.52), 'shadowed', bevel_w=0.0))
    for i in range(4):
        for j in range(2):
            x, z = -0.33 + i * 0.22, -0.22 + j * 0.44
            m.add(cyl((x, 0.72, z), 0.085, 0.085, 0.05, 10, 'dark', bevel_w=0.0))
            m.add(cyl((x, 0.735, z), 0.05, 0.05, 0.02, 8, 'glow_amber', bevel_w=0.0))
    m.add(box((0, 0.44, 0.59), (0.30, 0.06, 0.02), 'glow_amber', bevel_w=0.0))
    point(m, 'Muzzle', (0, 0.78, 0))
    return m


def build_w_mortar():
    m = Model(63, 'MECH_W_MORTAR')
    m.add(cyl((0, 0, 0), 0.42, 0.38, 0.18, 14, 'armor_dark', bevel_w=0.03))
    for side in (1, -1):
        m.add(frustum((0.30 * side, 0.36, 0), (0.06, 0.20, 0.26), top=(1.0, 0.6), mat='armor', bevel_w=0.02))
    d = Vector((0, math.sin(math.radians(55)), math.cos(math.radians(55))))
    base = Vector((0, 0.38, -0.10))
    m.group('Tube', tuple(base))
    m.add(rod(base - d * 0.25, base + d * 0.30, 0.30, 0.30, 14, 'armor_dark', bevel_w=0.03))
    m.add(rod(base + d * 0.25, base + d * 1.45, 0.24, 0.22, 14, 'armor', bevel_w=0.02))
    m.add(rod(base + d * 1.40, base + d * 1.62, 0.29, 0.29, 14, 'dark', bevel_w=0.02))
    m.add(rod(base + d * 0.70, base + d * 0.80, 0.27, 0.27, 14, 'team', bevel_w=0.0))
    m.group('')
    point(m, 'Muzzle', tuple(base + d * 1.66))
    return m


def build_w_laser():
    m = Model(64, 'MECH_W_LASER')
    mount_block(m)
    body = box((0, 0, 0.48), (0.26, 0.25, 0.50), 'armor', bevel_w=0.05)
    B.panel(body, 'y', 1, 0.06, -0.02, 'armor_lit')
    m.add(body)
    m.add(box((0.27, 0.0, 0.46), (0.02, 0.10, 0.30), 'team', bevel_w=0.01))
    m.add(cyl((0, 0, 0.95), 0.12, 0.10, 1.62, 12, 'steel', axis='z', bevel_w=0.015))
    for k, z in enumerate((1.12, 1.38, 1.64, 1.90)):
        m.add(cyl((0, 0, z), 0.20 - k * 0.012, 0.20 - k * 0.012, 0.08, 12, 'glow_cyan', axis='z', bevel_w=0.0))
    for side in (1, -1):
        for k in range(3):
            m.add(box((0, 0.22 * side, 0.30 + k * 0.22), (0.20, 0.03, 0.06), 'shadowed', bevel_w=0.0))
    m.add(cyl((0, 0, 2.52), 0.16, 0.12, 0.20, 12, 'dark', axis='z', bevel_w=0.02))
    m.add(cyl((0, 0, 2.70), 0.09, 0.09, 0.02, 12, 'glow_cyan', axis='z', bevel_w=0.0))
    point(m, 'Muzzle', (0, 0, 2.74))
    return m


def build_w_flamer():
    m = Model(65, 'MECH_W_FLAMER')
    mount_block(m)
    for side in (1, -1):
        m.add(cyl((0.18 * side, 0.20, -0.20), 0.14, 0.14, 0.78, 12, 'rust', axis='z', bevel_w=0.02))
        m.add(cyl((0.18 * side, 0.20, 0.58), 0.10, 0.06, 0.08, 10, 'steel', axis='z', bevel_w=0.0))
    m.add(box((0, -0.06, 0.72), (0.21, 0.20, 0.42), 'armor_dark', bevel_w=0.04))
    m.add(box((0.22, -0.06, 0.72), (0.02, 0.10, 0.26), 'team', bevel_w=0.01))
    m.add(cyl((0, -0.06, 1.12), 0.14, 0.09, 0.66, 10, 'steel', axis='z', bevel_w=0.015))
    m.add(cyl((0, -0.06, 1.76), 0.11, 0.13, 0.12, 10, 'dark', axis='z', bevel_w=0.015))
    m.add(box((0, -0.20, 1.76), (0.03, 0.03, 0.05), 'glow_warm', bevel_w=0.0))
    point(m, 'Muzzle', (0, -0.06, 1.92))
    return m


def build_w_railgun():
    m = Model(66, 'MECH_W_RAILGUN')
    mount_block(m)
    br = box((0, 0.02, 0.40), (0.30, 0.30, 0.56), 'armor_dark', bevel_w=0.05)
    B.panel(br, 'y', 1, 0.07, -0.025, 'armor')
    m.add(br)
    for side in (1, -1):
        for k in range(3):
            m.add(cyl((0.31 * side, 0.02, 0.10 + k * 0.26), 0.11, 0.11, 0.06, 10, 'glow_cyan', axis='x', bevel_w=0.0) if side < 0 else
                  cyl((0.25, 0.02, 0.10 + k * 0.26), 0.11, 0.11, 0.06, 10, 'glow_cyan', axis='x', bevel_w=0.0))
    m.add(box((0, 0.33, 0.40), (0.14, 0.03, 0.40), 'team', bevel_w=0.01))
    m.group('Barrel', (0, 0.05, 0.95))
    for side in (1, -1):
        m.add(box((0.12 * side, 0.05, 2.25), (0.05, 0.11, 1.40), 'steel', bevel_w=0.015))
    for k in range(5):
        z = 1.05 + k * 0.62
        m.add(box((0, 0.05, z), (0.21, 0.15, 0.05), 'armor', bevel_w=0.015))
    m.add(box((0, 0.05, 2.25), (0.025, 0.025, 1.34), 'glow_cyan', bevel_w=0.0))
    m.group('')
    point(m, 'Muzzle', (0, 0.05, 3.72))
    return m


def build_w_flametower():
    """The Flame Tower: a flame turret on its own ring, on a shoulder or the back. The
    Turret group turns on its own (MechView swings it onto its target), so it can
    burn what comes up behind the Mech while its arms fight ahead."""
    m = Model(67, 'MECH_W_FLAMETOWER')
    m.add(cyl((0, 0, 0), 0.44, 0.40, 0.14, 16, 'armor_dark', bevel_w=0.03))
    m.add(cyl((0, 0.14, 0), 0.37, 0.37, 0.03, 16, 'team', bevel_w=0.0))
    m.group('Turret', (0, 0.17, 0))
    housing = frustum((0, 0.42, -0.05), (0.36, 0.25, 0.42), top=(0.82, 0.74), mat='armor', bevel_w=0.04)
    B.panel(housing, 'y', 1, 0.06, -0.02, 'armor_lit')
    m.add(housing)
    for side in (1, -1):
        # Cheek plates, the nozzles with their pilot lights, and the gel tanks behind.
        m.add(box((0.37 * side, 0.40, 0.05), (0.03, 0.18, 0.30), 'armor_dark', bevel_w=0.015))
        m.add(cyl((0.14 * side, 0.46, 0.30), 0.075, 0.06, 0.66, 10, 'steel', axis='z', bevel_w=0.01))
        m.add(cyl((0.14 * side, 0.46, 0.94), 0.09, 0.10, 0.10, 10, 'dark', axis='z', bevel_w=0.01))
        m.add(box((0.14 * side, 0.36, 0.99), (0.025, 0.02, 0.03), 'glow_warm', bevel_w=0.0))
        m.add(cyl((0.21 * side, 0.40, -0.88), 0.13, 0.13, 0.52, 12, 'rust', axis='z', bevel_w=0.02))
        m.add(cyl((0.21 * side, 0.40, -0.38), 0.09, 0.09, 0.05, 10, 'steel', axis='z', bevel_w=0.0))
        m.add(rod((0.21 * side, 0.52, -0.40), (0.14 * side, 0.52, 0.28), 0.03, 0.03, 6, 'dark', bevel_w=0.0))
    m.add(box((0, 0.68, -0.05), (0.20, 0.02, 0.26), 'team', bevel_w=0.01))
    m.add(box((0, 0.69, 0.18), (0.05, 0.02, 0.05), 'glow_warm', bevel_w=0.0))
    m.group('')
    point(m, 'Muzzle', (0, 0.46, 1.08))
    return m


# ================================================================ the Mech Bay
def build_mech_bay():
    """The Mech Bay: a servicing gantry and drop beacon on an octagonal pad.

    Two tall towers carry a bridge with a crane trolley (UnitView's Trolley) and
    the repair arms; a radar dish turns on the right tower (Head); the gun tower
    stands at the rear left, its head a separate model (MECH_BAY_GUN). Banners in
    the team's colour hang from the towers: the order's standard."""
    m = Model(70, 'MECH_BAY')
    m.add(cyl((0, 0, 0), 4.20, 4.00, 0.34, 8, 'dark', bevel_w=0.05))
    deck = cyl((0, 0.34, 0), 3.80, 3.72, 0.14, 8, 'armor_dark', bevel_w=0.03)
    m.add(deck)
    m.add(ring_flat((0, 0.48, 0), 3.30, 3.62, 8, 'team', thickness=0.03))
    for k in range(8):
        a = B.face_angle(k, 8)
        m.add(box((math.cos(a) * 3.95, 0.20, math.sin(a) * 3.95), (0.10, 0.06, 0.10), 'glow_amber', bevel_w=0.0))
    # The cradle in the middle: a lift platform with a lit ring.
    m.add(cyl((0, 0.48, 0.3), 1.70, 1.62, 0.18, 16, 'steel', bevel_w=0.03))
    m.add(ring_flat((0, 0.66, 0.3), 1.30, 1.46, 20, 'glow_cyan', thickness=0.02))
    for side in (1, -1):
        x = 2.85 * side
        # Tower: a tapering lattice-ish column of stacked blocks.
        m.add(frustum((x, 2.20, -0.70), (0.62, 2.20, 0.70), top=(0.62, 0.66), mat='armor', bevel_w=0.06))
        B_panel = frustum((x, 4.70, -0.70), (0.48, 0.30, 0.52), top=(0.8, 0.8), mat='armor_dark', bevel_w=0.05)
        m.add(B_panel)
        for k in range(4):
            m.add(box((x - 0.63 * side, 0.9 + k * 0.95, -0.70), (0.02, 0.30, 0.40), 'shadowed', bevel_w=0.0))
        # The banner: the order's standard in the team's colour, with a lit sigil.
        m.add(box((x + 0.66 * side, 2.60, -0.70), (0.03, 1.30, 0.38), 'team', bevel_w=0.01))
        m.add(box((x + 0.70 * side, 3.05, -0.70), (0.02, 0.18, 0.18), 'glow_warm', bevel_w=0.0))
        m.add(box((x + 0.70 * side, 1.40, -0.70), (0.02, 0.05, 0.30), 'dark', bevel_w=0.0))
        # Buttresses and pipes.
        m.add(strut((x, 0.4, 0.8), (x, 2.8, -0.05), 0.10, 0.18, 'armor_dark', bevel_w=0.03))
        m.add(rod((x - 0.40 * side, 0.45, -1.5), (x - 0.40 * side, 4.3, -1.5), 0.08, 0.08, 8, 'steel', bevel_w=0.0))
    # The bridge across the towers, with the crane trolley and the repair arms.
    m.add(box((0, 5.20, -0.70), (3.30, 0.26, 0.34), 'armor_dark', bevel_w=0.05))
    m.add(box((0, 5.48, -0.70), (3.10, 0.04, 0.10), 'team', bevel_w=0.01))
    for k in range(7):
        m.add(box((-2.4 + k * 0.8, 4.92, -0.70), (0.04, 0.04, 0.30), 'glow_amber' if k % 2 == 0 else 'dark', bevel_w=0.0))
    m.group('Trolley', (-1.7, 4.85, -0.70))
    m.add(box((-1.7, 4.80, -0.70), (0.42, 0.18, 0.42), 'steel', bevel_w=0.03))
    m.add(rod((-1.7, 4.62, -0.70), (-1.7, 3.30, -0.70), 0.05, 0.05, 8, 'dark', bevel_w=0.0))
    m.add(box((-1.7, 3.20, -0.70), (0.24, 0.12, 0.24), 'armor_dark', bevel_w=0.02))
    m.add(box((-1.7, 3.06, -0.70), (0.10, 0.05, 0.10), 'glow_cyan', bevel_w=0.0))
    m.group('')
    # Repair arms reaching out over the front.
    for side in (1, -1):
        base = Vector((1.9 * side, 5.0, -0.4))
        elbow = Vector((2.3 * side, 5.6, 1.6))
        hand = Vector((2.0 * side, 4.2, 3.0))
        m.add(strut(base, elbow, 0.12, 0.14, 'armor', bevel_w=0.03))
        m.add(strut(elbow, hand, 0.09, 0.10, 'armor_dark', bevel_w=0.02))
        m.add(cyl(tuple(hand - Vector((0, 0.25, 0))), 0.12, 0.05, 0.25, 8, 'steel', bevel_w=0.0))
        m.add(box(tuple(hand - Vector((0, 0.30, 0))), (0.05, 0.05, 0.05), 'glow_cyan', bevel_w=0.0))
        point(m, 'Arm' + ('R' if side > 0 else 'L'), tuple(hand - Vector((0, 0.32, 0))))
    # The radar on the right tower.
    m.add(cyl((2.85, 5.00, -0.70), 0.18, 0.18, 0.60, 10, 'steel', bevel_w=0.0))
    m.group('Head', (2.85, 5.60, -0.70))
    dish = cyl((2.85, 5.60, -0.70), 0.08, 0.62, 0.22, 16, 'armor_lit', bevel_w=0.0)
    B.rot(dish, math.radians(-60), 'X', (2.85, 5.60, -0.70))
    m.add(dish)
    m.add(box((2.85, 5.70, -0.40), (0.05, 0.05, 0.05), 'glow_amber', bevel_w=0.0))
    m.group('')
    # The gun tower, rear left.
    gx, gz = -2.55, -2.35
    m.add(cyl((gx, 0.34, gz), 1.05, 0.86, 1.8, 8, 'armor_dark', bevel_w=0.05))
    m.add(cyl((gx, 2.14, gz), 0.78, 0.66, 2.5, 8, 'armor', bevel_w=0.05))
    m.add(ring_flat((gx, 3.2, gz), 0.72, 0.84, 8, 'team', thickness=0.06))
    m.add(cyl((gx, 4.64, gz), 0.70, 0.70, 0.14, 12, 'steel', bevel_w=0.02))
    point(m, 'GunMount', (gx, 4.78, gz))
    # Containers and a fuel stack for bulk.
    m.add(box((2.3, 0.85, -2.6), (0.70, 0.40, 0.45), 'rust', bevel_w=0.04))
    m.add(box((2.3, 1.40, -2.6), (0.60, 0.16, 0.40), 'armor_dark', bevel_w=0.03))
    m.add(cyl((0.4, 0.48, -2.9), 0.42, 0.42, 1.6, 12, 'steel', bevel_w=0.03))
    m.add(cyl((1.2, 0.48, -2.9), 0.36, 0.36, 1.3, 12, 'steel', bevel_w=0.03))
    point(m, 'Beacon', (0, 0.70, 0.3))
    return m


def build_mech_bay_gun():
    m = Model(71, 'MECH_BAY_GUN')
    body = frustum((0, 0.40, -0.10), (0.62, 0.36, 0.70), top=(0.74, 0.72), shift=(0, -0.08), mat='armor', bevel_w=0.05)
    B.panel(body, 'y', 1, 0.10, -0.02, 'armor_lit')
    m.add(body)
    m.add(box((0, 0.80, -0.55), (0.40, 0.03, 0.18), 'team', bevel_w=0.01))
    m.add(box((0, 0.46, 0.62), (0.30, 0.06, 0.02), 'glow_amber', bevel_w=0.0))
    m.group('Barrels', (0, 0.42, 0.55))
    for side in (1, -1):
        m.add(cyl((0.24 * side, 0.42, 0.50), 0.10, 0.085, 1.30, 12, 'steel', axis='z', bevel_w=0.015))
        m.add(cyl((0.24 * side, 0.42, 1.74), 0.13, 0.13, 0.22, 12, 'dark', axis='z', bevel_w=0.02))
    m.group('')
    return m


BUILDERS = [build_legs_biped, build_legs_quad, build_tracks, build_hover,
            lambda: build_frame('LIGHT'), lambda: build_frame('MEDIUM'), lambda: build_frame('HEAVY'),
            build_w_autocannon, build_w_gatling, build_w_missiles, build_w_mortar, build_w_laser, build_w_flamer, build_w_railgun,
            build_w_flametower,
            build_mech_bay, build_mech_bay_gun]

CHUNKS = {'MECH_BAY': 10}


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out_dir = argv[0] if argv else os.path.join(os.getcwd(), 'Assets', 'StarForge', 'Art', 'Models')
    blend_path = argv[1] if len(argv) > 1 else None
    os.makedirs(out_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)

    models = [fn() for fn in BUILDERS]   # all built before any is exported (see export_fbx)
    meta = {}
    for i, model in enumerate(models):
        slots, rad, top = E.prepare(model)
        E.bake_ao(model, rad)
        objs, tris = E.export_groups(model, slots, out_dir)
        chunks = E.export_chunks(model, slots, CHUNKS[model.name], out_dir) if model.name in CHUNKS else []
        for o in objs[1:]:
            o.name = '%s_%s' % (model.name, o.name)
        for o in chunks:
            o.name = '%s_%s' % (model.name, o.name)
            o.location += Vector((i * 8.0, 14.0, 0.0))
        objs[0].location.x = i * 8.0
        meta[model.name] = {'radius': round(rad, 3), 'height': round(top, 3), 'triangles': tris,
                            'materials': slots,
                            'groups': {g: [round(c, 3) for c in p] for g, p in model.groups.items()},
                            'points': {k: list(v) for k, v in POINTS.get(model.name, {}).items()},
                            'chunks': len(chunks)}
        print('%-20s tris=%6d radius=%5.2f height=%5.2f groups=%s points=%s' % (
            model.name, tris, rad, top, ','.join(sorted(model.groups)) or '-', ','.join(sorted(POINTS.get(model.name, {}))) or '-'))
    frames = {k: {'scale': FRAME_SCALE[k], 'mounts': [[n, kd, [c * FRAME_SCALE[k] for c in xyz]] for n, kd, xyz in v]}
              for k, v in FRAME_MOUNTS.items()}
    meta['_frames'] = frames
    with open(os.path.join(out_dir, 'mechs.json'), 'w') as fh:
        json.dump(meta, fh, indent=2)
    if blend_path:
        bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    print('exported %d mech models to %s' % (len(models), out_dir))


if __name__ == '__main__':
    main()
