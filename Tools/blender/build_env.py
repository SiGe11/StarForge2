"""build_env.py -- battlefield set dressing for the Unity edition.

MapBuilder places these only on ground no unit can use (cliff faces and the
map rim), so they change how the map looks and never where anything can go.
Same authoring rules as build_models.py: game space, +Y up, origin on the
ground at the model's centre; parts may sink below the origin, where the
terrain hides them.

    ROCK_SPIRE    a weathered stack of strata, a landmark at range
    ROCK_SHELF    low slabs stepping down a slope
    WRECK         the burnt-out hull of a crashed dropship, nose in the dirt
    RUIN_PYLON    a broken monolith left by whoever held this ground before

Cover (the new combat only; placed when a match begins, GameWorld.BeginTactical): walls a man gets
behind, of rock blocks under rusted plate like the pylon's, each with its shape for the rules in
`meta['cover']` -- segments (ax, az, bx, bz, height, thickness) in the model's frame, which the game
reads instead of the mesh. Keep the two in step.

    WALL_LOW      3 m of low wall a man fires over, 1.2 m high (a crouching man, 1.15 m, is behind it)
    WALL_SHORT    2 m of the same, broken at one end
    RUIN_WALL     3.2 m of a ruined house wall, 1.7 m at one end falling to 1.15
    RUIN_CORNER   the corner of a ruin, an L of 3 m and 2.6 m, tall at the angle

`python Tools/blender/build_env.py -- <dir>` (Blender, background) exports only these and merges them
into the folder's models.json; export_fbx.py writes them with everything else.
"""

import json
import math
import os
import random
import sys

from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))   # run on its own by Blender
import build_models as B
import sf_model as S
from sf_model import Model, box, frustum, cyl, blob, inset, face_plate

MESH_ROCK_SPIRE, MESH_ROCK_SHELF, MESH_WRECK, MESH_RUIN_PYLON = 30, 31, 32, 33
MESH_WALL_LOW, MESH_WALL_SHORT, MESH_RUIN_WALL, MESH_RUIN_CORNER = 34, 35, 36, 37


def slab(center, radius, height, seed, amount=0.9, spin=0.0, tilt=0.0):
    """A blob squashed to `height`: one bed of layered rock. Few segments and a
    strong displacement, so the bed is an angular slab rather than a pebble, and
    each one is turned and tilted so a stack does not read as a cairn."""
    part = blob((0, 0, 0), radius, 7, 5, seed, amount, 'rock')
    # blob spans roughly -0.45r .. +1.4r vertically before squashing.
    S.xform(part, Matrix.Diagonal(Vector((1.0, height / (1.85 * radius), 0.82, 1.0))))
    if spin:
        S.xform(part, Matrix.Rotation(spin, 4, 'Y'))
    if tilt:
        S.xform(part, Matrix.Rotation(tilt, 4, 'Z'))
    S.xform(part, Matrix.Translation(Vector(center)))
    return part


def build_rock_spire():
    """A jagged column of weathered rock, leaning, with a smaller spur beside it
    and broken blocks at its foot. Stacked discs read as a cairn; one displaced
    mass stretched vertically and cut by a couple of ledges reads as stone."""
    m = Model(MESH_ROCK_SPIRE, 'ROCK_SPIRE')

    def column(center, radius, height, seed, lean, spin):
        part = blob((0, 0, 0), radius, 7, 5, seed, 1.15, 'rock')
        S.xform(part, Matrix.Diagonal(Vector((1.0, height / (1.85 * radius), 0.86, 1.0))))
        S.xform(part, Matrix.Rotation(spin, 4, 'Y'))
        S.xform(part, Matrix.Rotation(lean, 4, 'Z'))
        S.xform(part, Matrix.Translation(Vector(center)))
        return part

    m.add(column((0.0, 2.6, 0.0), 1.55, 6.4, 3, math.radians(5), 0.4))
    m.add(column((1.35, 1.5, -0.55), 0.95, 3.4, 11, math.radians(-13), 2.1))
    # Ledges where the strata broke away.
    m.add(slab((-0.35, 1.5, 0.25), 1.35, 0.55, 17, 0.9, spin=1.2, tilt=math.radians(-7)))
    m.add(slab((0.30, 3.8, -0.30), 1.05, 0.45, 23, 0.9, spin=2.4, tilt=math.radians(6)))
    # Fallen blocks at the foot.
    for k, (a, d, r) in enumerate(((0.4, 2.3, 0.62), (2.3, 2.1, 0.45), (4.4, 2.6, 0.38), (5.6, 1.7, 0.30))):
        m.add(blob((math.cos(a) * d, 0.08, math.sin(a) * d), r, 7, 5, 41 + k, 0.95, 'rock'))
    return m


