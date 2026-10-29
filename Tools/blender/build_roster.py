"""build_roster.py -- the wider roster's models, and an exporter for them alone.

    Blender --background --factory-startup --python Tools/blender/build_roster.py -- <out_dir>

Same authoring rules as build_models.py: game space, +Y up, +Z forward, origin on
the ground at the model's centre, silhouette first, the shared primitive library
and palette, movable parts as Model.group (UnitView animates them by name).

    THUMPER        mortar infantry: a Trooper's frame, tube over the shoulder
    SCORCHER       flame infantry: fuel tanks on the back, a lance with a pilot light
    MARKSMAN       sniper: long rifle and scope, a slimmer pack, a cowl
    PROSPECTOR     armed ore truck: six wheels, a cab, a drill under the nose, a hopper
    PROSPECTOR_TURRET  its machine gun on the cab roof (Barrel kicks back)
    LANCER_HULL    low, fast tank destroyer on tracks
    LANCER_TURRET  a flat turret with one very long gun
    AEGIS_HULL     anti-aircraft tank: a boxy hull on six wheels
    AEGIS_TURRET   twin cannons angled up and a radar dish
    HAULER         heavy ore carrier: tracks, a cab and a great hopper
    KESTREL        gunship: a fuselage between two ducted rotors, rocket pods
    SWIFT          interceptor: a delta wing, twin fins, one hot engine
    ROCKETEER      anti-armour infantry: a long rocket launcher level on the shoulder
    IRONCLAD_LEGS  the mini-mech's legs and pelvis (LegL/LegR swing)
    IRONCLAD_TORSO its turning upper half: twin shoulder cannons, missile pods
    MECHHUNTER_HULL    light four-wheeled chassis, thin armour, a flag on a pole (Banner)
    MECHHUNTER_TURRET  an open mount with a long coil lance for killing Mechs (Barrel)
    ARMORY         upgrade works: forge hall, chimney, weapon racks
    AIRFIELD       landing pads, a tower and a hangar
    BIOLAB         glass vats round a domed hall, the creatures' cradle

Only these are exported (with their chunks for structures), and their entries are
merged into models.json: the full export_fbx.py run rewrites every FBX
byte-for-byte differently, so it is not used for an addition.
"""

import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: F401
from mathutils import Matrix, Vector

import sf_model as S
import build_models as B
from sf_model import Model, box, frustum, cyl, tube, sphere, ring_flat, inset, face_plate, xform

MESH_BASE = 40
_DROPPED = []
LANCER_TURRET_Y = 1.18
AEGIS_TURRET_Y = 1.30
IRONCLAD_TORSO_Y = 2.30
PROSPECTOR_TURRET_Y = 1.50
MECHHUNTER_TURRET_Y = 0.92


def turn(bm, angle, axis, pivot=(0, 0, 0)):
    """build_models.rot, for a part turned on purpose: its built box no longer bounds it."""
    S._EXPECT.pop(id(bm), None)
    return B.rot(bm, angle, axis, pivot)


def _strip_group(m, name):
    """Drop a group's parts from a model built by another builder (the Trooper's rifle).
    The dropped parts are kept alive (and their bounds records cleared): sf_model keys its
    records by id(bmesh), and a freed part's id reused by a new one hands it a stale record."""
    for p, g in zip(m.parts, m.part_groups):
        if g == name:
            S._EXPECT.pop(id(p), None)
            _DROPPED.append(p)
    keep = [(p, g) for p, g in zip(m.parts, m.part_groups) if g != name]
    m.parts = [p for p, _ in keep]
    m.part_groups = [g for _, g in keep]
    m.groups.pop(name, None)
    return m


def _infantry(mesh_id, name):
    """A Trooper's frame without its rifle: legs, chest, pauldrons, pack, helmet."""
    m = B.build_trooper()
    _strip_group(m, 'Gun')
    m.mesh_id = mesh_id
    m.name = name
    m.group('')
    return m


# ---------------------------------------------------------------- infantry
def build_thumper():
    """Thumper: a mortar tube carried over the right shoulder, the baseplate slung on
    the back and a bandolier of shells -- the tube is the silhouette."""
    m = _infantry(MESH_BASE + 0, 'THUMPER')
    # Shell racks either side of the pack.
    for side in (1, -1):
        m.add(box((0.24 * side, 1.18, -0.44), (0.07, 0.20, 0.06), 'armor_dark', bevel_w=0.02))
        for k in range(3):
            m.add(cyl((0.24 * side, 1.02 + k * 0.13, -0.50), 0.035, 0.035, 0.10, 6, 'glow_dim', axis='z', bevel_w=0.0))
    # Baseplate on the back.
    plate = cyl((0, 1.30, -0.50), 0.36, 0.36, 0.07, 12, 'steel', axis='z', bevel_w=0.015)
    m.add(plate)
    # The tube: over the shoulder, muzzle up and forward; it kicks back when fired.
    m.group('Gun', (0.30, 1.40, 0.0))
    tube_ = cyl((0.32, 1.10, -0.55), 0.12, 0.12, 1.40, 12, 'armor_dark', axis='z', bevel_w=0.015)
    turn(tube_, math.radians(-38), 'X', (0.32, 1.40, 0.0))
    m.add(tube_)
    muzzle = cyl((0.32, 1.10, 0.82), 0.15, 0.15, 0.12, 12, 'dark', axis='z', bevel_w=0.015)
    turn(muzzle, math.radians(-38), 'X', (0.32, 1.40, 0.0))
    m.add(muzzle)
    band = cyl((0.32, 1.10, 0.10), 0.13, 0.13, 0.14, 12, 'team', axis='z', bevel_w=0.0)
    turn(band, math.radians(-38), 'X', (0.32, 1.40, 0.0))
    m.add(band)
    m.add(box((0.32, 1.12, 0.12), (0.05, 0.06, 0.10), 'steel', bevel_w=0.015))
    m.group('')
    return m


