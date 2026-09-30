"""Render JangaFX's EmberGen simulations (CC0, OpenVDB) into the game's flipbooks.

    Blender --background --factory-startup --python Tools/blender/render_flipbooks.py -- <kind> [out_dir] [--preview]

Fetch the sources first: python3 Tools/fetch_assets.py --jangafx

Each frame of the sheet is rendered three times with Cycles, and the three are packed
premultiplied into one RGBA texture so the game can relight the smoke:

    R  the smoke lit by a sun high overhead (premultiplied by coverage)
    G  the fire's own light, as seen through the smoke in front of it: sqrt of its
       luminance over the sequence's bright end (the shader squares it back and
       colours it by heat)
    B  the smoke lit by the sky alone (premultiplied)
    A  coverage

SF_Flipbook composites out = sunColour * R + skyColour * B + fire(G), alpha A, with
Blend One OneMinusSrcAlpha, and crossfades between frames (Unity's animation blend
stream), so the fireball darkens what is behind it instead of adding to it.

--preview renders every 8th source frame at a quarter of the size into one contact
sheet, to choose the frame range. <kind>.json beside the sheet records the frame
range, the cell grid and where the ground is in a cell.
"""
import glob
import json
import os
import sys

import bpy
import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "Art", "Source", "jangafx")

# kind: source folder, first/last source frame, grid, cell size, how dense the smoke
# is, which grid glows, the camera's elevation, where the ground sits (0 = bottom of
# the cell), and how much of the flame grid is also smoke (a pure flame has none).
KINDS = {
    "explosion": dict(src="GroundExplosion/ground_explosion/ground_explosion_VDB", first=4, last=112,
                      grid=8, cell=256, density=4.0, glow="flames", smoke="density", elevation=28.0,
                      ground=0.12, albedo=0.55, flame_as_smoke=0.0),
    # Tongues of flame: a campfire's licks, 1.5 source frames apart so a particle playing
    # a stretch of the sheet moves at about the fire's own pace.
    "flame": dict(src="SmallCampfire/smallCampfire/smallCampfireVDB", first=40, last=135,
                  grid=8, cell=256, density=3.0, glow="flames", smoke="density", elevation=12.0,
                  ground=0.02, albedo=0.5, flame_as_smoke=0.4),
}

VOXEL = 0.02   # metres a voxel in the render scene


def frames_of(folder):
    files = glob.glob(os.path.join(SRC, folder, "*.vdb"))
    def num(p):
        digits = "".join(c for c in os.path.basename(p) if c.isdigit())
        return int(digits) if digits else 0
    return sorted(files, key=num)


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 24
    sc.cycles.use_denoising = True
    sc.cycles.volume_step_rate = 1.5
    sc.cycles.volume_max_steps = 512
    sc.cycles.max_bounces = 4
    sc.cycles.volume_bounces = 1
    sc.render.film_transparent = True
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.render.image_settings.file_format = "OPEN_EXR"
    sc.render.image_settings.color_depth = "32"
    sc.render.image_settings.color_mode = "RGBA"
    world = bpy.data.worlds.new("World")
    world.use_nodes = True
    sc.world = world
    return sc