def build_rock_shelf():
    """Broad beds stepping down one side, as if a terrace edge had slumped."""
    m = Model(MESH_ROCK_SHELF, 'ROCK_SHELF')
    for k in range(4):
        m.add(slab((k * 1.7 - 2.4, 0.9 - k * 0.28, 0.25 * math.sin(k * 1.9)),
                   2.5 - k * 0.3, 1.0 - k * 0.12, 61 + k * 7, 0.8, spin=k * 0.9,
                   tilt=math.radians(-5.0 - k * 2.0)))
    m.add(blob((2.6, 0.05, 1.4), 0.5, 7, 5, 91, 0.8, 'rock'))
    return m


def build_wreck():
    """A dropship that came down nose first: fuselage tilted into the ground and
    listing, one wing snapped up, the rear torn open to its ribs, an engine
    thrown clear. Rust and scorched dark plate; one emergency light still on."""
    m = Model(MESH_WRECK, 'WRECK')
    hull_parts = []

    fus = frustum((0, 1.25, 0), (1.30, 1.00, 3.60), top=(0.72, 0.90), shift=(0.0, -0.20),
                  mat='armor_dark', bevel_w=0.08)
    panel = face_plate(fus, 'y', 1)
    if panel:
        inset(fus, panel, 0.22, -0.06, 'rust')
    hull_parts.append(fus)
    hull_parts.append(frustum((0, 0.95, 4.05), (0.95, 0.72, 0.70), top=(0.50, 0.55),
                              shift=(0.0, -0.25), mat='armor', bevel_w=0.06))
    hull_parts.append(box((0, 1.20, 4.30), (0.55, 0.22, 0.30), 'dark', bevel_w=0.03))
    # Torn-open rear: bare ribs across the gap.
    for k in range(4):
        hull_parts.append(box((0, 2.05, -1.9 - k * 0.55), (1.05, 0.07, 0.07), 'steel', bevel_w=0.0))
    hull_parts.append(box((0, 0.55, -2.6), (1.10, 0.10, 1.00), 'dark', bevel_w=0.02))
    # Engine still on its pylon, and the emergency light.
    hull_parts.append(box((1.25, 1.10, -3.10), (0.28, 0.12, 0.50), 'steel', bevel_w=0.03))
    hull_parts.append(cyl((1.55, 1.10, -4.30), 0.52, 0.40, 1.40, 10, 'rust', axis='z', bevel_w=0.04))
    hull_parts.append(box((-0.70, 2.30, 1.20), (0.10, 0.05, 0.10), 'glow_amber', bevel_w=0.0))
    # Snapped wing, folded up against the hull.
    wing = box((2.70, 1.00, -0.40), (1.60, 0.09, 1.25), 'armor', bevel_w=0.03)
    B.rot(wing, math.radians(-28), 'Z', (1.30, 1.00, -0.40))
    hull_parts.append(wing)
    stub = box((-1.85, 0.80, -0.70), (0.60, 0.09, 1.00), 'rust', bevel_w=0.03)
    B.rot(stub, math.radians(18), 'Z', (-1.30, 0.80, -0.70))
    hull_parts.append(stub)

    for p in hull_parts:
        B.rot(p, math.radians(-11), 'X', (0, 0, 0))   # nose down into the ground
        B.rot(p, math.radians(9), 'Z', (0, 0, 0))     # listing to one side
        m.add(p)

    # The other engine, thrown clear and lying on its side.
    eng = cyl((-3.60, 0.42, -2.20), 0.46, 0.36, 1.30, 10, 'rust', axis='z', bevel_w=0.04)
    B.rot(eng, math.radians(38), 'Y', (-3.60, 0.42, -1.55))
    m.add(eng)
    for k, (x, z, r) in enumerate(((2.2, 3.4, 0.35), (-2.4, 2.2, 0.30), (0.9, -5.4, 0.40))):
        m.add(blob((x, 0.05, z), r, 6, 4, 101 + k, 0.8, 'rock'))
    return m


