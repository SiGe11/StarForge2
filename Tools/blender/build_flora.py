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

The crowns are leaf cards: quads textured with photographed leaf sprays
(Tools/blender/make_leaf_cards.py composes them from ambientCG's CC0 leaf
atlases), set at every twig tip of a grown branch skeleton and alpha-tested in
the shader, over a smaller, darker solid canopy inside that keeps the sky from
showing through. Every card's normals point out of the crown, so the crown
lights as one mass while its outline and surface are real leaves. Cards carry
their own UVs (a 'CardUV' layer the exporter keeps) and vertex alpha 1; the
solid canopy has alpha 0. Earlier versions -- jittered blobs, then grown
clusters under a generated leaf texture -- read as green rocks and green balls.

Each model also has a far copy (SF_<NAME>_LOD1.fbx) built with fewer, larger
cards (LOD_BUILDERS; decimation cannot thin out cards), drawn beyond 70 m.

    TREE_PINE    a spruce: tiers of drooping, serrated skirts round a straight trunk
    TREE_BROAD   an oak-like broadleaf: a short trunk under a wide, domed crown
    TREE_TALL    a poplar-like broadleaf: a tall bole in a narrow column of leaves
    TREE_DEAD    a bare, broken snag
    BUSH         a low, dense shrub
    TREE_BIRCH   a slender birch, often twin-stemmed, with a light, airy crown
    FERN         a rosette of arching fronds, for the shade of the groves
    REEDS        a clump of reeds with a few seed heads, for the shores
    BUSH_FLOWER  a looser shrub that the renderer tints with blossom

"""

import math
import random

import bmesh
from mathutils import Matrix, Vector, Quaternion, noise

import sf_model as S
from sf_model import Model, cyl

MESH_TREE_PINE, MESH_TREE_BROAD, MESH_TREE_TALL, MESH_TREE_DEAD, MESH_BUSH = 40, 41, 42, 43, 44
MESH_TREE_BIRCH, MESH_FERN, MESH_REEDS, MESH_BUSH_FLOWER = 45, 46, 47, 48

# Models that get a decimated far copy, and how much of each they keep.
LOD_RATIO = {'TREE_PINE': 0.35, 'TREE_BROAD': 0.3, 'TREE_TALL': 0.3, 'TREE_DEAD': 0.6, 'BUSH': 0.35,
             'TREE_BIRCH': 0.3, 'FERN': 0.4, 'REEDS': 0.4, 'BUSH_FLOWER': 0.35}


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


def leaf_cluster(center, r, seed, squash=(1.0, 0.8, 1.0), subdiv=2, mat='leaf', jag=0.13):
    """A soft cluster of leaves: an icosphere with a few gentle lobes, smooth-shaded
    (no jitter: faceted jitter is what made the old crowns read as rock), its
    underside pulled up, as leaves hang from the twig above, and its outline
    scalloped (`jag`) so a crown's edge breaks into sprays rather than a circle."""
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
        sc += noise.noise(d * 3.6 + offset * 1.3) * jag
        if d.y < -0.1:
            sc *= 1.0 + (d.y + 0.1) * 0.35
        v.co = Vector((d.x * r * sc * squash[0], d.y * r * sc * squash[1], d.z * r * sc * squash[2]))
    bm.normal_update()
    S.set_normal_hint(bm, (0.0, -0.25 * r, 0.0))
    S.xform(bm, Matrix.Translation(Vector(center)))
    S.set_mat(bm, bm.faces[:], mat)
    return bm