def volume_material(k):
    """Principled Volume: smoke from the density grid, fire from the flame grid through
    a heat ramp. Returns (material, nodes to switch between passes)."""
    m = bpy.data.materials.new("sim")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    vol = nt.nodes.new("ShaderNodeVolumePrincipled")
    nt.links.new(vol.outputs["Volume"], out.inputs["Volume"])

    glow = nt.nodes.new("ShaderNodeAttribute")
    glow.attribute_name = k["glow"]
    dens_sum = nt.nodes.new("ShaderNodeMath")
    dens_sum.operation = "MULTIPLY_ADD"
    # density = smoke * k.density + flame * k.flame_as_smoke  (in per-voxel units)
    flame_part = nt.nodes.new("ShaderNodeMath")
    flame_part.operation = "MULTIPLY"
    flame_part.inputs[1].default_value = k["flame_as_smoke"]
    nt.links.new(glow.outputs["Fac"], flame_part.inputs[0])
    if k["smoke"]:
        smoke = nt.nodes.new("ShaderNodeAttribute")
        smoke.attribute_name = k["smoke"]
        nt.links.new(smoke.outputs["Fac"], dens_sum.inputs[0])
        dens_sum.inputs[1].default_value = k["density"]
    else:
        dens_sum.inputs[0].default_value = 0.0
        dens_sum.inputs[1].default_value = 0.0
    nt.links.new(flame_part.outputs[0], dens_sum.inputs[2])
    scale = nt.nodes.new("ShaderNodeMath")
    scale.operation = "MULTIPLY"
    scale.inputs[1].default_value = 1.0 / VOXEL * 0.02   # optical depth per voxel ~ grid * 0.02 * factor
    nt.links.new(dens_sum.outputs[0], scale.inputs[0])
    nt.links.new(scale.outputs[0], vol.inputs["Density"])
    vol.inputs["Color"].default_value = (k["albedo"],) * 3 + (1.0,)

    # The flame grid runs well past 1 in a hot core; kept linear (no clamping ramp), so
    # the core's structure survives into the sheet, which is normalised afterwards.
    strength = nt.nodes.new("ShaderNodeMath")
    strength.operation = "MULTIPLY"
    strength.inputs[1].default_value = 40.0
    nt.links.new(glow.outputs["Fac"], strength.inputs[0])
    nt.links.new(strength.outputs[0], vol.inputs["Emission Strength"])
    vol.inputs["Emission Color"].default_value = (1.0, 1.0, 1.0, 1.0)
    return m, vol, strength


def load_volume(path, name="sim"):
    vol = bpy.data.volumes.new(name)
    vol.filepath = path
    vol.grids.load()
    ob = bpy.data.objects.new(name, vol)
    ob.scale = (VOXEL, VOXEL, VOXEL)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def world_bounds(ob):
    import mathutils
    bpy.context.view_layer.update()
    e = ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
    pts = [ob.matrix_world @ mathutils.Vector(c) for c in e.bound_box]
    return np.array([[p.x, p.y, p.z] for p in pts])


