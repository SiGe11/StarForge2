"""Build the songbird StarForge flies: Quaternius's CC0 bird, re-posed and re-animated.

    /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
        --python Tools/blender/build_songbird.py -- [preview.png]

The source (Art/Source/quaternius/.../bird.fbx, fetched by Tools/fetch_assets.py)
is a perched wagtail: body upright, legs down, wings folded along the back, and
its one animation is a flutter of the folded wings. Flown as it was, a flock
read as upright dark blobs flapping in place. This keeps the mesh and its
skeleton and writes new cycles for them:

    Fly    wings spread and beating (a quick downstroke, the tips trailing on the
           way up), legs tucked, tail level
    Fold   the wings shut against the body, legs tucked: the glide between beats
           of a small bird's bounding flight
    Perch  standing, head turning now and then
    Peck   a dip of the head to the ground and back

shortens the tail to a finch's, and colours it through one small palette
texture (UVs per face: back, head, breast, wings, tail, beak and legs), so a
bird is one draw. Writes
Assets/StarForge/Art/Fauna/Songbird.fbx and Songbird_palette.png; SceneAssembler
builds the prefab from them. With a path after --, also renders previews there
(and with a second path, writes the model there instead of into the project).
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "Art", "Source", "quaternius", "Animals_Pack_by_Quaternius",
                   "Animals Pack by Quaternius", "FBX", "bird.fbx")
OUT = os.path.join(ROOT, "Assets", "StarForge", "Art", "Fauna")
FPS = 24

# Palette cells (sRGB), after a chaffinch: chestnut back, slate head, rusty breast,
# dark flight feathers with pale coverts. Seen from above against brown ground a
# plain brown bird disappears; the pale patches flicker as the wings beat.
PALETTE = {
    "back": (0.46, 0.31, 0.20),
    "breast": (0.76, 0.47, 0.34),
    "wing": (0.13, 0.11, 0.10),
    "covert": (0.54, 0.48, 0.40),
    "tail": (0.16, 0.14, 0.13),
    "beak": (0.30, 0.28, 0.26),
    "legs": (0.42, 0.34, 0.28),
    "belly": (0.84, 0.74, 0.62),
    "head": (0.38, 0.43, 0.50),
}
CELLS = list(PALETTE)

# The source rig's bones (unnamed in the file): legs, head chain, tail chain, wings.
LEGS = "Bone"
HEAD = "Bone.001"
TAIL = "Bone.003"
WING_R, WING_R2 = "Bone.006", "Bone.007"
WING_L, WING_L2 = "Bone.008", "Bone.009"


def load():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SRC)
    for o in list(bpy.data.objects):
        if o.type in ("CAMERA", "LIGHT"):
            bpy.data.objects.remove(o)
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh = next(o for o in bpy.data.objects if o.type == "MESH")
    if arm.animation_data:
        arm.animation_data.action = None
    return arm, mesh


def region(mesh, poly, groups):
    """Which palette cell a face gets, from where it sits and what moves it."""
    me = mesh.data
    c = mesh.matrix_world @ poly.center
    n = (mesh.matrix_world.to_3x3() @ poly.normal).normalized()
    votes = {}
    for vi in poly.vertices:
        v = me.vertices[vi]
        if v.groups:
            g = max(v.groups, key=lambda g: g.weight)
            votes[groups[g.group]] = votes.get(groups[g.group], 0) + 1
    main = max(votes, key=votes.get) if votes else ""
    if main in (WING_R, WING_L):
        return "covert"
    if main in (WING_R2, WING_L2):
        return "wing"
    if main in ("Bone.004", "Bone.005") or c.y > 1.2:
        return "tail"
    if c.z < -0.45:
        return "legs"
    if poly.material_index == 1:
        return "breast"
    if main in ("Bone.002",) and c.z > 1.0:
        # The head: a dark cap, the beak its forward tip.
        return "beak" if c.y < -1.65 else "head"
    if n.z < -0.35 or (n.y < -0.4 and c.z < 0.9):
        return "belly" if n.z < -0.6 else "breast"
    return "back"


def paint(mesh):
    """One UV map pointing every face at its palette cell; one material."""
    me = mesh.data
    groups = {g.index: g.name for g in mesh.vertex_groups}
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    uv = me.uv_layers.new(name="Palette")
    counts = {}
    for p in me.polygons:
        r = region(mesh, p, groups)
        counts[r] = counts.get(r, 0) + 1
        u = (CELLS.index(r) + 0.5) / len(CELLS)
        for li in p.loop_indices:
            uv.data[li].uv = (u, 0.5)
    mat = bpy.data.materials.new("songbird")
    me.materials.clear()
    me.materials.append(mat)
    for p in me.polygons:
        p.material_index = 0
    print("songbird faces by region", counts)


def write_palette(path):
    w = len(CELLS)
    img = bpy.data.images.new("palette", w, 1, alpha=False)
    px = []
    for k in CELLS:
        px += [*PALETTE[k], 1.0]
    img.pixels[:] = px
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()


# ------------------------------------------------------------ posing
def rot_about(pb, axis, deg, pivot=None):
    """Turn a pose bone about a world axis through its head (armature space = world here)."""
    arm = pb.id_data
    bpy.context.view_layer.update()
    m = pb.matrix.copy()
    p = pivot if pivot is not None else m.to_translation()
    r = Matrix.Rotation(math.radians(deg), 4, Vector(axis))
    pb.matrix = Matrix.Translation(p) @ r @ Matrix.Translation(-p) @ m
    bpy.context.view_layer.update()


def reset(arm):
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = Quaternion()
        pb.location = (0, 0, 0)
        pb.scale = (1, 1, 1)
    # A wagtail's tail is as long as the rest of the bird; a finch's is half that.
    arm.pose.bones[TAIL].scale = (0.8, 0.55, 0.8)
    bpy.context.view_layer.update()


def tuck_legs(arm):
    pb = arm.pose.bones[LEGS]
    rot_about(pb, (1, 0, 0), -75)          # feet swung back under the tail
    pb.scale = (0.6, 0.6, 0.6)


def wings(arm, spread, lift, tip_lift, sweep):
    """spread: degrees the folded wing swings out to the side; lift: up (+) or down (-)
    at the shoulder; tip_lift: the outer half's extra bend; sweep: tips drawn back."""
    for inner, outer, s in ((WING_R, WING_R2, 1), (WING_L, WING_L2, -1)):
        pi, po = arm.pose.bones[inner], arm.pose.bones[outer]
        rot_about(pi, (0, 0, 1), -s * spread)          # out from along the back to the side
        rot_about(pi, (0, 1, 0), -s * lift)            # beat about the body's long axis
        rot_about(po, (0, 0, 1), s * sweep)
        rot_about(po, (0, 1, 0), -s * tip_lift)


