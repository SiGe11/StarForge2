# build_celebrant.py -- the field celebrant (docs/lore section 4: a long white alb, a gold cord,
# a veil to the waist, a hand-lamp; no iron, no weapon). Blender 5.x headless:
#   Blender --background --factory-startup --python-expr "import sys; sys.dont_write_bytecode=True" \
#       --python build_celebrant.py -- <out.fbx> [<preview.png>]
# Her body is Blender Studio's "Body Female - Stylized" (Julien Kaspar; Human Base Meshes bundle
# v1.4.1, CC0 in the bundle's own asset data; Art/Source/blender_studio/, Tools/fetch_assets.py):
# centred, scaled to 1.76 m, arms and eyes taken off (the sleeves and the veil are where they were),
# decimated to the game's chunky look. Ours (the THIRD-PARTY row names it): the alb's long skirt and
# wide bell sleeves, the cord with its ends, the veil to the waist and the lamp held in both hands
# before her chest. Plain colours (URP/Lit on import, as the animals). She faces -Y, the project's
# front; export_fbx.py's axes (forward -Z, up Y, baked) give Unity (x, y, z) = (-x, z, -y) of
# Blender's: she faces +Z there.
import bpy, bmesh, math, os, sys
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "/tmp/Celebrant.fbx"
PREVIEW = argv[1] if len(argv) > 1 else None
SRC = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..", "blender_studio",
                                    "human-base-meshes-bundle-v1.4.1", "human_base_meshes_bundle.blend"))
BODY = "GEO-body_female_stylized"
HEIGHT = 1.76

bpy.ops.wm.read_factory_settings(use_empty=True)


def mat(name, rgb, metal=0.0, rough=0.8, emit=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*rgb, 1.0)
    b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    if emit > 0:
        b.inputs["Emission Color"].default_value = (*rgb, 1.0)
        b.inputs["Emission Strength"].default_value = emit
    m.diffuse_color = (*rgb, 1.0)
    return m


ALB = mat("Celebrant_Alb", (0.86, 0.85, 0.80), rough=0.9)
VEIL = mat("Celebrant_Veil", (0.93, 0.92, 0.88), rough=0.85)
CORD = mat("Celebrant_Cord", (0.83, 0.62, 0.22), metal=0.7, rough=0.35)
SKIN = mat("Celebrant_Skin", (0.72, 0.55, 0.45), rough=0.7)
BRASS = mat("Celebrant_Lamp", (0.62, 0.45, 0.20), metal=0.8, rough=0.4)
FLAME = mat("Celebrant_Flame", (1.0, 0.62, 0.25), emit=6.0)



with bpy.data.libraries.load(SRC, link=False) as (src, dst):
    dst.objects = [BODY]
o = dst.objects[0]
bpy.context.collection.objects.link(o)
for md in list(o.modifiers): o.modifiers.remove(md)
o.data.transform(o.matrix_world)
o.matrix_world = Matrix()
xs = [v.co.x for v in o.data.vertices]; ys = [v.co.y for v in o.data.vertices]; zs = [v.co.z for v in o.data.vertices]
cx, z0 = (min(xs) + max(xs)) * 0.5, min(zs)
k = HEIGHT / (max(zs) - z0)
o.data.transform(Matrix.Scale(k, 4) @ Matrix.Translation((-cx, 0.0, -z0)))
# Arms and hands off (the base stands in an A-pose; the sleeves are ours, posed where they were).
bm = bmesh.new()
bm.from_mesh(o.data)
bmesh.ops.delete(bm, geom=[v for v in bm.verts if abs(v.co.x) > 0.19 and v.co.z > 0.6], context='VERTS')
bm.to_mesh(o.data)
bm.free()
bpy.context.view_layer.objects.active = o
dec = o.modifiers.new("dec", 'DECIMATE')
dec.ratio = 0.12   # under the alb and the veil only the feet show: the game's chunky density
bpy.ops.object.modifier_apply(modifier=dec.name)
o.data.materials.clear()
o.data.materials.append(SKIN)
for p in o.data.polygons: p.material_index = 0
o.name = "Body"
# The sleeves' joints (shoulder, elbow, wrist), our pose: hands meeting before her chest at the lamp.
J = {"upperarm_l": Vector((0.1516, 0.0543, 1.4181)), "upperarm_r": Vector((-0.1516, 0.0543, 1.4181)),
     "lowerarm_l": Vector((0.2016, -0.0296, 1.1985)), "lowerarm_r": Vector((-0.2016, -0.0296, 1.1985)),
     "hand_l": Vector((0.0978, -0.2465, 1.2644)), "hand_r": Vector((-0.0978, -0.2465, 1.2644))}
