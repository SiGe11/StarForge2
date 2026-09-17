"""build_models_ext.py -- models added for the Unity edition.

Same authoring rules as build_models.py: game space, +Y up, +Z forward, origin
on the ground at the model's centre, silhouette first. These use the same
primitive library, so they sit beside the originals without a style shift.

    SKIMMER        fast hover raider (trained at the Garrison)
    SENTINEL_BASE  defensive turret plinth (static)
    SENTINEL_HEAD  rotating gun housing; pivot at its origin, mounted 1.40 up
"""

import math

import build_models as B
import sf_model as S
from sf_model import Model, box, frustum, cyl, ring_flat, inset, face_plate

MESH_SKIMMER, MESH_SENTINEL_BASE, MESH_SENTINEL_HEAD = 20, 21, 22

SENTINEL_HEAD_MOUNT_Y = 1.40


def build_skimmer():
    """Skimmer: an arrowhead fuselage slung between two engine pods.

    At RTS distance it must read as *fast* and as different from the tracked
    Mauler, so the outline is long and forked: two pods trailing past the
    tail and a pointed nose, with the glowing exhausts and hover pads giving
    it a light signature the ground units do not have.
    """
    m = Model(MESH_SKIMMER, 'SKIMMER')

    hull = frustum((0, 0.74, -0.10), (0.40, 0.19, 1.00), top=(0.62, 0.84),
                   shift=(0.0, -0.06), mat='armor', bevel_w=0.05)
    deck = face_plate(hull, 'y', 1)
    if deck:
        inset(hull, deck, 0.07, -0.02, 'armor_lit')
    m.add(hull)
    m.add(frustum((0, 0.70, 1.12), (0.28, 0.13, 0.24), top=(0.30, 0.45),
                  shift=(0.0, 0.10), mat='armor_lit', bevel_w=0.03))
    # Canopy and a team spine running back from it.
    m.add(frustum((0, 0.98, 0.22), (0.19, 0.09, 0.34), top=(0.55, 0.55),
                  shift=(0.0, -0.06), mat='dark', bevel_w=0.03))
    m.add(box((0, 0.98, -0.42), (0.22, 0.04, 0.30), 'team', bevel_w=0.015))

    for side in (1, -1):
        # Pylon out to the pod.
        m.add(box((0.52 * side, 0.74, -0.30), (0.20, 0.05, 0.24), 'armor_dark',
                  bevel_w=0.02))
        # Engine pod: a tapered drum with an intake cone and a lit exhaust.
        m.add(cyl((0.80 * side, 0.72, -1.08), 0.22, 0.26, 1.38, 10, 'armor_dark',
                  axis='z', bevel_w=0.03))
        m.add(cyl((0.80 * side, 0.72, 0.30), 0.26, 0.10, 0.32, 10, 'armor',
                  axis='z', bevel_w=0.02))
        m.add(cyl((0.80 * side, 0.72, -1.15), 0.20, 0.20, 0.07, 10, 'glow_cyan',
                  axis='z', bevel_w=0.0))
        band_r = S.cone_radius_at(0.22, 0.26, 1.38, 0.55) + 0.03
        m.add(cyl((0.80 * side, 0.72, -0.53), band_r, band_r, 0.16, 10, 'team',
                  axis='z', bevel_w=0.0))
        # Hover pad under each pod.
        m.add(cyl((0.80 * side, 0.40, -0.40), 0.19, 0.15, 0.06, 10, 'glow_cyan',
                  bevel_w=0.0))
        # Twin cannons under the nose.
        m.add(cyl((0.20 * side, 0.56, 0.50), 0.045, 0.045, 0.86, 8, 'steel',
                  axis='z', bevel_w=0.0))
        m.add(box((0.20 * side, 0.56, 1.38), (0.06, 0.06, 0.04), 'dark', bevel_w=0.0))
        # Tail fin on each pod.
        m.add(frustum((0.80 * side, 1.08, -0.80), (0.03, 0.22, 0.28), top=(1.0, 0.5),
                      shift=(0.0, -0.18), mat='team_dark', bevel_w=0.01))

    # Sensor mast: the Skimmer is the scout, so it wears the antenna.
    m.add(cyl((0, 1.02, -0.66), 0.045, 0.03, 0.44, 6, 'steel', bevel_w=0.0))
    m.add(box((0, 1.48, -0.66), (0.12, 0.025, 0.025), 'glow_amber', bevel_w=0.0))
    return m