def key(arm, frame):
    for pb in arm.pose.bones:
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        pb.keyframe_insert("location", frame=frame)
        pb.keyframe_insert("scale", frame=frame)


def new_action(arm, name):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    if arm.animation_data is None:
        arm.animation_data_create()
    arm.animation_data.action = act
    return act


def make_actions(arm):
    acts = []
    # Fly: 8 frames at 24 fps, three beats a second at its own rate (the game speeds it up).
    acts.append(new_action(arm, "Fly"))
    beat = [  # frame, spread, lift, tip lift, sweep
        (0, 80, 55, 10, 5),
        (2, 86, 10, -8, 0),
        (4, 84, -40, -12, 0),
        (6, 70, -5, 35, 35),
        (8, 80, 55, 10, 5),
    ]
    for f, sp, li, tl, sw in beat:
        reset(arm)
        tuck_legs(arm)
        wings(arm, sp, li, tl, sw)
        key(arm, f)
    # Fold: wings shut, legs tucked (a pose held for the glide between beats).
    acts.append(new_action(arm, "Fold"))
    for f in (0, 8):
        reset(arm)
        tuck_legs(arm)
        wings(arm, 8, 0, 0, 0)
        key(arm, f)
    # Perch: standing, the head turning left and right now and then.
    acts.append(new_action(arm, "Perch"))
    for f, yaw in ((0, 0), (10, 0), (13, 28), (26, 28), (29, -22), (42, -22), (45, 0), (60, 0)):
        reset(arm)
        rot_about(arm.pose.bones[HEAD], (0, 0, 1), yaw)
        key(arm, f)
    # Peck: head down to the ground and back up, twice.
    acts.append(new_action(arm, "Peck"))
    for f, dip in ((0, 0), (3, 62), (5, 62), (8, 0), (10, 0), (13, 62), (15, 62), (18, 0), (24, 0)):
        reset(arm)
        rot_about(arm.pose.bones[HEAD], (1, 0, 0), dip)
        key(arm, f)
    for a in acts:
        for fc in fcurves(a):
            for kp in fc.keyframe_points:
                kp.interpolation = "BEZIER" if a.name != "Fly" else "LINEAR"
    reset(arm)
    return acts