body_obj = o


def lathe(name, profile, m, seg=14, sy=0.85, centre=(0.0, 0.0)):
    """A low-poly surface of revolution from (z, r) pairs, top to bottom, squashed front to back."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = []
    for z, r in profile:
        rings.append([bm.verts.new((centre[0] + r * math.cos(2 * math.pi * i / seg), centre[1] + r * sy * math.sin(2 * math.pi * i / seg), z))
                      for i in range(seg)])
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            bm.faces.new((a[i], b[i], b[(i + 1) % seg], a[(i + 1) % seg]))   # outward (the other way round drew inside out in Unity)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    o.data.materials.append(m)
    return o


def tube(name, points, m, seg=8):
    """A chunky tube through (point, radius) pairs: a sleeve. Open at both ends, faces both ways."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = []
    for k, (p, r) in enumerate(points):
        d = (points[min(k + 1, len(points) - 1)][0] - points[max(k - 1, 0)][0]).normalized()
        u = d.cross(Vector((0, 0, 1)))
        if u.length < 1e-3: u = d.cross(Vector((1, 0, 0)))
        u.normalize()
        v = d.cross(u)
        rings.append([bm.verts.new(p + r * (math.cos(2 * math.pi * i / seg) * u + math.sin(2 * math.pi * i / seg) * v)) for i in range(seg)])
    for a, b in zip(rings, rings[1:]):
        for i in range(seg):
            bm.faces.new((a[i], a[(i + 1) % seg], b[(i + 1) % seg], b[i]))
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o)
    o.data.materials.append(m)
    mod = o.modifiers.new("solid", 'SOLIDIFY')
    mod.thickness = 0.012
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return o


# The alb's skirt: from under the bodice to the ground, flaring.
lathe("AlbSkirt", [(1.10, 0.20), (0.92, 0.23), (0.6, 0.26), (0.3, 0.30), (0.03, 0.33), (0.02, 0.0)], ALB)
# Wide bell sleeves over both arms, shoulder to wrist, widening to the cuff that meets the lamp.
for s in ("l", "r"):
    sh, el, wr = J["upperarm_" + s], J["lowerarm_" + s], J["hand_" + s]
    cuff = wr + (wr - el).normalized() * 0.05   # a little past the wrist: the cuff hides where the hand was
    tube("Sleeve_" + s, [(sh, 0.08), (el, 0.095), (el.lerp(wr, 0.5), 0.12), (cuff, 0.155)], ALB)
# The veil: over the crown, closed in front of the face, falling over the shoulders and arms to the waist.
lathe("Veil", [(1.835, 0.0), (1.825, 0.06), (1.795, 0.11), (1.73, 0.145), (1.63, 0.16), (1.56, 0.19),
               (1.50, 0.25), (1.43, 0.30), (1.30, 0.325), (1.15, 0.338), (1.02, 0.348)], VEIL, seg=16, sy=0.8)
# The gold cord at the waist, just under the veil's hem, and its two ends down the front (-Y).
lathe("Cord", [(1.005, 0.205), (0.985, 0.21), (0.965, 0.205)], CORD, seg=16)
for dx in (-0.035, 0.035):
    lathe("CordEnd", [(0.98, 0.012), (0.62, 0.012), (0.6, 0.0)], CORD, seg=6, sy=1.0, centre=(dx, -0.205))
# The lamp, held in both hands before her chest: 1.5x the first one, the flame clear above it.
mid = (J["hand_l"] + J["hand_r"]) * 0.5
lx, ly, lz = mid.x, mid.y - 0.06, mid.z + 0.01
lathe("Lamp", [(lz + 0.045, 0.0), (lz + 0.042, 0.045), (lz + 0.015, 0.075), (lz - 0.018, 0.068), (lz - 0.033, 0.0)], BRASS, seg=10, sy=1.0, centre=(lx, ly))
lathe("Spout", [(lz + 0.018, 0.018), (lz + 0.018, 0.0)], BRASS, seg=6, sy=1.0, centre=(lx, ly - 0.09))
flame_base, flame_top = lz + 0.05, lz + 0.18
lathe("Flame", [(flame_top, 0.0), (lz + 0.10, 0.024), (flame_base, 0.0)], FLAME, seg=6, sy=1.0, centre=(lx, ly))
flame = Vector((lx, ly, (flame_base + flame_top) * 0.5))
unity = (-flame.x, flame.z, -flame.y)

