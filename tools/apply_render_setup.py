"""Give the built office level the render look from office_assets.blend.

`office_assets.blend` is where the showreel look lives: the AgX Medium High Contrast
view transform, the light-grey world, the EEVEE ray-tracing settings, and a soft
area-light rig. The level should not have its own second-hand copy of those numbers -
this script reads them out of the asset scene and applies them to the level, so the
asset scene stays the one place the look is authored.

What is copied verbatim, because it does not depend on what is being rendered:

    render engine, resolution percentage, pixel aspect, fps, frame range,
    image format/colour mode/depth, film transparency, motion blur, filter size,
    display device, view transform, look, exposure, gamma,
    EEVEE samples / shadows / ray tracing / fast GI / shadow rays and steps / threshold,
    and the world (appended from the asset blend, not re-created)

What has to be adapted, because the asset scene is a 108 m review row and the level is a
50 x 36 m building:

  * **Lights.** The asset rig is seven 9.6 m key softboxes 18 m apart plus three fills -
    no use indoors. The level already has one little emissive ceiling panel in each room,
    so each of its point lights becomes an area light the size of the panel it sits in
    (read off that panel's own mesh, so the two cannot drift apart) and facing down.
    `--panel-energy` sets what each panel emits; the default is a little above the point
    light it replaces, because an area light throws its power into a hemisphere rather
    than a sphere.
  * **Depth of field.** The showreel camera has it on at the same f-stop; the level's
    inspection cameras get it too, focused on whatever the camera is actually pointing at
    (found by ray-casting down the view axis, since none of them has a focus object).
  * **Shadow pool.** Fourteen lit rooms plus a sun overrun the 512 MB shadow atlas that is
    plenty for the asset scene, so the per-light shadow resolution is scaled down to fit
    it (see LEVEL_SHADOW_RESOLUTION_SCALE) - no extra VRAM, and indistinguishable in the
    rendered frames.
  * **Loose ends.** The level had no active camera and rendered to /tmp, so opening it and
    pressing F12 did not work and a render went somewhere disposable. It gets a camera and
    an output directory next to the .blend instead.

Run it as the last step of the level pipeline, or straight on a built level:

    blender --background --factory-startup \
        --python tools/build_office_level.py \
        --python tools/apply_render_setup.py

    blender -b ~/Work/office-level.blend --python tools/apply_render_setup.py

Options (after `--`): --reference PATH, --panel-energy W, --world-strength S,
--sun-scale S, --shadow-pool MB, --shadow-scale S, --camera NAME, --output PATH,
--no-lights, --no-dof, --keep-output, --no-save.
"""

import os
import sys

import bpy

REFERENCE = os.path.expanduser("~/Work/office_assets.blend")
REF_WORLD = "Office_World"
FALLBACK_PANEL = (1.6, 0.5)
DEFAULT_PANEL_ENERGY = 600.0

# EEVEE's shadow atlas is a fixed pool of pages: 512 MB, its default and the asset scene's
# setting, is 2048 pages. The level lights fourteen rooms plus a sun over 354 meshes and
# asks for 2074, at which point EEVEE logs "Shadow buffer full" and starts dropping whole
# lights' shadows - 36% of one test frame's pixels changed once it was fixed. Halving the
# per-light shadow resolution quarters the pages needed, so it fits in the default pool
# with no extra VRAM and renders to within 0.09 grey levels of the 1024 MB pool alternative.
# `--shadow-pool 1024 --shadow-scale 1.0` is the other way out, if VRAM is not the concern.
LEVEL_SHADOW_RESOLUTION_SCALE = 0.5

# So that opening the level and pressing F12 just works: an active camera to render from,
# and somewhere in the user's home rather than /tmp to put the file.
DEFAULT_CAMERA = "Cam_Reception"
DEFAULT_OUTPUT = "//render_level/level_"

# Scene-level settings cannot be *appended* from another .blend like a datablock can, so
# they are read by opening the reference file and copying what is listed here.
RENDER_ATTRS = ("resolution_percentage", "pixel_aspect_x", "pixel_aspect_y",
                "fps", "fps_base", "film_transparent", "use_motion_blur", "filter_size")
IMAGE_ATTRS = ("file_format", "color_mode", "color_depth", "compression")
EEVEE_ATTRS = ("taa_render_samples", "use_shadows", "use_raytracing", "use_fast_gi",
               "fast_gi_method", "shadow_ray_count", "shadow_step_count",
               "light_threshold", "use_shadow_jitter_viewport", "use_overscan")


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


