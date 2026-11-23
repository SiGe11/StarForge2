"""pose_preview.py -- a walker model's legs posed at rest, mid-stride and deeply bent (ThighX > ShinX > FootX
chained as the game chains them), to check the pins cover every joint before an export.

    Blender -b --factory-startup --python Tools/blender/pose_preview.py -- <fbx> <out_prefix>

Writes <out_prefix>_{rest,stride,bend}_{side,quarter}.png (workbench, about 3 s). Read only."""
import math
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
fbx, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx)
objs = {o.name.split('.')[0]: o for o in bpy.context.scene.objects}
print(sorted(objs))


def chain(child, parent):
    c, p = objs[child], objs[parent]
    mw = c.matrix_world.copy()
    c.parent = p
    c.matrix_world = mw


for t in 'RL':
    chain('Shin' + t, 'Thigh' + t)
    chain('Foot' + t, 'Shin' + t)
rest = {n: o.rotation_euler.copy() for n, o in objs.items()}

scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.show_cavity = True
scene.display.shading.show_object_outline = True
scene.display.shading.color_type = 'OBJECT'
scene.render.resolution_x = 900
scene.render.resolution_y = 700
scene.render.film_transparent = False
for i, (n, o) in enumerate(sorted(objs.items())):
    if o.type == 'MESH':
        o.color = (0.55 + 0.4 * ((i * 37) % 7) / 7, 0.55 + 0.4 * ((i * 53) % 5) / 5, 0.6, 1)

cam_data = bpy.data.cameras.new('cam')
cam_data.type = 'ORTHO'
cam_data.ortho_scale = 3.6
cam = bpy.data.objects.new('cam', cam_data)
scene.collection.objects.link(cam)
scene.camera = cam


def shoot(name, loc, look=Vector((0, 0, 1.15))):
    cam.location = Vector(loc)
    d = look - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = '%s_%s.png' % (out, name)
    bpy.ops.render.render(write_still=True)


def pose(thighR, shinR, footR, thighL, shinL, footL):
    for n, o in objs.items():
        if n in rest:
            o.rotation_euler = rest[n].copy()
    # Blender X is the game's X (the hinge); positive turns the bone's lower end forward... check by eye.
    for t, (a, b, c) in (('R', (thighR, shinR, footR)), ('L', (thighL, shinL, footL))):
        objs['Thigh' + t].rotation_euler.x += math.radians(a)
        objs['Shin' + t].rotation_euler.x += math.radians(b)
        objs['Foot' + t].rotation_euler.x += math.radians(c)
    bpy.context.view_layer.update()


pose(0, 0, 0, 0, 0, 0)
shoot('rest_side', (8, 0, 1.15))
shoot('rest_quarter', (6, -6, 3.5))
pose(30, -25, -5, -30, 35, -5)
shoot('stride_side', (8, 0, 1.15))
shoot('stride_quarter', (6, -6, 3.5))
pose(-35, 60, -25, 20, -40, 20)
shoot('bend_side', (8, 0, 1.15))
shoot('bend_quarter', (-6, -6, 2.5))