def card(base, up, normal, width, height, cell, crown, mat='leaf', flip=False):
    """A leaf card: a quad standing on `base` and reaching `height` along `up`,
    `width` across, facing `normal`, mapped to one cell of the 2x2 leaf-spray
    atlas (the twig in each cell runs up from its bottom centre, so it meets
    `base`). Its normals point out of the crown from `crown` (the exporter's
    normal hint), so a crown of cards lights as one mass."""
    up = Vector(up).normalized()
    right = up.cross(Vector(normal))
    if right.length < 1e-4:
        right = up.orthogonal()
    right.normalize()
    b = Vector(base)
    bm = S.new_bm()
    corners = [b - right * width * 0.5, b + right * width * 0.5,
               b + right * width * 0.5 + up * height, b - right * width * 0.5 + up * height]
    vs = [bm.verts.new(q) for q in corners]
    f = bm.faces.new(vs)
    uv = bm.loops.layers.uv.new('CardUV')
    cx, cy = (cell % 2) * 0.5, (cell // 2) * 0.5
    coords = ((1, 0), (0, 0), (0, 1), (1, 1)) if flip else ((0, 0), (1, 0), (1, 1), (0, 1))
    for loop in f.loops:
        u, v = coords[vs.index(loop.vert)]
        loop[uv].uv = (cx + u * 0.5, cy + v * 0.5)
    bm.normal_update()
    S.set_normal_hint(bm, crown)
    S.set_mat(bm, bm.faces[:], mat)
    S.set_vdata(bm, a=1.0)
    return bm


def cards_at(m, rng, tip, direction, crown, count, size, mat='leaf', lift=0.35):
    """`count` cards round a twig tip, each along the twig (tilted up a little
    and fanned round it), facing a random way about the twig."""
    d = Vector(direction).normalized()
    for k in range(count):
        up = _turn(d, 0.7, rng, lift)
        # Facing: round the twig axis, biased to face out of the crown.
        out = (Vector(tip) - Vector(crown))
        side = up.orthogonal().normalized()
        side.rotate(Quaternion(up, rng.uniform(0, 2 * math.pi)))
        n = (side + out.normalized() * 0.6).normalized() if out.length > 1e-3 else side
        sz = size * rng.uniform(0.8, 1.2)
        base = Vector(tip) - up * sz * 0.25
        m.add(card(base, up, n, sz, sz, rng.randrange(4), crown, mat, rng.random() < 0.5))


def shell(m, rng, centre, radii, count, size, mat='leaf'):
    """Cards spread over an ellipsoid round `centre`, each rooted just inside the
    surface and reaching out of it, up and outward: the crown's outline, dense
    enough that the canopy inside only shows in the gaps."""
    c = Vector(centre)
    golden = math.pi * (3.0 - math.sqrt(5.0))
    for k in range(count):
        # A Fibonacci sphere, the lowest fifth left out (the underside is in shade).
        y = 1.0 - (k + 0.5) / count * 1.8
        r = math.sqrt(max(0.0, 1.0 - y * y))
        a = k * golden + rng.uniform(-0.3, 0.3)
        d = Vector((math.cos(a) * r, y, math.sin(a) * r))
        at = c + Vector((d.x * radii[0], d.y * radii[1], d.z * radii[2])) * rng.uniform(0.72, 0.92)
        up = (d + Vector((0.0, 0.6, 0.0))).normalized()
        up = _turn(up, 0.35, rng, 0.1)
        side = up.orthogonal().normalized()
        side.rotate(Quaternion(up, rng.uniform(0, 2 * math.pi)))
        n = (side + d * 0.8).normalized()
        sz = size * rng.uniform(0.8, 1.2)
        m.add(card(at - up * sz * 0.3, up, n, sz, sz, rng.randrange(4), c, mat, rng.random() < 0.5))


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


def skirt(y, radius, droop, teeth, seed, mat='needle'):
    """One tier of a spruce: a drooping skirt of needles round the trunk, its rim
    cut into teeth (the tips of the boughs) that reach out and hang lower than the
    notches between them. Closed underneath, so it is a solid, opaque volume."""
    rng = random.Random(seed)
    bm = S.new_bm()
    top = bm.verts.new((0.0, y + 0.22, 0.0))
    under = bm.verts.new((0.0, y - droop * 0.5, 0.0))
    inner, rim = [], []
    n = teeth * 2
    spin = rng.uniform(0, 2 * math.pi)
    for k in range(n):
        a = spin + k * math.pi / teeth + rng.uniform(-0.1, 0.1)
        tooth = k % 2 == 0
        r = radius * (rng.uniform(0.94, 1.14) if tooth else rng.uniform(0.6, 0.74))
        dr = droop * (rng.uniform(0.9, 1.15) if tooth else 0.62)
        rim.append(bm.verts.new((math.cos(a) * r, y - dr, math.sin(a) * r)))
        mr = radius * rng.uniform(0.46, 0.54)
        inner.append(bm.verts.new((math.cos(a) * mr, y - droop * 0.22 + 0.06, math.sin(a) * mr)))
    for k in range(n):
        j = (k + 1) % n
        bm.faces.new((top, inner[j], inner[k]))
        bm.faces.new((inner[k], inner[j], rim[j], rim[k]))
        bm.faces.new((under, rim[k], rim[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.normal_update()
    S.set_normal_hint(bm, (0.0, y - droop * 0.9, 0.0))
    S.set_mat(bm, bm.faces[:], mat)
    return bm


def blade(base, direction, length, width, bend, seed, mat='leaf', rings=4):
    """A reed or grass blade: a thin, closed, three-sided strip tapering to a
    point, bowing over toward its tip (solid, so it shows from every side without
    two-sided rendering)."""
    rng = random.Random(seed)
    bm = S.new_bm()
    fwd = Vector(direction).normalized()
    side = fwd.cross(Vector((0.0, 0.0, 1.0)))
    if side.length < 1e-4:
        side = Vector((1.0, 0.0, 0.0))
    side.normalize()
    out = Vector((fwd.x, 0.0, fwd.z))
    out = out.normalized() if out.length > 1e-4 else side.cross(fwd).normalized()
    thick = side.cross(fwd).normalized()
    rings_v = []
    for k in range(rings + 1):
        t = k / rings
        w = width * (1.0 - t * 0.85)
        c = Vector(base) + fwd * (length * t) + out * (bend * length * t * t)
        c.y -= bend * length * 0.35 * t ** 3
        rings_v.append([bm.verts.new(c + side * w * 0.5), bm.verts.new(c + thick * w * 0.35), bm.verts.new(c - side * w * 0.5)])
    tip = bm.verts.new(Vector(base) + fwd * length * 1.04 + out * bend * length * 1.08 - Vector((0, bend * length * 0.4, 0)))
    for k in range(rings):
        a, b = rings_v[k], rings_v[k + 1]
        for j in range(3):
            bm.faces.new((a[j], b[j], b[(j + 1) % 3], a[(j + 1) % 3]))
    for j in range(3):
        bm.faces.new((rings_v[-1][j], tip, rings_v[-1][(j + 1) % 3]))
    bm.faces.new(tuple(reversed(rings_v[0])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.normal_update()
    S.set_mat(bm, bm.faces[:], mat)
    return bm


def build_tree_broad(lod=False):
    """An oak about 7.5 m tall: a stout trunk with a root flare, forking into four
    spreading limbs that divide twice; oak-leaf cards at every twig tip over a
    darker canopy inside."""
    m = Model(MESH_TREE_BROAD, 'TREE_BROAD')
    rng = random.Random(4101)
    top = Vector((0.1, 2.6, 0.05))
    m.add(limb((0, -0.5, 0), top, 0.42, 0.3, 7 if lod else 9, seed=3, bend=0.03))
    if not lod:
        root_flare(m, rng, 0.42)
    tips = []
    for k in range(4):
        a = k * math.pi / 2 + rng.uniform(-0.35, 0.35)
        d = Vector((math.cos(a) * 0.8, 0.72, math.sin(a) * 0.8)).normalized()
        grow(m, rng, top - Vector((0, rng.uniform(0, 0.4), 0)), d, rng.uniform(1.8, 2.2), 0.2, 0, 2 if lod else 3, tips,
             children=(2, 2), spread=0.6, rise=0.25)
    crown = Vector((0.0, 4.4, 0.0))
    for i in range(4):
        a = i * 2 * math.pi / 4 + rng.uniform(-0.3, 0.3)
        p = crown + Vector((math.cos(a) * 0.9, rng.uniform(-0.2, 0.5), math.sin(a) * 0.9))
        m.add(leaf_cluster(p, rng.uniform(1.0, 1.2), 700 + i, squash=(1.1, 0.75, 1.1), subdiv=1 if lod else 2))
    for p, d in tips:
        cards_at(m, rng, p, d, crown, 3 if lod else 5, 2.6 if lod else 2.0)
    shell(m, rng, crown, (3.0, 1.7, 3.0), 18 if lod else 34, 2.6 if lod else 2.0)
    return finish(m, 7.5, 3.4)


def build_tree_tall(lod=False):
    """A poplar about 10 m tall: a straight, slender bole with short branches
    rising steeply all the way up, beech-leaf cards packed into a narrow,
    flame-shaped column round a solid core."""
    m = Model(MESH_TREE_TALL, 'TREE_TALL')
    rng = random.Random(4202)
    m.add(limb((0, -0.5, 0), (-0.05, 8.6, 0.04), 0.3, 0.06, 6 if lod else 8, seed=5, bend=0.015))
    if not lod:
        root_flare(m, rng, 0.3, 4)
    tips = []
    y = 2.2
    k = 0
    while y < 8.6:
        a = k * 2.4 + rng.uniform(-0.3, 0.3)
        t = (y - 2.2) / 6.4
        d = Vector((math.cos(a) * 0.5, 0.87, math.sin(a) * 0.5)).normalized()
        grow(m, rng, (0.0, y, 0.0), d, (1.3 - t * 0.7) * rng.uniform(0.85, 1.15), 0.08 * (1 - t * 0.5), 0, 1, tips,
             children=(1, 2), spread=0.35, rise=0.45)
        y += rng.uniform(0.38, 0.52) * (1.6 if lod else 1.0)
        k += 1
    for i in range(7):
        yy = 3.0 + i * 0.85
        t = (yy - 2.2) / 7.0
        w = 1.0 * math.sin(min(1.0, t * 1.5 + 0.1) * math.pi * 0.5) * (1.0 - t * 0.6)
        m.add(leaf_cluster((0.0, yy, 0.0), 0.35 + w * 0.3, 880 + i, squash=(1.0, 1.3, 1.0), subdiv=1))
    for p, d in tips:
        centre = Vector((0.0, p.y, 0.0))
        cards_at(m, rng, p, d, centre, 2 if lod else 4, 1.7 if lod else 1.3, lift=0.6)
    shell(m, rng, Vector((0.0, 5.6, 0.0)), (1.3, 3.2, 1.3), 14 if lod else 26, 1.7 if lod else 1.35)
    return finish(m, 10.0, 1.7)


def build_tree_pine(lod=False):
    """A spruce about 9.5 m tall: a straight trunk; tiers of drooping boughs of
    needle-spray cards, widest near the foot and narrowing to a spire, over a
    dark, serrated core that keeps it dense."""
    m = Model(MESH_TREE_PINE, 'TREE_PINE')
    rng = random.Random(4303)
    H = 9.5
    m.add(limb((0, -0.5, 0), (0.04, H, 0.03), 0.32, 0.03, 6 if lod else 8, seed=7))
    if not lod:
        root_flare(m, rng, 0.32, 4)
    tiers = 8 if lod else 11
    for t in range(tiers):
        f = t / (tiers - 1)
        y = 1.5 + f * (H - 2.6)
        radius = 2.7 * (1.0 - f) ** 0.9 + 0.45
        m.add(skirt(y, radius * 0.72, 0.4 + radius * 0.3, 6 if f < 0.6 else 5, 900 + t))
        count = max(4, int(round((9 - f * 4) * (0.6 if lod else 1.0))))
        spin = rng.uniform(0, 2 * math.pi)
        for b in range(count):
            a = spin + b * 2 * math.pi / count + rng.uniform(-0.2, 0.2)
            out = Vector((math.cos(a), 0.0, math.sin(a)))
            length = radius * rng.uniform(1.0, 1.2)
            droop = rng.uniform(0.25, 0.45)
            up = (out - Vector((0, droop, 0))).normalized()
            side = out.cross(Vector((0, 1, 0))).normalized()
            n = (up.cross(side)).normalized()
            if n.y < 0:
                n = -n
            n = (n + out * 0.25).normalized()
            base = Vector((0.0, y + 0.15, 0.0)) + out * 0.15
            w = (0.5 + radius * 0.35) * (1.3 if lod else 1.0)
            m.add(card(base, up, n, w, length, rng.randrange(4), (0.0, y - 1.0, 0.0), 'needle', rng.random() < 0.5))
    m.add(leaf_cluster((0.0, H - 0.55, 0.0), 0.42, 990, squash=(0.6, 1.5, 0.6), subdiv=1, mat='needle', jag=0.05))
    return finish(m, H, 3.0)


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


def build_bush(lod=False):
    """A shrub about 1.3 m tall and 2.4 m across: beech-leaf cards all round a
    low, dark mound."""
    m = Model(MESH_BUSH, 'BUSH')
    rng = random.Random(4505)
    centre = Vector((0.0, 0.35, 0.0))
    m.add(leaf_cluster((0.0, 0.5, 0.0), 0.62, 1000, squash=(1.2, 0.75, 1.2), subdiv=1))
    shell(m, rng, Vector((0.0, 0.55, 0.0)), (1.0, 0.55, 1.0), 12 if lod else 22, 1.3 if lod else 1.0)
    return finish(m, 1.35, 1.2)


def build_tree_birch(lod=False):
    """A birch about 8.5 m tall: two slender, pale stems leaning apart from one
    foot, with thin branches drooping at the tips and small birch-leaf cards, so
    light gets through the crown."""
    m = Model(MESH_TREE_BIRCH, 'TREE_BIRCH')
    rng = random.Random(4606)
    tips = []
    for stem, (lean, h) in enumerate((((0.55, 0.12), 8.4), ((-0.45, -0.25), 7.0))):
        top = Vector((lean[0], h, lean[1]))
        m.add(limb((0, -0.5, 0), top, 0.2 if stem == 0 else 0.16, 0.04, 6 if lod else 7, seed=31 + stem, bend=0.02))
        y = 3.0
        k = 0
        while y < h - 0.6:
            a = k * 2.3 + stem + rng.uniform(-0.3, 0.3)
            t = y / h
            at = Vector((0, 0, 0)).lerp(top, t)
            d = Vector((math.cos(a) * 0.75, 0.55, math.sin(a) * 0.75)).normalized()
            grow(m, rng, at, d, (1.7 - t * 0.9) * rng.uniform(0.85, 1.15), 0.05, 0, 1, tips,
                 children=(2, 3), spread=0.55, rise=-0.25)
            y += rng.uniform(0.55, 0.8) * (1.5 if lod else 1.0)
            k += 1
        tips.append((top, Vector((0, 1, 0))))
    if not lod:
        root_flare(m, rng, 0.2, 3)
    crown = Vector((0.0, 5.8, 0.0))
    m.add(leaf_cluster((0.2, 5.6, 0.0), 1.1, 1190, squash=(1.1, 1.5, 1.1), subdiv=1))
    for p, d in tips:
        cards_at(m, rng, p, d, crown, 2 if lod else 4, 1.6 if lod else 1.25, lift=-0.1)
    shell(m, rng, crown, (1.9, 2.2, 1.9), 12 if lod else 24, 1.6 if lod else 1.3)
    return finish(m, 8.5, 2.4)


def build_fern(lod=False):
    """A fern about 0.8 m tall: a rosette of arching fronds (needle-spray cards
    bent once) from the ground."""
    m = Model(MESH_FERN, 'FERN')
    rng = random.Random(4707)
    count = 6 if lod else 9
    centre = Vector((0.0, -0.3, 0.0))
    for k in range(count):
        a = k * 2 * math.pi / count + rng.uniform(-0.25, 0.25)
        out = Vector((math.cos(a), 0.0, math.sin(a)))
        side = out.cross(Vector((0, 1, 0)))
        cell = rng.randrange(4)
        # Two segments: rising, then arching over.
        up1 = (out * 0.55 + Vector((0, 1.0, 0))).normalized()
        up2 = (out * 1.0 + Vector((0, -0.15, 0))).normalized()
        n1 = up1.cross(side).normalized()
        n2 = up2.cross(side).normalized()
        m.add(card((0, 0.02, 0), up1, n1 if n1.y > 0 else -n1, 0.45, 0.5, cell, centre, 'leaf'))
        m.add(card(Vector((0, 0.02, 0)) + up1 * 0.5, up2, n2 if n2.y > 0 else -n2, 0.4, 0.5, cell, centre, 'leaf'))
    return finish(m, 0.85, 1.0)


def build_reeds():
    """A clump of reeds about 1.6 m tall: blades rising from one root and bowing
    outward, and a few brown seed heads on stiff stems standing above them."""
    m = Model(MESH_REEDS, 'REEDS')
    rng = random.Random(4808)
    for k in range(16):
        a = rng.uniform(0, 2 * math.pi)
        lean = rng.uniform(0.08, 0.35)
        d = Vector((math.cos(a) * lean, 1.0, math.sin(a) * lean))
        foot = Vector((math.cos(a) * rng.uniform(0.0, 0.25), -0.05, math.sin(a) * rng.uniform(0.0, 0.25)))
        m.add(blade(foot, d, rng.uniform(1.0, 1.55), rng.uniform(0.07, 0.1), rng.uniform(0.1, 0.3), 1300 + k))
    for k in range(4):
        a = rng.uniform(0, 2 * math.pi)
        d = rng.uniform(0.05, 0.3)
        base = Vector((math.cos(a) * d, 0.0, math.sin(a) * d))
        top = base + Vector((math.cos(a) * 0.1, rng.uniform(1.5, 1.8), math.sin(a) * 0.1))
        m.add(limb(base, top - Vector((0, 0.3, 0)), 0.018, 0.014, 4, seed=1340 + k))
        m.add(limb(top - Vector((0, 0.32, 0)), top, 0.05, 0.04, 6, seed=1350 + k))
    return finish(m, 1.7, 0.7)


def build_bush_flower(lod=False):
    """A shrub about 1.5 m tall, looser than the plain bush: cards on a few stems
    leaning out from a low mound. The renderer tints part of its cards with
    blossom."""
    m = Model(MESH_BUSH_FLOWER, 'BUSH_FLOWER')
    rng = random.Random(4909)
    centre = Vector((0.0, 0.4, 0.0))
    m.add(leaf_cluster((0.0, 0.5, 0.0), 0.55, 1400, squash=(1.2, 0.8, 1.2), subdiv=1))
    shell(m, rng, Vector((0.0, 0.6, 0.0)), (1.1, 0.65, 1.1), 12 if lod else 22, 1.3 if lod else 1.0)
    return finish(m, 1.5, 1.3)


FLORA_BUILDERS = [build_tree_pine, build_tree_broad, build_tree_tall, build_tree_dead, build_bush,
                  build_tree_birch, build_fern, build_reeds, build_bush_flower]

# Far copies with fewer, larger cards (card models cannot be decimated).
LOD_BUILDERS = {
    'TREE_PINE': lambda: build_tree_pine(True),
    'TREE_BROAD': lambda: build_tree_broad(True),
    'TREE_TALL': lambda: build_tree_tall(True),
    'TREE_BIRCH': lambda: build_tree_birch(True),
    'BUSH': lambda: build_bush(True),
    'BUSH_FLOWER': lambda: build_bush_flower(True),
    'FERN': lambda: build_fern(True),
}