def render_to_array(sc, path):
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    bpy.data.images.remove(img)
    os.remove(path)
    return px[::-1]   # top row first


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    preview = "--preview" in argv
    argv = [a for a in argv if not a.startswith("--")]
    kind = argv[0]
    out_dir = argv[1] if len(argv) > 1 else os.path.join(ROOT, "Assets", "StarForge", "Art", "Textures")
    k = KINDS[kind]
    files = frames_of(k["src"])
    first, last = k["first"], min(k["last"], len(files) - 1)
    grid, cell = k["grid"], k["cell"]
    n = grid * grid
    if preview:
        picks = list(range(0, len(files), 8))
        grid = int(np.ceil(np.sqrt(len(picks))))
        cell = 128
    else:
        picks = [int(round(first + (last - first) * i / (n - 1))) for i in range(n)]

    sc = reset_scene()
    sc.render.resolution_x = cell
    sc.render.resolution_y = cell
    if preview:
        sc.cycles.samples = 16

    # Bounds over the whole range, from a handful of frames (a sim only grows and drifts).
    probe = sorted(set(picks[:: max(1, len(picks) // 8)] + [picks[-1]]))
    lo, hi = np.full(3, 1e9), np.full(3, -1e9)
    for i in probe:
        ob = load_volume(files[i], "probe")
        b = world_bounds(ob)
        lo, hi = np.minimum(lo, b.min(0)), np.maximum(hi, b.max(0))
        data = ob.data
        bpy.data.objects.remove(ob)
        bpy.data.volumes.remove(data)
    centre = (lo + hi) / 2
    print("bounds", lo, hi)

    # An orthographic camera looking along +y (the sims' depth), raised by the elevation.
    import math
    import mathutils
    el = math.radians(k["elevation"])
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("cam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    fwd = mathutils.Vector((0.0, math.cos(el), -math.sin(el)))
    rot = fwd.to_track_quat("-Z", "Y")
    cam.rotation_euler = rot.to_euler()
    right = rot @ mathutils.Vector((1, 0, 0))
    up = rot @ mathutils.Vector((0, 1, 0))
    corners = np.array([[x, y, z] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
    cr = np.array(right)
    cu = np.array(up)
    u = corners @ cr
    v = corners @ cu
    ground = np.array([centre[0], centre[1], lo[2]])
    gv = ground @ cu
    span_u = max(abs(u - ground @ cr).max() * 2, 1e-3)
    # The ground sits at k.ground of the height from the bottom; the top must fit.
    span_v = max((v.max() - gv) / (1.0 - k["ground"]), (gv - v.min()) / max(k["ground"], 1e-3))
    size = max(span_u, span_v) * 1.04
    cam_data.ortho_scale = size
    mid_v = gv - k["ground"] * size + size / 2
    cam_pos = mathutils.Vector(ground) - fwd * 50.0
    cam_pos += mathutils.Vector(cu) * (mid_v - gv)
    cam.location = cam_pos
    cam_data.clip_end = 200.0

    # The grids' bounding boxes are far larger than what shows, so fit the frame to
    # what a quick, noisy render of every few frames actually covers.
    mat, vol, strength = volume_material(k)
    bg = sc.world.node_tree.nodes["Background"]
    bg.inputs["Strength"].default_value = 0.0
    strength.inputs[1].default_value = 0.0
    res, samples, denoise = sc.render.resolution_x, sc.cycles.samples, sc.cycles.use_denoising
    sc.render.resolution_x = sc.render.resolution_y = 96
    sc.cycles.samples = 2
    sc.cycles.use_denoising = False
    tmp = os.path.join(bpy.app.tempdir, "sf_flip.exr")
    cover = np.zeros((96, 96), dtype=bool)
    for i in picks[:: max(1, len(picks) // 12)]:
        ob = load_volume(files[i], "fit")
        ob.data.materials.append(mat)
        cover |= render_to_array(sc, tmp)[..., 3] > 0.03
        data = ob.data
        bpy.data.objects.remove(ob)
        bpy.data.volumes.remove(data)
    sc.render.resolution_x = sc.render.resolution_y = res
    sc.cycles.samples, sc.cycles.use_denoising = samples, denoise
    ys, xs = np.nonzero(cover)
    cu_c = np.array(cam.location) @ cu
    cr_c = np.array(cam.location) @ cr
    u0 = cr_c + (xs.min() / 96 - 0.5) * size
    u1 = cr_c + ((xs.max() + 1) / 96 - 0.5) * size
    v1 = cu_c + (0.5 - ys.min() / 96) * size
    v0 = cu_c + (0.5 - (ys.max() + 1) / 96) * size
    if k["ground"] < 0.05:
        v0 = min(v0, gv)          # a flame's foot stays on the bottom edge
    size = max(u1 - u0, v1 - v0) * 1.08
    cam_data.ortho_scale = size
    cam.location = mathutils.Vector(cr * ((u0 + u1) / 2) + cu * ((v0 + v1) / 2)) - fwd * 50.0 \
        + mathutils.Vector(np.array(fwd) * (np.array(ground) @ np.array(fwd)))
    ground_v = (gv - ((v0 + v1) / 2 - size / 2)) / size
    ground_u = (ground @ cr - ((u0 + u1) / 2 - size / 2)) / size
    print(f"fitted: {size:.2f} m square, ground at ({ground_u:.3f}, {ground_v:.3f}) of the cell")

    sun_data = bpy.data.lights.new("sun", "SUN")
    sun = bpy.data.objects.new("sun", sun_data)
    sc.collection.objects.link(sun)
    # High overhead and a little from the camera's side, as the game's sun mostly is.
    sun.rotation_euler = (math.radians(25), math.radians(-15), 0)
    sun_data.angle = math.radians(3)

    sheet = np.zeros((grid * cell, grid * cell, 4), dtype=np.float32)
    for idx, fi in enumerate(picks):
        ob = load_volume(files[fi])
        ob.data.materials.append(mat)
        # 1. Fire: no scattering, only the glow, still dimmed by the smoke in front of it.
        vol.inputs["Color"].default_value = (0, 0, 0, 1)
        strength.inputs[1].default_value = 40.0
        sun_data.energy = 0.0
        bg.inputs["Strength"].default_value = 0.0
        fire = render_to_array(sc, tmp)
        # 2. Sun: the smoke lit from above, no glow, no sky.
        vol.inputs["Color"].default_value = (k["albedo"],) * 3 + (1.0,)
        strength.inputs[1].default_value = 0.0
        sun_data.energy = 3.0
        sunlit = render_to_array(sc, tmp)
        # 3. Sky: the smoke under a uniform sky, no sun.
        sun_data.energy = 0.0
        bg.inputs["Color"].default_value = (1, 1, 1, 1)
        bg.inputs["Strength"].default_value = 1.0
        sky = render_to_array(sc, tmp)
        data = ob.data
        bpy.data.objects.remove(ob)
        bpy.data.volumes.remove(data)
        bg.inputs["Strength"].default_value = 0.0

        lum = np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
        a = np.clip(sunlit[..., 3], 0, 1)
        r0, c0 = (idx // grid) * cell, (idx % grid) * cell
        sheet[r0:r0 + cell, c0:c0 + cell, 0] = sunlit[..., :3] @ lum
        sheet[r0:r0 + cell, c0:c0 + cell, 1] = fire[..., :3] @ lum
        sheet[r0:r0 + cell, c0:c0 + cell, 2] = sky[..., :3] @ lum
        sheet[r0:r0 + cell, c0:c0 + cell, 3] = a
        print(f"frame {idx + 1}/{len(picks)} (source {fi}) alpha max {a.max():.2f} fire max {sheet[r0:r0+cell, c0:c0+cell, 1].max():.2f}", flush=True)

    # Normalise: lighting by the sheet's bright end, fire by its 99.7th percentile.
    for ch in (0, 2):
        top = np.percentile(sheet[..., ch][sheet[..., 3] > 0.05], 99.5) if (sheet[..., 3] > 0.05).any() else 1.0
        sheet[..., ch] = np.clip(sheet[..., ch] / max(top, 1e-6), 0, 1)
        sheet[..., ch] = np.minimum(sheet[..., ch], sheet[..., 3])   # stays premultiplied
    glow = sheet[..., 1]
    emax = np.percentile(glow[glow > 1e-3], 99.7) if (glow > 1e-3).any() else 1.0
    sheet[..., 1] = np.sqrt(np.clip(glow / max(emax, 1e-6), 0, 1))

    os.makedirs(out_dir, exist_ok=True)
    name = f"fx_{kind}{'_preview' if preview else ''}"
    h, w = sheet.shape[:2]
    img = bpy.data.images.new(name, w, h, alpha=True, float_buffer=False)
    img.alpha_mode = "CHANNEL_PACKED"   # four data channels, not a colour with alpha
    img.colorspace_settings.name = "Non-Color"
    img.pixels[:] = sheet[::-1].ravel()
    img.filepath_raw = os.path.join(out_dir, name + ".png")
    img.file_format = "PNG"
    img.save()
    meta = dict(kind=kind, source=k["src"], frames=[int(p) for p in picks], grid=grid, cell=cell,
                ground=[round(float(ground_u), 3), round(float(ground_v), 3)], cell_metres=round(float(size), 3),
                voxel_metres=VOXEL, elevation=k["elevation"])
    meta_dir = out_dir if preview else os.path.join(ROOT, "Tools", "blender", "flipbooks")
    os.makedirs(meta_dir, exist_ok=True)
    with open(os.path.join(meta_dir, name + ".json"), "w") as f:
        json.dump(meta, f, indent=1)
    print("wrote", img.filepath_raw)


if __name__ == "__main__":
    main()
