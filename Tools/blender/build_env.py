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
"""

import math

from mathutils import Matrix, Vector

import build_models as B
import sf_model as S
from sf_model import Model, box, frustum, cyl, blob, inset, face_plate

MESH_ROCK_SPIRE, MESH_ROCK_SHELF, MESH_WRECK, MESH_RUIN_PYLON = 30, 31, 32, 33


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


ENV_BUILDERS = [build_rock_spire, build_rock_shelf, build_wreck, build_ruin_pylon]