ARGS = argv_after_ddash()


def opt(name, default=None):
    return ARGS[ARGS.index(name) + 1] if name in ARGS else default


def flag(name):
    return name in ARGS


def read_reference():
    """Open the asset scene, copy its look into a plain dict, and note its camera."""
    bpy.ops.wm.open_mainfile(filepath=REFERENCE)
    scene = bpy.context.scene
    ref = {
        "engine": scene.render.engine,
        "render": {a: getattr(scene.render, a) for a in RENDER_ATTRS},
        "image": {a: getattr(scene.render.image_settings, a) for a in IMAGE_ATTRS},
        "frame_range": (scene.frame_start, scene.frame_end),
        "display_device": scene.display_settings.display_device,
        "view": {a: getattr(scene.view_settings, a)
                 for a in ("view_transform", "look", "exposure", "gamma")},
        "eevee": {a: getattr(scene.eevee, a) for a in EEVEE_ATTRS if hasattr(scene.eevee, a)},
        "fstop": None,
    }
    cam = scene.camera
    if cam is not None and cam.data.dof.use_dof:
        ref["fstop"] = cam.data.dof.aperture_fstop
    return ref


def apply_reference(scene, ref, world_strength):
    scene.render.engine = ref["engine"]
    for attr, value in ref["render"].items():
        setattr(scene.render, attr, value)
    for attr, value in ref["image"].items():
        setattr(scene.render.image_settings, attr, value)
    scene.frame_start, scene.frame_end = ref["frame_range"]
    scene.display_settings.display_device = ref["display_device"]
    for attr, value in ref["view"].items():
        setattr(scene.view_settings, attr, value)
    for attr, value in ref["eevee"].items():
        setattr(scene.eevee, attr, value)

    # Append the world rather than rebuilding it, so its nodes are literally the asset
    # scene's. Replace any copy from an earlier run instead of leaving an .001 behind.
    for existing in [w for w in bpy.data.worlds if w.name.startswith(REF_WORLD)]:
        existing.name = f"__replaced__{existing.name}"
        bpy.data.worlds.remove(existing)
    with bpy.data.libraries.load(REFERENCE, link=False) as (src, dst):
        dst.worlds = [REF_WORLD]
    world = bpy.data.worlds.get(REF_WORLD)
    if world is None:
        raise RuntimeError(f"world '{REF_WORLD}' not found in {REFERENCE}")
    scene.world = world

    background = next(n for n in world.node_tree.nodes if n.type == "BACKGROUND")
    if world_strength is not None:
        background.inputs["Strength"].default_value = world_strength
    strength = background.inputs["Strength"].default_value
    print(f"  world     '{world.name}' colour="
          f"{tuple(round(c, 3) for c in background.inputs['Color'].default_value)} "
          f"strength={strength:.3f}")
    print(f"  view      {ref['view']['view_transform']} / {ref['view']['look']}")
    print(f"  eevee     samples={scene.eevee.taa_render_samples} "
          f"raytracing={scene.eevee.use_raytracing} "
          f"fast_gi={scene.eevee.fast_gi_method}")
    print(f"  render    {scene.render.resolution_x}x{scene.render.resolution_y} "
          f"{scene.render.image_settings.file_format} "
          f"{scene.render.image_settings.color_mode}{scene.render.image_settings.color_depth} "
          f"frames {scene.frame_start}-{scene.frame_end} @ {scene.render.fps}fps")


def panel_size_for(light_name):
    """The visible ceiling panel this light hangs under, as (size, size_y)."""
    panel = bpy.data.objects.get(light_name[:-len("_light")]) if light_name.endswith("_light") else None
    if panel is not None and panel.type == "MESH":
        x, y, _ = panel.dimensions
        if x > 0.01 and y > 0.01:
            return x, y
    return FALLBACK_PANEL