def build_sentinel_base():
    """Sentinel plinth: an octagonal bastion with corner buttresses.

    Low and wide so the rotating head is the part that reads; the buttresses
    give the footprint corners in plan view, which separates it at a glance
    from the round bunkhouse.
    """
    m = Model(MESH_SENTINEL_BASE, 'SENTINEL_BASE')

    m.add(cyl((0, 0, 0), 1.74, 1.64, 0.28, 8, 'dark', bevel_w=0.04))
    m.add(cyl((0, 0.28, 0), 1.46, 1.18, 0.50, 8, 'armor', bevel_w=0.05))
    for i in range(4):
        a = B.face_angle(i * 2, 8)
        r = 1.28
        bt = frustum((math.cos(a) * r, 0.42, math.sin(a) * r), (0.40, 0.42, 0.26),
                     top=(0.55, 0.70), shift=(-0.12, 0.0), mat='armor_dark',
                     bevel_w=0.035)
        B.orient_radial(bt, a)
        m.add(bt)
        m.add(box((math.cos(a) * 1.58, 0.30, math.sin(a) * 1.58), (0.06, 0.05, 0.06),
                  'glow_amber', bevel_w=0.0))
    # 0.08 thick, so the collar's top clears the buttress tops at 0.84 instead
    # of sitting flush with them.
    m.add(ring_flat((0, 0.78, 0), 0.92, 1.22, 16, 'team', thickness=0.08))
    m.add(cyl((0, 0.78, 0), 0.90, 0.76, 0.52, 8, 'armor_dark', bevel_w=0.04))
    glow_r = S.cone_radius_at(0.90, 0.76, 0.52, 0.30) + 0.03
    m.add(cyl((0, 1.04, 0), glow_r, glow_r, 0.08, 8, 'glow_cyan', caps=False,
              bevel_w=0.0))
    m.add(cyl((0, 1.30, 0), 0.68, 0.68, 0.10, 16, 'steel', bevel_w=0.02))
    return m


def build_sentinel_head():
    """Sentinel gun housing: a sloped block with twin long barrels.

    Built around its own origin so the game can spin it; front-heavy barrels
    and a back-mounted radar make the facing obvious from any angle.
    """
    m = Model(MESH_SENTINEL_HEAD, 'SENTINEL_HEAD')

    body = frustum((0, 0.36, -0.10), (0.60, 0.30, 0.68), top=(0.72, 0.70),
                   shift=(0.0, -0.08), mat='armor', bevel_w=0.05)
    roof = face_plate(body, 'y', 1)
    if roof:
        inset(body, roof, 0.10, -0.025, 'armor_lit')
    m.add(body)
    for side in (1, -1):
        m.add(frustum((0.62 * side, 0.34, 0.08), (0.14, 0.24, 0.44), top=(0.6, 0.8),
                      shift=(-0.06 * side, -0.04), mat='armor_dark', bevel_w=0.03))
    # The barrels recoil together.
    m.group('Barrels', (0.0, 0.38, 0.45))
    for side in (1, -1):
        m.add(cyl((0.24 * side, 0.38, 0.50), 0.09, 0.075, 1.05, 10, 'steel',
                  axis='z', bevel_w=0.02))
        m.add(cyl((0.24 * side, 0.38, 1.50), 0.12, 0.12, 0.22, 10, 'dark',
                  axis='z', bevel_w=0.02))
    m.group('')
    m.add(box((0, 0.38, 0.56), (0.40, 0.17, 0.12), 'armor_dark', bevel_w=0.03))
    m.add(cyl((0, 0.60, 0.50), 0.09, 0.09, 0.08, 10, 'glow_amber', axis='z',
              bevel_w=0.0))
    m.add(box((0, 0.68, -0.52), (0.38, 0.03, 0.18), 'team', bevel_w=0.015))
    m.add(cyl((0.30, 0.66, -0.42), 0.05, 0.05, 0.30, 6, 'steel', bevel_w=0.0))
    m.add(cyl((0.30, 0.96, -0.42), 0.22, 0.05, 0.08, 10, 'armor_lit', bevel_w=0.0))
    return m


EXT_BUILDERS = [build_skimmer, build_sentinel_base, build_sentinel_head]
