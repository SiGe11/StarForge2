"""build_flora.py -- trees and bushes for the Unity edition.

Same authoring rules as build_models.py: game space, +Y up, origin on the
ground at the trunk. Unity does not place these as objects: the map generator
writes their positions into the Vegetation component, which draws each kind
GPU-instanced with StarForge/Tree and knocks them down, sets them alight and
burns them out during a match. So the models carry what that shader needs:

  * two material slots at most, bark first, then foliage ('leaf' or 'needle');
  * vertex colour G, a random value per leaf cluster (the shader varies the green);
  * vertex colour B, how freely the vertex sways: zero at the foot of the trunk,
    one at the outer tips of the crown;
  * models.json 'foliage': the crown's centre and radii (measured from the
    foliage itself), which the shader bends the foliage normals toward and the
    game places flames in.

The first version hung a handful of big jittered blobs on three sticks, and at
RTS range they read as green rocks. These are grown instead: a trunk with a root
flare splits into limbs and twigs by a seeded recursion, and every twig carries
a cluster of leaves, so the crown is dozens of soft, smooth-shaded clusters with
light getting between them and branches showing through the gaps. The conifer
is built from whorls of drooping boughs, each bough a tapering, ridged frond,
rather than from stacked cones. No leaf cards: no alpha test (CLAUDE.md).

The exporter also writes a decimated copy of each (SF_<NAME>_LOD1.fbx) that the
renderer uses for trees far from the camera.

    TREE_PINE    a spruce: whorls of drooping boughs round a straight trunk
    TREE_BROAD   an oak-like broadleaf: a short trunk under a wide, domed crown
    TREE_TALL    a poplar-like broadleaf: a tall bole in a narrow column of leaves
    TREE_DEAD    a bare, broken snag
    BUSH         a low, dense shrub
"""

import math
import random

import bmesh
from mathutils import Matrix, Vector, Quaternion, noise

import sf_model as S
from sf_model import Model, cyl

MESH_TREE_PINE, MESH_TREE_BROAD, MESH_TREE_TALL, MESH_TREE_DEAD, MESH_BUSH = 40, 41, 42, 43, 44

# Models that get a decimated far copy, and how much of each they keep.
LOD_RATIO = {'TREE_PINE': 0.35, 'TREE_BROAD': 0.3, 'TREE_TALL': 0.3, 'TREE_DEAD': 0.6, 'BUSH': 0.35}


def _clamp01(x):
    return max(0.0, min(1.0, x))