def build_scorcher():
    """Scorcher: two fat fuel tanks on the back, banded in team colour, and a short lance
    with a pilot light at the nozzle. Heavier boots and a sealed visor."""
    m = _infantry(MESH_BASE + 1, 'SCORCHER')
    # Two fat tanks standing out behind the pack, so the back reads as a fuel rig, not a Trooper's.
    for side in (1, -1):
        m.add(cyl((0.17 * side, 0.86, -0.62), 0.17, 0.17, 0.92, 12, 'rust', bevel_w=0.03))
        m.add(cyl((0.17 * side, 1.78, -0.62), 0.17, 0.06, 0.14, 12, 'armor_dark', bevel_w=0.0))
        m.add(cyl((0.17 * side, 1.12, -0.62), 0.182, 0.182, 0.12, 12, 'team', bevel_w=0.0))
        m.add(cyl((0.17 * side, 1.48, -0.62), 0.182, 0.182, 0.06, 12, 'team_dark', bevel_w=0.0))
    # Hose from the tanks over the right hip to the lance.
    hose = cyl((0.30, 1.02, -0.50), 0.035, 0.035, 0.68, 6, 'dark', axis='z', bevel_w=0.0)
    m.add(hose)
    # Visor band: a hot amber slit instead of the Trooper's cyan.
    m.add(box((0, 1.74, 0.17), (0.12, 0.03, 0.03), 'glow_amber', bevel_w=0.0))
    m.group('Gun', (0.30, 1.10, 0.10))
    m.add(box((0.30, 1.08, 0.24), (0.07, 0.08, 0.20), 'armor_dark', bevel_w=0.02))
    m.add(cyl((0.30, 1.10, 0.40), 0.05, 0.065, 0.46, 8, 'steel', axis='z', bevel_w=0.012))
    m.add(cyl((0.30, 1.10, 0.86), 0.075, 0.075, 0.08, 8, 'dark', axis='z', bevel_w=0.012))
    m.add(box((0.30, 1.02, 0.86), (0.025, 0.025, 0.03), 'glow_warm', bevel_w=0.0))
    m.group('')
    return m


def build_marksman():
    """Marksman: a long rifle with a scope and a bipod, a slim pack and a cowl over
    the helmet; the barrel is half again his height."""
    m = _infantry(MESH_BASE + 2, 'MARKSMAN')
    # Cowl draped over the helmet and shoulders.
    m.add(frustum((0, 1.70, -0.06), (0.24, 0.10, 0.22), top=(0.70, 0.75), mat='armor_dark', bevel_w=0.04))
    m.add(box((0, 1.76, 0.17), (0.04, 0.03, 0.03), 'glow_cyan', bevel_w=0.0))
    m.group('Gun', (0.30, 1.22, 0.10))
    m.add(box((0.30, 1.20, 0.18), (0.055, 0.08, 0.26), 'dark', bevel_w=0.02))
    m.add(cyl((0.30, 1.24, 0.42), 0.03, 0.026, 1.20, 8, 'steel', axis='z', bevel_w=0.0))
    m.add(cyl((0.30, 1.24, 1.60), 0.05, 0.05, 0.14, 8, 'dark', axis='z', bevel_w=0.01))
    # Scope.
    m.add(cyl((0.30, 1.36, 0.08), 0.045, 0.045, 0.36, 8, 'armor_dark', axis='z', bevel_w=0.01))
    m.add(cyl((0.30, 1.36, 0.44), 0.05, 0.05, 0.02, 8, 'glow_cyan', axis='z', bevel_w=0.0))
    # Folded bipod under the barrel.
    m.add(box((0.30, 1.16, 0.70), (0.02, 0.02, 0.16), 'steel', bevel_w=0.0))
    m.group('')
    return m


def build_prospector():
    """Prospector: an armed ore truck -- six wheels under an armoured body, a cab at the
    front with a mining drill under its nose that spins as it cuts (group Cutter), a
    hopper behind whose glass glows with the load (group Load), and a machine-gun
    turret on the cab roof (PROSPECTOR_TURRET, mounted at PROSPECTOR_TURRET_Y)."""
    m = Model(MESH_BASE + 3, 'PROSPECTOR')
    for side in (1, -1):
        for zc in (-0.95, -0.05, 0.85):
            # cyl() grows along +x from its base: centre each wheel on x = +-0.80.
            m.add(cyl((0.80 * side - 0.13, 0.36, zc), 0.36, 0.36, 0.26, 12, 'rubber', axis='x', bevel_w=0.03))
            m.add(cyl((0.93 * side - (0.03 if side < 0 else 0.0), 0.36, zc), 0.17, 0.17, 0.03, 8, 'steel', axis='x', bevel_w=0.0))
        # Mudguards over the wheels.
        m.add(frustum((0.80 * side, 0.80, -0.05), (0.17, 0.06, 1.42), top=(1.15, 0.95), shift=(0.03 * side, 0.0),
                      mat='armor_dark', bevel_w=0.03))
    m.add(frustum((0, 0.60, -0.05), (0.66, 0.18, 1.38), top=(1.0, 1.0), mat='dark', bevel_w=0.04))
    # Cab: an armoured box with a sloped glacis and a slit of glass.
    cab = frustum((0, 1.12, 0.78), (0.70, 0.36, 0.55), top=(0.86, 0.72), shift=(0.0, -0.10), mat='armor', bevel_w=0.05)
    roof = face_plate(cab, 'y', 1)
    if roof:
        inset(cab, roof, 0.08, -0.02, 'armor_lit')
    m.add(cab)
    m.add(frustum((0, 1.27, 1.27), (0.52, 0.07, 0.05), top=(0.9, 1.0), mat='glow_cyan', bevel_w=0.0))
    for side in (1, -1):
        m.add(box((0.665 * side, 1.08, 0.78), (0.03, 0.17, 0.38), 'team', bevel_w=0.012))
        m.add(box((0.48 * side, 0.82, 1.36), (0.09, 0.06, 0.04), 'glow_amber', bevel_w=0.0))
    # The drill under the nose: a toothed cone on its own axis (z), spun by UnitView.
    m.add(box((0, 0.56, 1.40), (0.30, 0.17, 0.12), 'armor_dark', bevel_w=0.03))
    m.group('Cutter', (0.0, 0.56, 1.60))
    m.add(cyl((0, 0.56, 1.52), 0.22, 0.03, 0.55, 10, 'steel', axis='z', bevel_w=0.0))
    for k in range(4):
        a = 2.0 * math.pi * k / 4
        m.add(box((math.cos(a) * 0.15, 0.56 + math.sin(a) * 0.15, 1.66), (0.03, 0.03, 0.14), 'dark', bevel_w=0.0))
    m.group('')
    # The hopper: flared and armoured, ore glass down each side, the heap on top.
    m.add(frustum((0, 1.18, -0.62), (0.74, 0.40, 0.78), top=(1.10, 1.06), mat='armor_dark', bevel_w=0.04))
    for side in (1, -1):
        m.add(box((0.79 * side, 1.18, -0.62), (0.035, 0.14, 0.52), 'ore_glow', bevel_w=0.0))
        m.add(box((0.80 * side, 0.95, -0.62), (0.035, 0.05, 0.70), 'team', bevel_w=0.01))
    m.group('Load', (0.0, 1.58, -0.62))
    m.add(frustum((0, 1.63, -0.62), (0.64, 0.08, 0.68), top=(0.55, 0.6), mat='crystal', bevel_w=0.02))
    m.group('')
    m.add(tube((-0.52, 1.10, 0.20), 0.07, 0.05, 0.62, 8, 'dark', axis='y'))
    m.add(box((0.40, 0.74, -1.47), (0.22, 0.12, 0.05), 'rust', bevel_w=0.02))
    m.add(cyl((0, 1.48, 0.70), 0.30, 0.30, 0.05, 10, 'dark', bevel_w=0.01))
    return m