def build_ruin_pylon():
    """Two weathered monoliths of oxidised metal, one intact to its broken crown
    with a faint glow still in its seam, one snapped short; a fallen block."""
    m = Model(MESH_RUIN_PYLON, 'RUIN_PYLON')
    m.add(frustum((0, 0.30, 0), (1.45, 0.30, 1.30), top=(0.86, 0.86), mat='rock', bevel_w=0.06))
    pillar = frustum((0, 3.00, 0), (0.72, 2.40, 0.52), top=(0.62, 0.72), mat='rust', bevel_w=0.06)
    face = face_plate(pillar, 'z', 1)
    if face:
        inset(pillar, face, 0.14, -0.06, 'shadowed')
    m.add(pillar)
    m.add(box((0, 2.90, 0.50), (0.05, 1.70, 0.03), 'glow_cyan', bevel_w=0.0))
    crown = frustum((0.10, 5.62, 0.05), (0.50, 0.32, 0.40), top=(0.7, 0.7), mat='rust', bevel_w=0.05)
    B.rot(crown, math.radians(22), 'Z', (0.30, 5.35, 0.05))
    m.add(crown)
    stump = frustum((-1.70, 1.25, -0.70), (0.46, 1.25, 0.40), top=(0.75, 0.75), mat='rust', bevel_w=0.05)
    B.rot(stump, math.radians(8), 'X', (-1.70, 0.0, -0.70))
    m.add(stump)
    block = box((1.75, 0.38, 0.95), (0.36, 0.36, 0.95), 'rust', bevel_w=0.05)
    B.rot(block, math.radians(34), 'Y', (1.75, 0.38, 0.95))
    m.add(block)
    return m


# ------------------------------------------------------------ cover
WALL_THICK = 0.46


def wall_run(m, rng, x0, x1, z, tops, axis='x', thick=WALL_THICK):
    """A wall from x0 to x1 (along X, or along Z with axis='z') at z: courses of rock blocks, the top
    course of rusted plate. `tops` gives the top's height at each end; where it falls, the courses
    stop short in a broken line. No two faces flush: neighbours overlap and differ in depth and height."""
    length = x1 - x0
    parts = []
    # How high the wall stands at a point along it.
    def top_at(t):
        return tops[0] + (tops[1] - tops[0]) * t
    x = x0
    while x < x1 - 0.05:
        w = min(x1 - x, rng.uniform(0.62, 1.05))
        if x1 - (x + w) < 0.3:
            w = x1 - x
        mid = (x + w * 0.5 - x0) / max(1e-3, length)
        top = top_at(mid) + rng.uniform(-0.04, 0.04)
        y = 0.0
        course = 0
        while y < top - 0.12:
            h = min(top - y, rng.uniform(0.40, 0.56) if course == 0 else rng.uniform(0.34, 0.46))
            is_top = y + h >= top - 0.12
            d = thick * 0.5 + rng.uniform(-0.03, 0.02) - (0.03 if is_top else 0.0)
            cx = x + w * 0.5 + rng.uniform(-0.03, 0.03)
            hw = w * 0.5 + 0.02 + rng.uniform(0.0, 0.015)
            mat = 'rust' if is_top and course > 0 else 'rock'
            blk = box((cx, y + h * 0.5, z + rng.uniform(-0.025, 0.025)), (hw, h * 0.5 + 0.012, d), mat,
                      bevel_w=0.035 if mat == 'rock' else 0.02, bevel_seg=1)
            B.rot(blk, math.radians(rng.uniform(-3.0, 3.0)), 'Y', (cx, 0.0, z))
            parts.append(blk)
            y += h
            course += 1
        x += w
    # Rubble fallen from it.
    for k in range(max(1, int(length / 1.4))):
        t = rng.uniform(0.1, 0.9)
        side = rng.choice((-1, 1))
        parts.append(blob((x0 + length * t, 0.05, z + side * (thick * 0.5 + rng.uniform(0.15, 0.45))),
                          rng.uniform(0.12, 0.22), 6, 4, rng.randint(1, 9999), 0.7, 'rock'))
    for p in parts:
        if axis == 'z':
            B.rot(p, math.radians(-90.0), 'Y', (0.0, 0.0, 0.0))
        m.add(p)


def cover_meta(m, segments):
    m.meta['cover'] = [[round(v, 3) for v in sg] for sg in segments]