def relight(scene, energy, sun_scale):
    """Each ceiling point light becomes an area light the size of its own panel."""
    converted = 0
    for ob in scene.objects:
        if ob.type != "LIGHT":
            continue
        if ob.data.type == "SUN":
            # Anchored to the value the level was built with, so re-running the tool (which
            # is meant to happen after every rebuild) can never compound the scale.
            base = ob.get("base_energy", ob.data.energy)
            ob["base_energy"] = base
            ob.data.energy = base * sun_scale
            print(f"  sun       energy {ob.data.energy:.2f} (base {base:.2f})")
            continue
        if ob.data.type != "POINT" or energy <= 0:
            continue

        size_x, size_y = panel_size_for(ob.name)
        area = bpy.data.lights.new(ob.data.name, type="AREA")
        area.shape = "RECTANGLE"
        area.size, area.size_y = size_x, size_y
        area.energy = energy
        area.color = ob.data.color
        area.use_shadow = ob.data.use_shadow
        ob.data = area
        ob.rotation_mode = "XYZ"
        ob.rotation_euler = (0.0, 0.0, 0.0)  # area lights face -Z, i.e. straight down
        converted += 1
    if converted:
        print(f"  lights    {converted} ceiling point lights -> {energy:.0f} W area panels")
    else:
        print("  lights    left as they were")

    pool = opt("--shadow-pool")
    if pool:
        scene.eevee.shadow_pool_size = pool  # enum identifier, e.g. "1024"
    scene.eevee.shadow_resolution_scale = float(
        opt("--shadow-scale", LEVEL_SHADOW_RESOLUTION_SCALE))
    print(f"  shadows   pool {scene.eevee.shadow_pool_size} MB, "
          f"resolution scale {scene.eevee.shadow_resolution_scale}")


def focus_cameras(scene, fstop):
    """Give the inspection cameras the showreel's depth of field, focused ahead of them."""
    import mathutils

    if fstop is None:
        print("  dof       reference camera has none, skipped")
        return
    for cam in [o for o in scene.objects if o.type == "CAMERA"]:
        # Where the camera is actually looking: cast down the view axis and stop at the
        # first surface, which is what a viewer reads as "in focus".
        origin = cam.matrix_world.translation
        direction = cam.matrix_world.to_quaternion() @ mathutils.Vector((0.0, 0.0, -1.0))
        hit, location, *_ = scene.ray_cast(bpy.context.view_layer.depsgraph,
                                          origin, direction)
        distance = (location - origin).length if hit else 10.0
        cam.data.dof.use_dof = True
        cam.data.dof.focus_distance = max(distance, cam.data.clip_start * 2.0)
        cam.data.dof.aperture_fstop = fstop
        print(f"  dof       {cam.name}: focus {cam.data.dof.focus_distance:.2f} m, "
              f"f/{fstop:.1f}")


def pick_camera(scene, name):
    """Give the scene an active camera, so F12 has something to render."""
    cameras = [o for o in scene.objects if o.type == "CAMERA"]
    if not cameras:
        raise RuntimeError("the level has no cameras to render from")
    chosen = next((c for c in cameras if c.name == (name or DEFAULT_CAMERA)), cameras[0])
    scene.camera = chosen
    print(f"  camera    {chosen.name} ({chosen.data.lens:.0f} mm)"
          + ("" if len(cameras) == 1 else
             f", others: {', '.join(c.name for c in cameras if c is not chosen)}"))


def main():
    if not os.path.isfile(REFERENCE):
        raise RuntimeError(f"reference scene not found: {REFERENCE}")
    level_path = bpy.data.filepath
    if not level_path:
        raise RuntimeError("the level must be saved before its render setup can be applied")

    print("RENDER_SETUP from", REFERENCE)
    ref = read_reference()

    bpy.ops.wm.open_mainfile(filepath=level_path)
    scene = bpy.context.scene

    world_strength = opt("--world-strength")
    apply_reference(scene, ref, float(world_strength) if world_strength else None)

    if not flag("--no-lights"):
        energy = float(opt("--panel-energy", DEFAULT_PANEL_ENERGY))
        relight(scene, energy, float(opt("--sun-scale", "1.0")))
    if not flag("--no-dof"):
        focus_cameras(scene, ref["fstop"])
    pick_camera(scene, opt("--camera"))
    if not flag("--keep-output"):
        scene.render.filepath = opt("--output", DEFAULT_OUTPUT)
    print(f"  output    {scene.render.filepath}")

    if flag("--no-save"):
        print("RENDER_SETUP not saved (--no-save)")
        return

    bpy.ops.wm.save_mainfile()
    print(f"RENDER_SETUP saved {bpy.data.filepath}")


if __name__ == "__main__":
    main()