def build_prospector_turret():
    """The Prospector's roof gun: a small shielded ring mount with a machine gun that
    kicks back (group Barrel) when it fires."""
    m = Model(MESH_BASE + 17, 'PROSPECTOR_TURRET')
    m.add(cyl((0, 0.0, 0), 0.28, 0.24, 0.16, 10, 'armor', bevel_w=0.02))
    m.add(frustum((0, 0.30, 0.20), (0.24, 0.16, 0.04), top=(0.8, 1.0), mat='armor_dark', bevel_w=0.015))
    m.group('Barrel', (0.0, 0.26, 0.10))
    m.add(box((0, 0.26, 0.02), (0.07, 0.07, 0.16), 'dark', bevel_w=0.01))
    m.add(cyl((0, 0.26, 0.16), 0.035, 0.03, 0.70, 8, 'steel', axis='z', bevel_w=0.0))
    m.add(cyl((0, 0.26, 0.82), 0.05, 0.05, 0.08, 8, 'dark', axis='z', bevel_w=0.0))
    m.group('')
    m.add(box((0.16, 0.20, -0.12), (0.06, 0.08, 0.08), 'team', bevel_w=0.01))
    return m


# ---------------------------------------------------------------- tanks
def build_lancer_hull():
    """Lancer hull: long, low and narrow -- a tank destroyer, not a brawler -- with a
    sharp glacis and side skirts in team colour."""
    m = Model(MESH_BASE + 4, 'LANCER_HULL')
    B.track_unit(m, 1.12, 0.34, 6, 1.95, 0.44, 0.56)
    for side in (1, -1):
        m.add(frustum((1.12 * side, 0.96, -0.05), (0.32, 0.14, 1.80), top=(1.2, 0.95), shift=(0.06 * side, 0.0),
                      mat='armor_dark', bevel_w=0.04))
        for zc in (-0.9, 0.0, 0.9):
            m.add(frustum((1.40 * side, 0.92, zc), (0.04, 0.17, 0.40), top=(1.0, 0.9), shift=(0.05 * side, 0.0),
                          mat='team', bevel_w=0.02))
    low = frustum((0, 0.58, 0), (0.86, 0.22, 1.85), top=(1.08, 1.0), mat='dark', bevel_w=0.05)
    m.add(low)
    up = frustum((0, 0.98, -0.15), (0.92, 0.18, 1.55), top=(0.92, 0.88), shift=(0.0, -0.05), mat='armor', bevel_w=0.05)
    deck = face_plate(up, 'y', 1)
    if deck:
        inset(up, deck, 0.14, -0.03, 'armor_lit')
    m.add(up)
    # The glacis: one long sloped plate, the shape that says "fast".
    gl = frustum((0, 0.86, 1.62), (0.86, 0.22, 0.42), top=(0.75, 0.35), shift=(0.0, -0.18), mat='armor_lit', bevel_w=0.04)
    turn(gl, math.radians(24), 'X', (0, 0.86, 1.62))
    m.add(gl)
    # Engine louvres at the back.
    m.add(frustum((0, 1.20, -1.40), (0.70, 0.08, 0.36), top=(0.95, 0.9), mat='armor_dark', bevel_w=0.03))
    for i in range(4):
        m.add(box((0, 1.29, -1.62 + i * 0.14), (0.56, 0.02, 0.04), 'shadowed', bevel_w=0.0))
    for side in (1, -1):
        m.add(cyl((0.62 * side, 1.00, 1.86), 0.08, 0.08, 0.05, 10, 'glow_dim', axis='z', bevel_w=0.0))
    return m


def build_lancer_turret():
    """Lancer turret: a flat, sloped disc with a very long gun -- the gun is the unit."""
    m = Model(MESH_BASE + 5, 'LANCER_TURRET')
    shell = frustum((0, 0.20, -0.15), (0.70, 0.20, 0.80), top=(0.62, 0.70), shift=(0.0, -0.08), mat='armor', bevel_w=0.05)
    roof = face_plate(shell, 'y', 1)
    if roof:
        inset(shell, roof, 0.12, -0.025, 'armor_lit')
    m.add(shell)
    m.add(box((0, 0.26, -0.88), (0.48, 0.14, 0.18), 'armor_dark', bevel_w=0.035))
    m.add(box((0, 0.42, -0.86), (0.36, 0.025, 0.12), 'team', bevel_w=0.01))
    m.group('Barrel', (0.0, 0.22, 0.60))
    m.add(box((0, 0.22, 0.70), (0.24, 0.17, 0.14), 'armor_dark', bevel_w=0.04))
    m.add(cyl((0, 0.22, 0.80), 0.10, 0.085, 2.30, 12, 'steel', axis='z', bevel_w=0.015))
    m.add(cyl((0, 0.22, 1.60), 0.13, 0.13, 0.20, 12, 'armor_dark', axis='z', bevel_w=0.02))
    m.add(cyl((0, 0.22, 3.05), 0.13, 0.13, 0.22, 12, 'dark', axis='z', bevel_w=0.02))
    m.group('')
    m.add(cyl((-0.30, 0.42, -0.30), 0.16, 0.14, 0.12, 10, 'armor_dark', bevel_w=0.02))
    m.add(box((-0.30, 0.52, -0.18), (0.08, 0.025, 0.02), 'glow_cyan', bevel_w=0.0))
    return m