def build_wall_low():
    m = Model(MESH_WALL_LOW, 'WALL_LOW')
    wall_run(m, random.Random(341), -1.5, 1.5, 0.0, (1.2, 1.2))
    cover_meta(m, [(-1.5, 0.0, 1.5, 0.0, 1.2, WALL_THICK)])
    return m


def build_wall_short():
    m = Model(MESH_WALL_SHORT, 'WALL_SHORT')
    wall_run(m, random.Random(352), -1.0, 0.45, 0.0, (1.2, 1.16))
    wall_run(m, random.Random(353), 0.45, 1.0, 0.0, (1.06, 0.66))
    cover_meta(m, [(-1.0, 0.0, 0.45, 0.0, 1.18, WALL_THICK), (0.45, 0.0, 1.0, 0.0, 0.8, WALL_THICK)])
    return m


def build_ruin_wall():
    m = Model(MESH_RUIN_WALL, 'RUIN_WALL')
    wall_run(m, random.Random(361), -1.6, -0.4, 0.0, (1.75, 1.55))
    wall_run(m, random.Random(362), -0.4, 1.6, 0.0, (1.25, 1.1))
    cover_meta(m, [(-1.6, 0.0, -0.4, 0.0, 1.65, WALL_THICK), (-0.4, 0.0, 1.6, 0.0, 1.15, WALL_THICK)])
    return m


def build_ruin_corner():
    m = Model(MESH_RUIN_CORNER, 'RUIN_CORNER')
    t = WALL_THICK
    # The angle at (-1.5, -1.3): one arm along X, one along Z, tallest where they meet.
    wall_run(m, random.Random(371), -1.5 - t * 0.5, -0.3, -1.3, (1.95, 1.7))
    wall_run(m, random.Random(372), -0.3, 1.5, -1.3, (1.25, 1.08))
    arm = Model(0, 'tmp')
    # The Z arm, built along X and turned: x here runs from the angle outward.
    wall_run(arm, random.Random(373), 0.0 + t * 0.5, 1.4, 0.0, (1.6, 1.45))
    wall_run(arm, random.Random(374), 1.4, 2.6, 0.0, (1.22, 1.05))
    for p in arm.parts:
        B.rot(p, math.radians(-90.0), 'Y', (0.0, 0.0, 0.0))
        S.xform(p, Matrix.Translation(Vector((-1.5, 0.0, -1.3))))
        m.add(p)
    cover_meta(m, [(-1.5 - t * 0.5, -1.3, -0.3, -1.3, 1.8, t), (-0.3, -1.3, 1.5, -1.3, 1.15, t),
                   (-1.5, -1.3 + t * 0.5, -1.5, 0.1, 1.5, t), (-1.5, 0.1, -1.5, 1.3, 1.12, t)])
    return m


COVER_BUILDERS = [build_wall_low, build_wall_short, build_ruin_wall, build_ruin_corner]
ENV_BUILDERS = [build_rock_spire, build_rock_shelf, build_wreck, build_ruin_pylon] + COVER_BUILDERS


def main():
    """Export only the cover pieces and merge them into the folder's models.json (export_fbx.py would
    rewrite every model)."""
    import bpy
    import export_fbx as X
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out_dir = argv[0] if argv else os.path.join(os.getcwd(), 'Assets', 'StarForge', 'Art', 'Models')
    os.makedirs(out_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    models = [fn() for fn in COVER_BUILDERS]
    meta_path = os.path.join(out_dir, 'models.json')
    meta = json.load(open(meta_path)) if os.path.exists(meta_path) else {}
    for i, model in enumerate(models):
        slots, rad, top = X.prepare(model)
        X.bake_ao(model, rad)
        objs, tris = X.export_groups(model, slots, out_dir)
        objs[0].location.x = i * 7.0
        meta[model.name] = {'radius': round(rad, 3), 'height': round(top, 3), 'triangles': tris,
                            'materials': X.fbx_slots(slots), 'groups': {}, 'chunks': 0, 'lod1_triangles': 0}
        meta[model.name].update(model.meta)
        print('%-16s tris=%6d radius=%5.2f height=%5.2f  %s  cover %s' % (model.name, tris, rad, top, ','.join(slots), model.meta['cover']))
    with open(meta_path, 'w') as fh:
        json.dump(meta, fh, indent=2)


if __name__ == '__main__':
    main()