def limb(p0, p1, r0, r1, seg, seed=0, bend=0.0):
    """A tapered bark cylinder from p0 to p1 (open ends: the foot is buried and
    the tip is hidden in leaves or tapers to nothing). `bend` bows the middle
    sideways, so no branch is a machined rod."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    length = max(d.length, 1e-3)
    part = cyl((0, 0, 0), r0, r1, length, seg, 'bark', axis='y', caps=False, bevel_w=0.0)
    if bend:
        rng = random.Random(seed)
        a = rng.uniform(0, 2 * math.pi)
        side = Vector((math.cos(a), 0.0, math.sin(a)))
        for v in part.verts:
            t = v.co.y / length
            v.co += side * (math.sin(t * math.pi) * bend * length)
    S.xform(part, Vector((0, 1, 0)).rotation_difference(d.normalized()).to_matrix().to_4x4())
    S.xform(part, Matrix.Translation(p0))
    return part


def leaf_cluster(center, r, seed, squash=(1.0, 0.8, 1.0), subdiv=2, mat='leaf'):
    """A soft cluster of leaves: an icosphere with a few gentle lobes, smooth-shaded
    (no jitter: faceted jitter is what made the old crowns read as rock), its
    underside pulled up, as leaves hang from the twig above."""
    rng = random.Random(seed)
    bm = S.new_bm()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=r)
    lobes = [(Vector((rng.uniform(-1, 1), rng.uniform(-0.3, 1), rng.uniform(-1, 1))).normalized(),
              rng.uniform(0.08, 0.2)) for _ in range(4)]
    offset = Vector((rng.uniform(-50, 50), rng.uniform(-50, 50), rng.uniform(-50, 50)))
    for v in bm.verts:
        d = v.co.normalized()
        sc = 1.0
        for axis, amp in lobes:
            sc += amp * max(0.0, d.dot(axis)) ** 2
        # Smooth mid-frequency bumps: sprays of leaves, not a ball.
        sc += noise.noise(d * 2.2 + offset) * 0.22
        if d.y < -0.1:
            sc *= 1.0 + (d.y + 0.1) * 0.35
        v.co = Vector((d.x * r * sc * squash[0], d.y * r * sc * squash[1], d.z * r * sc * squash[2]))
    bm.normal_update()
    S.set_normal_hint(bm, (0.0, -0.25 * r, 0.0))
    S.xform(bm, Matrix.Translation(Vector(center)))
    S.set_mat(bm, bm.faces[:], mat)
    return bm


def bough(base, direction, length, width, droop, seed, mat='needle', rings=5):
    """A conifer bough: a frond tapering from the trunk to a point, widest a third
    of the way out, ridged along its spine and drooping toward the tip. Lofted
    from diamond cross-sections: a spine on top, the edges out to the sides and a
    shallow keel underneath."""
    rng = random.Random(seed)
    bm = S.new_bm()
    fwd = Vector(direction).normalized()
    side = fwd.cross(Vector((0, 1, 0)))
    if side.length < 1e-4:
        side = Vector((1, 0, 0))
    side.normalize()
    up = side.cross(fwd).normalized()
    sections = []
    for k in range(rings):
        t = (k + 1) / (rings + 1)
        w = width * math.sin(math.pi * min(1.0, t * 1.25)) ** 0.8 * rng.uniform(0.85, 1.1)
        c = Vector(base) + fwd * (length * t) - Vector((0, 1, 0)) * (droop * t * t)
        h = w * 0.32
        sections.append([bm.verts.new(c + up * h),
                         bm.verts.new(c + side * w - up * h * 0.25),
                         bm.verts.new(c - up * h * 0.55),
                         bm.verts.new(c - side * w - up * h * 0.25)])
    root = bm.verts.new(Vector(base))
    tip = bm.verts.new(Vector(base) + fwd * length - Vector((0, 1, 0)) * droop)
    for j in range(4):
        bm.faces.new((root, sections[0][j], sections[0][(j + 1) % 4]))
        bm.faces.new((tip, sections[-1][(j + 1) % 4], sections[-1][j]))
    for k in range(rings - 1):
        a, b = sections[k], sections[k + 1]
        for j in range(4):
            bm.faces.new((a[j], b[j], b[(j + 1) % 4], a[(j + 1) % 4]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.normal_update()
    S.set_mat(bm, bm.faces[:], mat)
    return bm


def _turn(direction, max_angle, rng, rise=0.0):
    """A direction turned by up to `max_angle` about a random axis, nudged upward."""
    d = Vector(direction).normalized()
    axis = d.orthogonal().normalized()
    axis.rotate(Quaternion(d, rng.uniform(0, 2 * math.pi)))
    out = Quaternion(axis, rng.uniform(max_angle * 0.5, max_angle)) @ d
    out.y += rise
    return out.normalized()


def grow(m, rng, base, direction, length, radius, depth, max_depth, tips, children=(2, 3), spread=0.6, rise=0.25):
    """A branch and, recursively, the branches off it. The tips of the last
    generation go into `tips` as (position, direction)."""
    base = Vector(base)
    end = base + Vector(direction) * length
    seg = 7 if depth == 0 else 5
    m.add(limb(base, end, radius, radius * 0.62, seg, seed=rng.randrange(1 << 30), bend=0.05))
    if depth >= max_depth:
        tips.append((end, Vector(direction)))
        return
    for _ in range(rng.randint(*children)):
        at = base.lerp(end, rng.uniform(0.55, 1.0))
        d = _turn(direction, spread, rng, rise)
        grow(m, rng, at, d, length * rng.uniform(0.55, 0.75), radius * 0.6, depth + 1, max_depth, tips,
             children, spread, rise)


def finish(m, height, radius):
    """Sway weights on every part, and the crown's shape (from the foliage's own
    extent) for models.json."""
    rng = random.Random(len(m.parts) * 31 + int(height * 100))
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for part in m.parts:
        is_bark = all(S.MAT_NAMES[f[S._layer(part)]] == 'bark' for f in part.faces)
        if not is_bark:
            for v in part.verts:
                lo = Vector((min(lo.x, v.co.x), min(lo.y, v.co.y), min(lo.z, v.co.z)))
                hi = Vector((max(hi.x, v.co.x), max(hi.y, v.co.y), max(hi.z, v.co.z)))
        S.set_vdata(part, g=rng.random(), b=lambda co, bark=is_bark: _clamp01(
            (_clamp01(co.y / height) ** 1.6) * (0.55 + 0.45 * _clamp01(math.hypot(co.x, co.z) / radius))
            * (0.45 if bark else 1.0)))
    if hi.x > lo.x:
        c = (lo + hi) * 0.5
        r = (hi - lo) * 0.5
        m.meta['foliage'] = {'center': [round(0.0, 3), round(c.y, 3), round(0.0, 3)],
                             'radii': [round(max(r.x, r.z), 3), round(r.y, 3), round(max(r.x, r.z), 3)]}
    return m


def root_flare(m, rng, r, n=5):
    """Roots spreading into the ground at the foot of the trunk."""
    for k in range(n):
        a = k * 2 * math.pi / n + rng.uniform(-0.3, 0.3)
        out = Vector((math.cos(a), 0.0, math.sin(a)))
        m.add(limb(Vector((0, 0.45, 0)) + out * r * 0.3, Vector((0, -0.25, 0)) + out * r * 2.3,
                   r * 0.45, r * 0.12, 5, seed=rng.randrange(1 << 30), bend=0.1))


def build_tree_broad():
    """An oak-like tree about 7.5 m tall: a stout trunk with a root flare, forking
    into four spreading limbs that divide twice, each twig carrying a leaf
    cluster, over a few large clusters that fill the dome from inside."""
    m = Model(MESH_TREE_BROAD, 'TREE_BROAD')
    rng = random.Random(4101)
    top = Vector((0.1, 2.6, 0.05))
    m.add(limb((0, -0.5, 0), top, 0.42, 0.3, 9, seed=3, bend=0.03))
    root_flare(m, rng, 0.42)
    tips = []
    for k in range(4):
        a = k * math.pi / 2 + rng.uniform(-0.35, 0.35)
        d = Vector((math.cos(a) * 0.8, 0.72, math.sin(a) * 0.8)).normalized()
        grow(m, rng, top - Vector((0, rng.uniform(0, 0.4), 0)), d, rng.uniform(1.8, 2.2), 0.2, 0, 3, tips,
             children=(2, 2), spread=0.6, rise=0.25)
    for i, (p, d) in enumerate(tips):
        m.add(leaf_cluster(p + d * 0.15 + Vector((0, 0.1, 0)), rng.uniform(0.62, 0.86), 600 + i, squash=(1.0, 0.75, 1.0)))
    centre = Vector((0.0, 4.6, 0.0))
    for i in range(4):
        a = i * 2 * math.pi / 4 + rng.uniform(-0.3, 0.3)
        p = centre + Vector((math.cos(a) * 1.0, rng.uniform(-0.3, 0.5), math.sin(a) * 1.0))
        m.add(leaf_cluster(p, rng.uniform(0.95, 1.15), 700 + i))
    return finish(m, 7.5, 3.2)


def build_tree_tall():
    """A poplar-like tree about 9.5 m tall: a straight, slender bole with short
    branches rising steeply all the way up, clustered into a narrow column."""
    m = Model(MESH_TREE_TALL, 'TREE_TALL')
    rng = random.Random(4202)
    m.add(limb((0, -0.5, 0), (-0.05, 8.2, 0.04), 0.3, 0.06, 8, seed=5, bend=0.015))
    root_flare(m, rng, 0.3, 4)
    tips = []
    y = 2.3
    k = 0
    while y < 7.8:
        a = k * 2.4 + rng.uniform(-0.3, 0.3)
        t = (y - 2.3) / 5.5
        d = Vector((math.cos(a) * 0.55, 0.85, math.sin(a) * 0.55)).normalized()
        grow(m, rng, (0.0, y, 0.0), d, (1.5 - t * 0.7) * rng.uniform(0.85, 1.15), 0.09 * (1 - t * 0.5), 0, 1, tips,
             children=(1, 2), spread=0.35, rise=0.4)
        y += rng.uniform(0.45, 0.65)
        k += 1
    for i, (p, d) in enumerate(tips):
        m.add(leaf_cluster(p + d * 0.15, rng.uniform(0.45, 0.62), 800 + i, squash=(0.9, 1.2, 0.9)))
    for i, yy in enumerate((3.2, 4.3, 5.4, 6.5, 7.6, 8.6)):
        m.add(leaf_cluster((rng.uniform(-0.2, 0.2), yy, rng.uniform(-0.2, 0.2)), 0.75 - i * 0.05, 880 + i,
                           squash=(1.0, 1.25, 1.0)))
    return finish(m, 9.5, 1.8)


def build_tree_pine():
    """A spruce about 9 m tall: a straight trunk carrying whorls of drooping boughs
    that shorten toward a spire, each whorl turned from the one below."""
    m = Model(MESH_TREE_PINE, 'TREE_PINE')
    rng = random.Random(4303)
    H = 9.0
    m.add(limb((0, -0.5, 0), (0.04, H, 0.03), 0.32, 0.03, 8, seed=7))
    root_flare(m, rng, 0.32, 4)
    whorls = 12
    for w in range(whorls):
        t = w / (whorls - 1)
        y = 1.2 + t * (H - 1.9)
        length = 2.5 * (1.0 - t) ** 0.95 + 0.4
        count = max(5, int(round(8 - t * 3)))
        spin = rng.uniform(0, 2 * math.pi)
        for b in range(count):
            a = spin + b * 2 * math.pi / count + rng.uniform(-0.2, 0.2)
            d = Vector((math.cos(a), rng.uniform(-0.05, 0.12), math.sin(a)))
            m.add(bough((0.0, y, 0.0), d, length * rng.uniform(0.85, 1.1), 0.3 + length * 0.26,
                        length * rng.uniform(0.3, 0.42), 900 + w * 13 + b, rings=4))
    for b in range(3):
        a = b * 2 * math.pi / 3
        m.add(bough((0.0, H - 0.9, 0.0), (math.cos(a) * 0.25, 1.0, math.sin(a) * 0.25), 1.2, 0.2, 0.0, 990 + b, rings=3))
    return finish(m, H, 2.9)


def build_tree_dead():
    """A dead snag about 6 m tall, its top snapped off, with bare, crooked branches
    that fork into twigs."""
    m = Model(MESH_TREE_DEAD, 'TREE_DEAD')
    rng = random.Random(4404)
    m.add(limb((0, -0.5, 0), (0.25, 5.6, -0.12), 0.36, 0.1, 8, seed=21, bend=0.03))
    root_flare(m, rng, 0.36, 4)
    tips = []
    for k, y in enumerate((2.0, 2.9, 3.7, 4.5, 5.2)):
        a = k * 2.2 + rng.uniform(-0.3, 0.3)
        d = Vector((math.cos(a) * 0.8, 0.6, math.sin(a) * 0.8)).normalized()
        grow(m, rng, (0.05 * y / 5.6 * 5, y, -0.02 * y), d, rng.uniform(1.2, 1.7), 0.1, 0, 1, tips,
             children=(1, 2), spread=0.6, rise=0.2)
    return finish(m, 6.0, 1.8)


def build_bush():
    """A shrub about 1.3 m tall and 2.4 m across: a dense mound of small clusters."""
    m = Model(MESH_BUSH, 'BUSH')
    rng = random.Random(4505)
    m.add(leaf_cluster((0.0, 0.62, 0.0), 0.62, 1000, squash=(1.1, 0.85, 1.1)))
    for i in range(8):
        a = i * 2 * math.pi / 8 + rng.uniform(-0.25, 0.25)
        d = rng.uniform(0.55, 0.85)
        r = rng.uniform(0.34, 0.5)
        m.add(leaf_cluster((math.cos(a) * d, 0.28 + r * 0.55, math.sin(a) * d), r, 1001 + i,
                           squash=(1.0, 0.85, 1.0), subdiv=2 if r > 0.42 else 1))
    for i in range(3):
        a = rng.uniform(0, 2 * math.pi)
        m.add(leaf_cluster((math.cos(a) * 0.3, 0.95, math.sin(a) * 0.3), rng.uniform(0.3, 0.4), 1020 + i, subdiv=1))
    return finish(m, 1.35, 1.2)


FLORA_BUILDERS = [build_tree_pine, build_tree_broad, build_tree_tall, build_tree_dead, build_bush]