def build_aegis_hull():
    """Aegis hull: tall and boxy on six big wheels -- the opposite of the Lancer's line --
    so the anti-aircraft tank reads at a glance."""
    m = Model(MESH_BASE + 6, 'AEGIS_HULL')
    for side in (1, -1):
        for zc in (-1.15, 0.0, 1.15):
            # cyl() grows along +x from its base: centre each wheel on x = +-1.05.
            m.add(cyl((1.05 * side - 0.17, 0.46, zc), 0.46, 0.46, 0.34, 12, 'rubber', axis='x', bevel_w=0.03))
            m.add(cyl((1.22 * side - (0.04 if side < 0 else 0.0), 0.46, zc), 0.22, 0.22, 0.04, 8, 'steel', axis='x', bevel_w=0.0))
        m.add(frustum((1.02 * side, 0.98, 0.0), (0.18, 0.12, 1.70), top=(1.2, 0.95), shift=(0.04 * side, 0.0),
                      mat='armor_dark', bevel_w=0.035))
    body = frustum((0, 0.92, 0.0), (0.82, 0.42, 1.78), top=(0.92, 0.88), mat='armor', bevel_w=0.06)
    deck = face_plate(body, 'y', 1)
    if deck:
        inset(body, deck, 0.14, -0.03, 'armor_lit')
    m.add(body)
    m.add(frustum((0, 1.00, 1.85), (0.70, 0.30, 0.16), top=(0.85, 0.6), shift=(0.0, -0.08), mat='armor_lit', bevel_w=0.04))
    for side in (1, -1):
        m.add(box((0.86 * side, 1.20, -0.4), (0.04, 0.20, 0.80), 'team', bevel_w=0.015))
        m.add(cyl((0.50 * side, 1.00, 1.98), 0.08, 0.08, 0.05, 10, 'glow_dim', axis='z', bevel_w=0.0))
    m.add(box((0.45, 1.40, -1.40), (0.25, 0.08, 0.30), 'rust', bevel_w=0.03))
    return m


def build_aegis_turret():
    """Aegis turret: a squat drum with twin cannons angled up at the sky and a spinning
    radar dish behind (group Head)."""
    m = Model(MESH_BASE + 7, 'AEGIS_TURRET')
    m.add(cyl((0, 0.0, 0), 0.78, 0.66, 0.42, 10, 'armor', bevel_w=0.04))
    m.add(ring_flat((0, 0.42, 0), 0.40, 0.66, 10, 'team', thickness=0.04))
    m.group('Barrels', (0.0, 0.62, 0.30))
    m.add(box((0, 0.62, 0.36), (0.42, 0.20, 0.22), 'armor_dark', bevel_w=0.035))
    for side in (1, -1):
        b = cyl((0.26 * side, 0.62, 0.50), 0.065, 0.055, 1.50, 10, 'steel', axis='z', bevel_w=0.01)
        turn(b, math.radians(-18), 'X', (0.26 * side, 0.62, 0.36))
        m.add(b)
        mz = cyl((0.26 * side, 0.62, 1.90), 0.09, 0.09, 0.14, 10, 'dark', axis='z', bevel_w=0.01)
        turn(mz, math.radians(-18), 'X', (0.26 * side, 0.62, 0.36))
        m.add(mz)
    m.group('')
    m.group('Head', (0.0, 0.70, -0.45))
    m.add(cyl((0, 0.44, -0.45), 0.05, 0.05, 0.36, 6, 'steel', bevel_w=0.0))
    dish = cyl((0, 0.82, -0.45), 0.42, 0.10, 0.10, 12, 'armor_lit', axis='z', bevel_w=0.0)
    turn(dish, math.radians(-25), 'X', (0, 0.82, -0.45))
    m.add(dish)
    m.add(box((0, 0.82, -0.40), (0.04, 0.04, 0.04), 'glow_cyan', bevel_w=0.0))
    m.group('')
    return m


# ---------------------------------------------------------------- workers
def build_hauler():
    """Hauler: a slow, armoured ore carrier -- a long tracked chassis, a small cab at
    the front and a great hopper behind whose glass glows with the load."""
    m = Model(MESH_BASE + 8, 'HAULER')
    B.track_unit(m, 1.08, 0.38, 5, 1.80, 0.48, 0.60)
    m.add(frustum((0, 0.70, 0), (0.92, 0.24, 1.82), top=(1.02, 1.0), mat='dark', bevel_w=0.05))
    # Cab.
    cab = frustum((0, 1.30, 1.25), (0.78, 0.40, 0.50), top=(0.85, 0.70), shift=(0.0, -0.08), mat='armor', bevel_w=0.05)
    m.add(cab)
    m.add(frustum((0, 1.42, 1.62), (0.62, 0.16, 0.06), top=(0.9, 1.0), mat='glow_cyan', bevel_w=0.0))
    m.add(box((0, 1.74, 1.20), (0.60, 0.04, 0.30), 'team', bevel_w=0.015))
    # The hopper: wide, flared, armoured, with ore glass down each side.
    hop = frustum((0, 1.42, -0.55), (0.98, 0.50, 1.15), top=(1.12, 1.06), mat='armor_dark', bevel_w=0.05)
    m.add(hop)
    for side in (1, -1):
        m.add(box((1.02 * side, 1.40, -0.55), (0.03, 0.18, 0.80), 'ore_glow', bevel_w=0.0))
        for zc in (-1.3, -0.55, 0.2):
            m.add(box((1.10 * side, 1.20, zc), (0.05, 0.30, 0.06), 'armor', bevel_w=0.015))
    m.group('Load', (0.0, 1.92, -0.55))
    m.add(frustum((0, 1.96, -0.55), (0.88, 0.10, 1.02), top=(0.55, 0.6), mat='crystal', bevel_w=0.02))
    m.group('')
    m.add(tube((-0.50, 1.70, 0.85), 0.10, 0.07, 0.50, 8, 'dark', axis='y'))
    m.add(box((0.60, 1.10, 1.78), (0.10, 0.10, 0.06), 'glow_amber', bevel_w=0.0))
    m.add(box((-0.60, 1.10, 1.78), (0.10, 0.10, 0.06), 'glow_amber', bevel_w=0.0))
    return m


