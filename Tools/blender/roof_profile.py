# roof_profile.py — the roof of each structure model as a radial profile, for the
# flames FXDirector stands on a burning building (FXDirector.RoofOf: the player
# cannot read the meshes, so the numbers are baked into the code).
#
#   /Applications/Blender.app/Contents/MacOS/Blender --background --factory-startup \
#       --python-expr "import sys; sys.dont_write_bytecode=True" \
#       --python Tools/blender/roof_profile.py -- Assets/StarForge/Art/Models [NAME ...]
#
# For each model: its radius, then the height of its top surface (metres over the
# model's origin, which stands on the ground) at r/R = 0, 1/8 .. 1, taken as the
# 15th percentile of 96 rays round the circle. The low percentile is the point: a
# structure is not round (a Garrison's towers, a Workshop's bay), and a flame whose
# foot is a little inside the building comes out of its roof, while one above the
# real surface hangs in the air. Prints one C# row a model, in RoofOf's layout.
import bpy, sys, math
from mathutils.bvhtree import BVHTree
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
models = argv[0]
names = argv[1:] or ['FOUNDRY', 'GARRISON', 'WORKSHOP', 'BUNKHOUSE', 'ARMORY', 'AIRFIELD', 'BIOLAB', 'SENTINEL_BASE', 'MECH_BAY']
KNOTS, RAYS, PERCENTILE = 8, 96, 0.15

for name in names:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=f"{models}/SF_{name}.fbx")
    verts, polys = [], []
    for ob in bpy.context.scene.objects:
        if ob.type != 'MESH':
            continue
        base, mw = len(verts), ob.matrix_world
        verts += [mw @ v.co for v in ob.data.vertices]
        polys += [[base + i for i in p.vertices] for p in ob.data.polygons]
    if not verts:
        print('ROOF', name, 'no mesh')
        continue
    zmax = max(v.z for v in verts)
    rmax = max(math.hypot(v.x, v.y) for v in verts)
    tree = BVHTree.FromPolygons(verts, polys)
    row = []
    for i in range(KNOTS + 1):
        r = rmax * i / KNOTS * 0.999
        hs = []
        for j in range(RAYS):
            a = 2 * math.pi * j / RAYS
            hit = tree.ray_cast(Vector((r * math.cos(a), r * math.sin(a), zmax + 5)), Vector((0, 0, -1)))
            hs.append(max(0.0, hit[0].z) if hit[0] is not None else 0.0)
        hs.sort()
        row.append(hs[int(RAYS * PERCENTILE)])
    cells = ', '.join(f"{v:.2f}f".replace('0.00f', '0f') for v in [rmax] + row)
    print(f"ROOF {name}: {{ {cells} }}  // model height {zmax:.2f}")