def fcurves(action):
    try:
        return list(action.fcurves)
    except AttributeError:
        out = []
        for layer in action.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    out += list(bag.fcurves)
        return out


def export(arm, mesh, acts):
    # Every action as its own clip: park each on an NLA track.
    arm.animation_data.action = None
    for a in acts:
        tr = arm.animation_data.nla_tracks.new()
        tr.name = a.name
        st = tr.strips.new(a.name, 0, a)
        st.name = a.name
    bpy.context.scene.render.fps = FPS      # the source file left it at 18
    bpy.context.scene.render.fps_base = 1.0
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(OUT, "Songbird.fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
        add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False,
        bake_anim_use_nla_strips=True, bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0, use_armature_deform_only=True, mesh_smooth_type="FACE")
    print("wrote", os.path.relpath(path, ROOT))


def preview(arm, mesh, acts, path):
    sc = bpy.context.scene
    mat = mesh.data.materials[0]
    mat.use_nodes = True
    nt = mat.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(OUT, "Songbird_palette.png"))
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs[0], nt.nodes["Principled BSDF"].inputs[0])
    sc.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE_NEXT"
    sc.render.resolution_x = sc.render.resolution_y = 320
    sc.render.film_transparent = False
    world = bpy.data.worlds.new("w")
    world.color = (0.5, 0.55, 0.6)
    sc.world = world
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3
    sun.rotation_euler = (math.radians(40), 0, math.radians(30))
    sc.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 9
    c = Vector((0, 1, 0.6))
    views = {"side": (c + Vector((12, 0, 0)), (90, 0, 90)), "front": (c + Vector((0, -12, 0)), (90, 0, 0)),
             "top": (c + Vector((0, 0, 12)), (0, 0, 0)), "above": (c + Vector((7, -7, 8)), (55, 0, 45))}
    shots = []
    arm.animation_data.action = None
    while arm.animation_data.nla_tracks:
        arm.animation_data.nla_tracks.remove(arm.animation_data.nla_tracks[0])
    for a in acts:
        arm.animation_data.action = a
        try:
            arm.animation_data.action_slot = a.slots[0]
        except Exception:
            pass
        frames = [0, 2, 4, 6] if a.name == "Fly" else [0, 4]
        if a.name == "Peck":
            frames = [0, 4]
        for f in frames:
            sc.frame_set(f)
            for vn, (loc, rot) in views.items():
                if a.name != "Fly" and vn in ("front",):
                    continue
                cam.location = loc
                cam.rotation_euler = tuple(math.radians(x) for x in rot)
                p = os.path.join(os.path.dirname(path), f"sb_{a.name}_{f}_{vn}.png")
                sc.render.filepath = p
                bpy.ops.render.render(write_still=True)
                shots.append(p)
    print("PREVIEWS", " ".join(shots))


def main():
    global OUT
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(args) > 1:
        OUT = args[1]          # a trial run: write somewhere other than the project
    arm, mesh = load()
    paint(mesh)
    os.makedirs(OUT, exist_ok=True)
    write_palette(os.path.join(OUT, "Songbird_palette.png"))
    acts = make_actions(arm)
    export(arm, mesh, acts)
    if args:
        preview(arm, mesh, acts, args[0])


main()