# ---------------------------------------------------------------- aircraft
def build_kestrel():
    """Kestrel: a gunship -- a blunt fuselage between two ducted rotors on stub wings,
    rocket pods under them, a sensor ball on the chin. Built with its belly at y=0:
    the game flies it."""
    m = Model(MESH_BASE + 9, 'KESTREL')
    body = frustum((0, 0.55, 0.0), (0.46, 0.38, 1.35), top=(0.80, 0.85), shift=(0.0, -0.05), mat='armor', bevel_w=0.06)
    roof = face_plate(body, 'y', 1)
    if roof:
        inset(body, roof, 0.08, -0.02, 'armor_lit')
    m.add(body)
    m.add(frustum((0, 0.72, 1.36), (0.34, 0.24, 0.32), top=(0.70, 0.55), shift=(0.0, -0.08), mat='armor_lit', bevel_w=0.04))
    m.add(frustum((0, 0.95, 0.85), (0.24, 0.12, 0.40), top=(0.7, 0.6), mat='glow_cyan', bevel_w=0.02))
    # Tail boom and fin.
    m.add(frustum((0, 0.62, -1.80), (0.16, 0.14, 0.70), top=(0.7, 1.0), mat='armor_dark', bevel_w=0.03))
    m.add(frustum((0, 1.00, -2.30), (0.04, 0.34, 0.24), top=(1.0, 0.6), shift=(0.0, -0.12), mat='team', bevel_w=0.01))
    m.add(sphere((0, 0.22, 1.30), 0.16, 10, 6, 'dark'))
    m.add(box((0, 0.22, 1.44), (0.06, 0.04, 0.03), 'glow_amber', bevel_w=0.0))
    for side in (1, -1):
        m.add(box((0.85 * side, 0.62, 0.0), (0.45, 0.07, 0.30), 'armor_dark', bevel_w=0.025))
        # Duct ring and the rotor spinning inside it.
        m.add(tube((1.45 * side, 0.45, 0.0), 0.62, 0.54, 0.34, 16, 'armor', axis='y'))
        m.add(ring_flat((1.45 * side, 0.79, 0.0), 0.54, 0.64, 16, 'team', thickness=0.02))
        m.group('RotorR' if side > 0 else 'RotorL', (1.45 * side, 0.62, 0.0))
        m.add(cyl((1.45 * side, 0.56, 0.0), 0.10, 0.10, 0.12, 8, 'dark', bevel_w=0.0))
        for k in range(3):
            blade = box((1.45 * side, 0.62, 0.0), (0.50, 0.012, 0.06), 'steel', bevel_w=0.0)
            turn(blade, 2.0 * math.pi * k / 3, 'Y', (1.45 * side, 0.62, 0.0))
            m.add(blade)
        m.group('')
        # Rocket pod under the wing root.
        m.add(cyl((0.82 * side, 0.30, -0.35), 0.15, 0.15, 0.80, 8, 'armor_dark', axis='z', bevel_w=0.02))
        m.add(cyl((0.82 * side, 0.30, 0.45), 0.13, 0.13, 0.02, 8, 'shadowed', axis='z', bevel_w=0.0))
    return m


def build_swift():
    """Swift: an interceptor -- a needle nose, a broad delta wing, twin canted fins and
    one hot engine. Belly at y=0: the game flies it."""
    m = Model(MESH_BASE + 10, 'SWIFT')
    m.add(frustum((0, 0.42, 0.10), (0.32, 0.26, 1.40), top=(0.65, 0.95), mat='armor', bevel_w=0.05))
    m.add(cyl((0, 0.45, 1.45), 0.26, 0.02, 0.95, 10, 'armor_lit', axis='z', bevel_w=0.0))
    m.add(frustum((0, 0.70, 0.55), (0.18, 0.10, 0.45), top=(0.6, 0.7), mat='glow_cyan', bevel_w=0.02))
    # The delta: a thin slab tapered to the tips.
    for side in (1, -1):
        w = frustum((0.95 * side, 0.36, -0.45), (0.80, 0.04, 0.85), top=(1.0, 1.0), mat='armor_dark', bevel_w=0.015)
        for v in w.verts:
            t = abs(v.co.x) / 1.75
            v.co.z = v.co.z * (1.0 - 0.75 * t) - 0.55 * t + 0.2 * (1 - t)
        S._EXPECT.pop(id(w), None)   # reshaped on purpose: no longer the box it was built as
        m.add(w)
        m.add(box((1.55 * side, 0.36, -0.70), (0.18, 0.05, 0.10), 'team', bevel_w=0.01))
        fin = frustum((0.30 * side, 0.82, -1.10), (0.04, 0.34, 0.32), top=(1.0, 0.55), shift=(0.0, -0.14), mat='team_dark', bevel_w=0.01)
        turn(fin, math.radians(-18 * side), 'Z', (0.30 * side, 0.50, -1.10))
        m.add(fin)
        m.add(cyl((0.45 * side, 0.30, 0.30), 0.04, 0.04, 0.60, 6, 'steel', axis='z', bevel_w=0.0))
    m.add(cyl((0, 0.42, -1.55), 0.24, 0.20, 0.30, 10, 'dark', axis='z', bevel_w=0.02))
    m.add(cyl((0, 0.42, -1.58), 0.17, 0.17, 0.04, 10, 'glow_amber', axis='z', bevel_w=0.0))
    return m


# ---------------------------------------------------------------- the Mech-Hunter
def build_mechhunter_hull():
    """Mech-Hunter hull: a light, quick chassis on four big wheels with thin plating -- it
    is not meant to take a hit -- and the order's flag on a pole at the back (group Banner,
    pivot at the crossbar, as the Mech's standard: UnitView hangs cloth there)."""
    m = Model(MESH_BASE + 18, 'MECHHUNTER_HULL')
    for side in (1, -1):
        for zc in (-0.92, 0.92):
            m.add(cyl((0.86 * side - 0.15, 0.42, zc), 0.42, 0.42, 0.30, 12, 'rubber', axis='x', bevel_w=0.03))
            m.add(cyl((1.01 * side - (0.03 if side < 0 else 0.0), 0.42, zc), 0.19, 0.19, 0.03, 8, 'steel', axis='x', bevel_w=0.0))
        # Open frame rails between the wheels: you can see through it.
        m.add(box((0.62 * side, 0.70, 0.0), (0.05, 0.05, 1.20), 'steel', bevel_w=0.0))
        m.add(box((0.66 * side, 0.88, 0.05), (0.03, 0.10, 0.70), 'team', bevel_w=0.01))
    m.add(frustum((0, 0.62, 0.0), (0.56, 0.14, 1.25), top=(0.95, 0.92), mat='armor_dark', bevel_w=0.03))
    m.add(frustum((0, 0.80, 1.05), (0.50, 0.08, 0.32), top=(0.85, 0.6), shift=(0.0, -0.12), mat='armor_lit', bevel_w=0.02))
    m.add(box((0, 0.84, -0.80), (0.40, 0.10, 0.36), 'dark', bevel_w=0.02))
    m.add(box((0.36, 0.72, 1.32), (0.07, 0.05, 0.03), 'glow_amber', bevel_w=0.0))
    m.add(box((-0.36, 0.72, 1.32), (0.07, 0.05, 0.03), 'glow_amber', bevel_w=0.0))
    m.add(cyl((0, 0.76, 0.0), 0.44, 0.44, 0.12, 12, 'dark', bevel_w=0.01))
    # The flag: a pole off the back corner, the standard hanging from its crossbar.
    bx, bz = -0.46, -1.10
    m.add(cyl((bx, 0.84, bz), 0.035, 0.03, 2.30, 8, 'steel', bevel_w=0.0))
    m.add(box((bx, 3.17, bz), (0.05, 0.05, 0.05), 'glow_amber', bevel_w=0.0))
    m.group('Banner', (bx, 3.00, bz))
    m.add(box((bx, 3.00, bz), (0.34, 0.025, 0.025), 'dark', bevel_w=0.0))
    m.add(box((bx, 2.42, bz - 0.01), (0.30, 0.56, 0.018), 'team', bevel_w=0.008))
    m.add(box((bx, 1.84, bz - 0.01), (0.30, 0.04, 0.02), 'dark', bevel_w=0.0))
    em = box((bx, 2.50, bz - 0.035), (0.11, 0.11, 0.012), 'glow_warm', bevel_w=0.0)
    B.rot(em, math.radians(45), 'Z', (bx, 2.50, bz - 0.035))
    m.add(em)
    m.group('')
    return m