# Nothing of the body may show through the veil or the alb: count vertices outside both shells.
VEIL_P = [(1.835, 0.0), (1.825, 0.06), (1.795, 0.11), (1.73, 0.145), (1.63, 0.16), (1.56, 0.19), (1.50, 0.25), (1.43, 0.30), (1.30, 0.325), (1.15, 0.338), (1.02, 0.348)]
SKIRT_P = [(1.10, 0.20), (0.92, 0.23), (0.6, 0.26), (0.3, 0.30), (0.03, 0.33)]
def rad(prof, z):
    for (za, ra), (zb, rb) in zip(prof, prof[1:]):
        if zb <= z <= za: return ra + (rb - ra) * (za - z) / max(1e-6, za - zb)
    return None
# Where the stylised head (its nose and brow) reaches past the veil, it is drawn in under it: the face
# is not seen, and hidden geometry is ours to shape.
for v in body_obj.data.vertices:
    x, y, z = v.co
    rv = rad(VEIL_P, z)
    if rv is not None and z > 1.10:
        e = math.sqrt((x / rv) ** 2 + (y / (0.8 * rv)) ** 2)
        if e > 0.95: v.co.x, v.co.y = x * 0.95 / e, y * 0.95 / e
out = 0
for v in body_obj.data.vertices:
    x, y, z = v.co
    rv, rs = rad(VEIL_P, z), rad(SKIRT_P, z)
    inside = (rv is not None and (x / rv) ** 2 + (y / (0.8 * rv)) ** 2 <= 1.0) or (rs is not None and (x / rs) ** 2 + (y / (0.85 * rs)) ** 2 <= 1.0) or z < 0.03 or z > 1.835
    out += not inside
print(f"POKE {out} of {len(body_obj.data.vertices)} body vertices outside the veil and the alb")

# One object, so one renderer: join, then export with the project's axes (export_fbx.py).
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
bpy.ops.object.select_all(action='DESELECT')
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
bpy.ops.object.join()
cel = bpy.context.view_layer.objects.active
cel.name = cel.data.name = "Celebrant"
bpy.ops.object.shade_flat()
os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH'}, apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
                         mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False, path_mode='STRIP')
zs = [v.co.z for v in cel.data.vertices]
print(f"CELEBRANT {len(cel.data.vertices)} verts, {len(cel.data.polygons)} faces, height {max(zs):.2f} m, "
      f"materials {[m.name for m in cel.data.materials]}; flame middle Blender ({flame.x:.3f}, {flame.y:.3f}, {flame.z:.3f}) "
      f"= Unity right {unity[0]:+.3f}, up {unity[1]:.3f}, forward {unity[2]:+.3f}")

if PREVIEW:
    # Front, side, back and the game camera's three-quarter view from above (pitch 48), workbench.
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.display.shading.show_backface_culling = True   # as Unity draws it: an inside-out part shows here
    sc.render.resolution_x, sc.render.resolution_y = 480, 720
    sc.world = sc.world or bpy.data.worlds.new("w")
    sc.world.color = (0.22, 0.23, 0.24)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 2.1
    shots = []
    views = (("front", 0.0, 90.0), ("side", 90.0, 90.0), ("back", 180.0, 90.0), ("game", 45.0, 42.0))
    for tag, yaw, tilt in views:
        a, t = math.radians(yaw), math.radians(tilt)
        d = 3.0
        cam.location = (d * math.sin(a) * math.sin(t), -d * math.cos(a) * math.sin(t), 0.95 + d * math.cos(t))
        cam.rotation_euler = (t, 0.0, a)
        cam.data.ortho_scale = 2.1 if tag != "game" else 2.6
        p = os.path.splitext(PREVIEW)[0] + f"_{tag}.png"
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
        shots.append(p)
    imgs = [bpy.data.images.load(p) for p in shots]
    w, h = imgs[0].size
    n = len(imgs)
    px = [0.0] * (w * n * h * 4)
    for k, im in enumerate(imgs):
        src = list(im.pixels)
        for y in range(h):
            o = (y * w * n + k * w) * 4
            px[o:o + w * 4] = src[y * w * 4:(y + 1) * w * 4]
    sheet = bpy.data.images.new("sheet", w * n, h)
    sheet.pixels = px
    sheet.filepath_raw = PREVIEW
    sheet.file_format = 'PNG'
    sheet.save()
    for p in shots: os.remove(p)
    print("PREVIEW", PREVIEW)