def build_mechhunter_turret():
    """Mech-Hunter turret: an open mount behind a shield plate, carrying a lance far longer
    than the vehicle needs -- a coil gun for punching through a Mech's armour -- its coils
    lit (group Barrel kicks back)."""
    m = Model(MESH_BASE + 19, 'MECHHUNTER_TURRET')
    m.add(cyl((0, 0.0, 0), 0.40, 0.34, 0.18, 10, 'armor', bevel_w=0.02))
    m.add(frustum((0, 0.42, 0.34), (0.42, 0.26, 0.04), top=(0.85, 1.0), mat='armor_dark', bevel_w=0.015))
    m.add(box((0.30, 0.34, -0.24), (0.12, 0.14, 0.18), 'team', bevel_w=0.015))
    m.group('Barrel', (0.0, 0.40, 0.10))
    m.add(box((0, 0.40, -0.10), (0.16, 0.14, 0.34), 'dark', bevel_w=0.02))
    m.add(box((0, 0.40, -0.30), (0.10, 0.08, 0.10), 'glow_cyan', bevel_w=0.0))
    m.add(cyl((0, 0.40, 0.24), 0.08, 0.07, 2.70, 10, 'steel', axis='z', bevel_w=0.0))
    for k, zc in enumerate((0.70, 1.30, 1.90)):
        m.add(tube((0, 0.40, zc), 0.15, 0.09, 0.10, 10, 'glow_cyan', axis='z'))
    m.add(cyl((0, 0.40, 2.90), 0.12, 0.12, 0.18, 10, 'dark', axis='z', bevel_w=0.01))
    m.group('')
    return m


# ---------------------------------------------------------------- structures
def build_armory():
    """Armory: a forge hall with a pitched roof, a tall chimney with a glowing throat,
    and racks of barrels and plates outside -- where weapons and armour are made."""
    m = Model(MESH_BASE + 11, 'ARMORY')
    m.add(box((0, 0.15, 0), (3.40, 0.15, 3.10), 'dark', bevel_w=0.05))
    hall = frustum((0, 1.30, 0.2), (2.40, 1.05, 2.10), top=(0.96, 0.96), mat='armor', bevel_w=0.07)
    for ax in ('x',):
        B.panel(hall, ax, 1, 0.18, -0.04, 'armor_dark')
        B.panel(hall, ax, -1, 0.18, -0.04, 'armor_dark')
    m.add(hall)
    roof = frustum((0, 2.62, 0.2), (2.55, 0.30, 2.25), top=(0.15, 1.0), mat='armor_dark', bevel_w=0.05)
    m.add(roof)
    # The ridge stripe stops short of the gables (run to them, its ends lay in their planes).
    m.add(box((0, 2.92, 0.2), (0.12, 0.05, 2.18), 'team', bevel_w=0.015))
    # Great door, lit from inside.
    m.add(box((0, 1.05, 2.33), (1.10, 0.95, 0.06), 'shadowed', bevel_w=0.02))
    # The light under it stands proud of the door (flush with it, the two faces z-fought).
    m.add(box((0, 0.35, 2.41), (1.00, 0.15, 0.03), 'glow_warm', bevel_w=0.0))
    # Chimney.
    m.add(cyl((-1.70, 0.30, -1.70), 0.62, 0.48, 4.20, 10, 'armor_dark', bevel_w=0.05))
    m.add(cyl((-1.70, 4.50, -1.70), 0.52, 0.52, 0.20, 10, 'dark', bevel_w=0.03))
    m.add(cyl((-1.70, 4.62, -1.70), 0.36, 0.36, 0.06, 10, 'glow_amber', caps=True, bevel_w=0.0))
    m.add(ring_flat((-1.70, 2.80, -1.70), 0.56, 0.68, 10, 'team', thickness=0.10))
    # Weapon racks: barrels and armour plates outside the east wall.
    for k in range(4):
        m.add(cyl((2.75, 0.30, -1.2 + k * 0.55), 0.10, 0.10, 1.30, 8, 'steel', bevel_w=0.0))
    m.add(box((2.75, 1.70, -0.38), (0.10, 0.06, 1.05), 'armor_dark', bevel_w=0.02))
    for k in range(3):
        m.add(box((-2.78, 0.75, -0.9 + k * 0.85), (0.06, 0.55, 0.35), 'armor_lit', bevel_w=0.03))
    m.add(box((1.60, 0.55, 2.70), (0.45, 0.40, 0.35), 'rust', bevel_w=0.03))
    return m


def build_airfield():
    """Airfield: two landing pads ringed in team colour, a control tower with a glass cab
    and a beacon, and a curved hangar -- where aircraft are made and take off."""
    m = Model(MESH_BASE + 12, 'AIRFIELD')
    m.add(box((0, 0.12, 0), (4.60, 0.12, 4.20), 'dark', bevel_w=0.05))
    for x in (-2.20, 2.20):
        m.add(cyl((x, 0.24, 1.70), 1.70, 1.70, 0.10, 16, 'armor_dark', bevel_w=0.02))
        m.add(ring_flat((x, 0.34, 1.70), 1.30, 1.55, 16, 'team', thickness=0.02))
        for k in range(4):
            a = 2.0 * math.pi * k / 4 + math.pi / 4
            m.add(box((x + math.cos(a) * 1.62, 0.36, 1.70 + math.sin(a) * 1.62), (0.06, 0.03, 0.06), 'glow_cyan', bevel_w=0.0))
        m.add(box((x, 0.35, 1.70), (0.60, 0.012, 0.08), 'armor_lit', bevel_w=0.0))
        m.add(box((x, 0.35, 1.70), (0.08, 0.012, 0.60), 'armor_lit', bevel_w=0.0))
    # Hangar: a long half-barrel at the back.
    hang = cyl((0, 1.10, -2.30), 1.55, 1.55, 6.60, 12, 'armor', axis='x', bevel_w=0.04)
    xform(hang, Matrix.Translation(Vector((-3.30, -0.25, 0.0))))
    for v in hang.verts:
        if v.co.y < 0.24:
            v.co.y = 0.24
    m.add(hang)
    m.add(box((0, 1.00, -0.80), (2.70, 0.80, 0.05), 'shadowed', bevel_w=0.02))
    for x in (-2.0, 0.0, 2.0):
        m.add(box((x, 2.40, -2.30), (0.08, 0.06, 1.45), 'team', bevel_w=0.01))
    # Control tower.
    m.add(cyl((3.70, 0.24, -2.60), 0.55, 0.42, 3.80, 8, 'armor_dark', bevel_w=0.04))
    m.add(cyl((3.70, 4.00, -2.60), 0.95, 0.80, 0.80, 8, 'glow_cyan', bevel_w=0.02))
    m.add(cyl((3.70, 4.78, -2.60), 1.02, 0.60, 0.25, 8, 'armor', bevel_w=0.03))
    m.add(cyl((3.70, 5.03, -2.60), 0.04, 0.04, 0.80, 6, 'steel', bevel_w=0.0))
    m.add(box((3.70, 5.88, -2.60), (0.08, 0.08, 0.08), 'glow_amber', bevel_w=0.0))
    return m


def build_biolab():
    """Biolab: a low domed hall ringed by tall glass vats, each lit a sickly green with
    something curled inside -- the only structure that looks grown, not built."""
    m = Model(MESH_BASE + 13, 'BIOLAB')
    m.add(cyl((0, 0, 0), 4.10, 4.00, 0.30, 12, 'dark', bevel_w=0.05))
    m.add(cyl((0, 0.30, 0), 2.70, 2.55, 1.40, 12, 'armor', bevel_w=0.06))
    dome = sphere((0, 1.70, 0), 2.45, 16, 10, 'armor_dark')
    for v in dome.verts:
        if v.co.y < 1.70:
            v.co.y = 1.70
        else:
            v.co.y = 1.70 + (v.co.y - 1.70) * 0.62
    m.add(dome)
    m.add(ring_flat((0, 1.70, 0), 2.40, 2.75, 16, 'team', thickness=0.10))
    m.add(cyl((0, 3.18, 0), 0.55, 0.40, 0.40, 10, 'steel', bevel_w=0.03))
    m.add(cyl((0, 3.58, 0), 0.30, 0.30, 0.10, 10, 'glow_bio', bevel_w=0.0))
    for k in range(6):
        a = B.face_angle(k * 2, 12)
        x, z = math.cos(a) * 3.35, math.sin(a) * 3.35
        m.add(cyl((x, 0.30, z), 0.62, 0.62, 0.22, 10, 'armor_dark', bevel_w=0.03))
        m.add(cyl((x, 0.52, z), 0.52, 0.52, 2.10, 10, 'glow_bio', caps=False, bevel_w=0.0))
        m.add(sphere((x, 1.45, z), 0.30, 8, 6, 'dark'))
        m.add(cyl((x, 2.62, z), 0.62, 0.50, 0.24, 10, 'armor', bevel_w=0.03))
        pipe = cyl((x * 0.82, 2.70, z * 0.82), 0.07, 0.07, 1.0, 6, 'steel', axis='x', bevel_w=0.0)
        turn(pipe, -a, 'Y', (x * 0.82, 2.70, z * 0.82))
        m.add(pipe)
    m.add(box((0, 0.95, 2.62), (0.95, 0.65, 0.12), 'shadowed', bevel_w=0.03))
    return m


def build_rocketeer():
    """Rocketeer: a rocket launcher on the right shoulder, level and long, a flared
    exhaust cone behind the head and rockets racked on the back -- the tube runs the
    other way from the Thumper's, flat along the line of fire."""
    m = _infantry(MESH_BASE + 14, 'ROCKETEER')
    for side in (1, -1):
        m.add(cyl((0.13 * side, 1.08, -0.52), 0.07, 0.07, 0.52, 8, 'armor_dark', bevel_w=0.01))
        m.add(cyl((0.13 * side, 1.60, -0.52), 0.07, 0.0, 0.16, 8, 'glow_amber', bevel_w=0.0))
    m.group('Gun', (0.36, 1.62, 0.0))
    m.add(cyl((0.38, 1.66, -0.95), 0.13, 0.13, 1.85, 12, 'armor_dark', axis='z', bevel_w=0.015))
    m.add(cyl((0.38, 1.66, -1.15), 0.20, 0.13, 0.24, 12, 'dark', axis='z', bevel_w=0.01))
    m.add(cyl((0.38, 1.66, 0.85), 0.16, 0.16, 0.12, 12, 'steel', axis='z', bevel_w=0.01))
    m.add(cyl((0.38, 1.66, -0.10), 0.145, 0.145, 0.18, 12, 'team', axis='z', bevel_w=0.0))
    m.add(box((0.38, 1.86, 0.20), (0.04, 0.06, 0.12), 'armor_lit', bevel_w=0.01))
    m.add(box((0.38, 1.90, 0.24), (0.03, 0.03, 0.02), 'glow_cyan', bevel_w=0.0))
    m.add(box((0.38, 1.46, 0.10), (0.05, 0.12, 0.06), 'dark', bevel_w=0.01))
    m.group('')
    return m


def build_ironclad_legs():
    """Ironclad's lower half: two thick digitigrade legs on broad feet under a squat
    pelvis -- a walker the size of a small building's door, heavier than any tank."""
    m = Model(MESH_BASE + 15, 'IRONCLAD_LEGS')
    m.add(frustum((0, 2.05, 0), (0.85, 0.32, 0.70), top=(1.05, 1.0), mat='armor_dark', bevel_w=0.06))
    for side in (1, -1):
        x = 0.82 * side
        m.group('LegR' if side > 0 else 'LegL', (x, 2.05, 0.0))
        hip = cyl((x - 0.25 * side, 2.05, 0), 0.34, 0.34, 0.5, 10, 'steel', axis='x', bevel_w=0.03)
        m.add(hip)
        m.add(frustum((x, 1.55, 0.18), (0.30, 0.55, 0.34), top=(1.1, 1.0), shift=(0.0, 0.12), mat='armor', bevel_w=0.05))
        m.add(frustum((x, 0.80, -0.05), (0.24, 0.42, 0.26), top=(1.2, 1.1), shift=(0.0, 0.10), mat='armor_dark', bevel_w=0.04))
        m.add(box((x + 0.18 * side, 1.40, 0.30), (0.05, 0.30, 0.14), 'team', bevel_w=0.015))
        m.add(cyl((x - 0.18 * side, 1.10, 0.06), 0.18, 0.18, 0.36, 10, 'steel', axis='x', bevel_w=0.02))
        foot = frustum((x, 0.16, 0.10), (0.38, 0.16, 0.62), top=(0.75, 0.70), shift=(0.0, 0.08), mat='armor_dark', bevel_w=0.04)
        m.add(foot)
        for t in (-1, 0, 1):
            m.add(box((x + 0.24 * t, 0.10, 0.70), (0.08, 0.10, 0.14), 'dark', bevel_w=0.02))
        m.group('')
    return m


def build_ironclad_torso():
    """Ironclad's upper half (it turns on the pelvis): an armoured cab with a slit
    visor, a heavy cannon on each shoulder for the ground and a missile pod on each
    side for the air."""
    m = Model(MESH_BASE + 16, 'IRONCLAD_TORSO')
    cab = frustum((0, 0.55, -0.05), (0.95, 0.55, 0.80), top=(0.85, 0.80), shift=(0.0, -0.06), mat='armor', bevel_w=0.07)
    roof = face_plate(cab, 'y', 1)
    if roof:
        inset(cab, roof, 0.12, -0.03, 'armor_lit')
    m.add(cab)
    m.add(box((0, 0.78, 0.76), (0.55, 0.08, 0.05), 'glow_cyan', bevel_w=0.0))
    m.add(box((0, 1.13, -0.10), (0.60, 0.04, 0.45), 'team', bevel_w=0.015))
    m.add(frustum((0, 0.50, -0.95), (0.70, 0.40, 0.25), top=(0.9, 1.0), mat='armor_dark', bevel_w=0.05))
    m.group('Barrels', (0.0, 0.85, 0.40))
    for side in (1, -1):
        m.add(box((1.20 * side, 0.85, 0.15), (0.26, 0.26, 0.55), 'armor_dark', bevel_w=0.04))
        m.add(cyl((1.20 * side, 0.85, 0.65), 0.10, 0.09, 1.30, 10, 'steel', axis='z', bevel_w=0.01))
        m.add(cyl((1.20 * side, 0.85, 1.90), 0.15, 0.15, 0.18, 10, 'dark', axis='z', bevel_w=0.02))
    m.group('')
    for side in (1, -1):
        pod = box((1.25 * side, 1.45, -0.35), (0.34, 0.30, 0.42), 'armor', bevel_w=0.04)
        m.add(pod)
        for r in range(2):
            for c in range(3):
                m.add(cyl((1.25 * side + (c - 1) * 0.18, 1.32 + r * 0.26, 0.06), 0.065, 0.065, 0.04, 8, 'glow_amber', axis='z', bevel_w=0.0))
    return m


ROSTER_BUILDERS = [
    build_thumper, build_scorcher, build_marksman, build_prospector,
    build_lancer_hull, build_lancer_turret, build_aegis_hull, build_aegis_turret,
    build_hauler, build_kestrel, build_swift,
    build_armory, build_airfield, build_biolab,
    build_rocketeer, build_ironclad_legs, build_ironclad_torso,
    build_prospector_turret, build_mechhunter_hull, build_mechhunter_turret,
]
ROSTER_CHUNKED = {'ARMORY': 8, 'AIRFIELD': 10, 'BIOLAB': 9}


def main():
    import export_fbx as X
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out_dir = argv[0] if argv else os.path.join(os.getcwd(), 'Assets', 'StarForge', 'Art', 'Models')
    only = set(argv[1:])
    os.makedirs(out_dir, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)

    models = [fn() for fn in ROSTER_BUILDERS]
    if only:
        models = [m for m in models if m.name in only]
    meta_path = os.path.join(out_dir, 'models.json')
    meta = json.load(open(meta_path)) if os.path.exists(meta_path) else {}
    for i, model in enumerate(models):
        slots, rad, top = X.prepare(model)
        X.bake_ao(model, rad)
        objs, tris = X.export_groups(model, slots, out_dir)
        chunks = X.export_chunks(model, slots, ROSTER_CHUNKED[model.name], out_dir) if model.name in ROSTER_CHUNKED else []
        for o in objs[1:]:
            o.name = '%s_%s' % (model.name, o.name)
        for o in chunks:
            o.name = '%s_%s' % (model.name, o.name)
        objs[0].location.x = i * 7.0
        meta[model.name] = {'radius': round(rad, 3), 'height': round(top, 3), 'triangles': tris,
                            'materials': slots,
                            'groups': {g: [round(c, 3) for c in p] for g, p in model.groups.items()},
                            'chunks': len(chunks), 'lod1_triangles': 0}
        meta[model.name].update(model.meta)
        print('%-16s tris=%6d radius=%5.2f height=%5.2f groups=%s chunks=%d  %s' % (
            model.name, tris, rad, top, ','.join(sorted(model.groups)) or '-', len(chunks), ','.join(slots)))
    mounts = meta.setdefault('_mounts', {})
    mounts['LANCER_TURRET_Y'] = LANCER_TURRET_Y
    mounts['AEGIS_TURRET_Y'] = AEGIS_TURRET_Y
    mounts['IRONCLAD_TORSO_Y'] = IRONCLAD_TORSO_Y
    mounts['PROSPECTOR_TURRET_Y'] = PROSPECTOR_TURRET_Y
    mounts['MECHHUNTER_TURRET_Y'] = MECHHUNTER_TURRET_Y
    with open(meta_path, 'w') as fh:
        json.dump(meta, fh, indent=2)
    print('exported %d roster models to %s' % (len(models), out_dir))


if __name__ == '__main__':
    main()
