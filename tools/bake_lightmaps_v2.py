"""Bake office lightmaps with a research-aligned Cycles workflow (v2).

This is a fresh bake path next to ``bake_lightmaps.py``. It keeps the same
outputs the game already reads:

  content/levels/office/office_lightmap.png
  content/levels/office/office_lightmap.json

What changed relative to v1 (and why), based on Blender docs / The Lightmapper
/ common game-lightmap practice:

  * UV: Smart UV Project + Average Island Scale + Pack Islands with a
    **fraction** margin in atlas pixels. Prefer continuous charts over
    Lightmap Pack (per-face islands). Still weld a throwaway copy before
    projecting so imported split-normal meshes do not become a triangle mosaic.
  * Sampling: adaptive sampling + path guiding + light tree. Interiors put
    noise in the shadows; adaptive spends samples there, guiding finds the
    ceiling panels. Cycles bake uses these render settings.
  * Bounces: more diffuse bounces on the higher tiers (shadow fill).
  * Denoise: offline OIDN ``RTLightmap`` via ``denoise_lightmap.py`` (Cycles'
    own denoise is a no-op on bakes).
  * Still bake a temporary joined proxy with originals ``hide_render``: per-object
    setup cost and per-object margin dilation are otherwise fatal on this level.

Usage::

    blender -b ~/Work/office-level.blend --python tools/bake_lightmaps_v2.py -- \\
        --quality preview

    blender -b ~/Work/office-level.blend --python tools/bake_lightmaps_v2.py -- \\
        --quality high --repack --device GPU

``--device`` is ``CPU`` (default) or ``GPU``. On GPU, Cycles compute devices
are enabled (CUDA preferred, then OPTIX / HIP / ONEAPI / METAL).

After ``--repack``, re-export the sector so TEXCOORD_1 matches::

    blender -b ~/Work/office-level.blend --python tools/export_level_glb.py
"""

from __future__ import annotations

import json
import math
import os
import subprocess
import sys
import time

import bmesh
import bpy
import numpy as np
from PIL import Image

LEVEL_ID = "office"
SECTOR_ID = "office"
LEVELS_DIR = None
UV_LAYER = "Lightmap"
ATLAS_IMAGE = "office_lightmap"
EXCLUDE_NAMES = {"Ground"}
BAKE_MARGIN_PX = 4
PACK_RESOLUTION = 4096

# preview: cheap sanity check. high: production-ish. production: slower, cleaner shadows.
# adaptive=0 means fixed sample count (predictable). A low adaptive threshold with a
# low min_samples can lock in self-shadow blacks before path guiding helps.
QUALITY = {
    "preview":    {"resolution": 1024, "samples": 64,   "min_samples": 64,  "bounces": 2,
                   "adaptive": 0.0,    "guiding": False},
    "medium":     {"resolution": 2048, "samples": 256,  "min_samples": 64,  "bounces": 3,
                   "adaptive": 0.01,   "guiding": True},
    "high":       {"resolution": 4096, "samples": 512,  "min_samples": 128, "bounces": 4,
                   "adaptive": 0.005,  "guiding": True},
    "production": {"resolution": 4096, "samples": 1024, "min_samples": 256, "bounces": 6,
                   "adaptive": 0.002,  "guiding": True},
}


def argv_after_ddash():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


ARGS = argv_after_ddash()


def opt(name, default=None):
    return ARGS[ARGS.index(name) + 1] if name in ARGS else default


def flag(name):
    return name in ARGS


def resolve_levels_dir():
    if LEVELS_DIR:
        return LEVELS_DIR
    try:
        repo_root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        candidate = os.path.join(repo_root, "content", "levels")
        if os.path.isdir(candidate):
            return candidate
    except NameError:
        pass
    raise RuntimeError("Cannot resolve content/levels.")


def hhmmss(seconds):
    if seconds < 60:
        return f"{seconds:.1f} s"
    if seconds < 3600:
        return f"{int(seconds // 60)}m {seconds % 60:04.1f}s"
    return f"{int(seconds // 3600)}h {int(seconds // 60) % 60:02d}m"


# ============================================================== gather
def targets_to_bake(only):
    scene = bpy.context.scene
    targets, skipped = [], {"name": 0, "hidden": 0, "render_hidden": 0,
                            "unselected": 0, "empty": 0, "no_material": 0}
    for obj in scene.objects:
        if obj.type != "MESH":
            continue
        if obj.name in EXCLUDE_NAMES:
            skipped["name"] += 1
            continue
        if obj.hide_get():
            skipped["hidden"] += 1
            continue
        if obj.hide_render:
            skipped["render_hidden"] += 1
            continue
        if only and only.lower() not in obj.name.lower():
            skipped["unselected"] += 1
            continue
        if not obj.data.polygons:
            skipped["empty"] += 1
            continue
        if not obj.data.materials:
            skipped["no_material"] += 1
            continue
        targets.append(obj)
    return targets, skipped


def uv_state(obj):
    layers = obj.data.uv_layers
    return {
        "active": layers.active.name if layers.active else None,
        "render": [l.name for l in layers if getattr(l, "active_render", False)],
    }


def restore_uv_state(obj, state):
    layers = obj.data.uv_layers
    for layer in layers:
        layer.active_render = layer.name in state["render"]
    if state["active"] and state["active"] in layers:
        layers.active = layers[state["active"]]


def face_key(me, poly):
    pts = [(round(v.x, 4), round(v.y, 4), round(v.z, 4))
           for v in (me.vertices[i].co for i in poly.vertices)]
    k = min(range(len(pts)), key=lambda i: pts[i])
    return tuple(pts[k:] + pts[:k])


def face_centroid_key(obj, poly, world=True):
    """Stable face id: centroid + normal, in world or local space."""
    me = obj.data
    if world:
        mw = obj.matrix_world
        pts = [mw @ me.vertices[i].co for i in poly.vertices]
        n = (mw.to_3x3() @ poly.normal).normalized()
    else:
        pts = [me.vertices[i].co for i in poly.vertices]
        n = poly.normal.normalized()
    c = sum(pts, pts[0].__class__((0, 0, 0))) / len(pts)
    return (round(c.x, 3), round(c.y, 3), round(c.z, 3),
            round(n.x, 2), round(n.y, 2), round(n.z, 2))


def uv_area(obj):
    me = obj.data
    layer = me.uv_layers[UV_LAYER]
    total = 0.0
    for poly in me.polygons:
        uvs = [layer.data[li].uv for li in poly.loop_indices]
        for k in range(1, len(uvs) - 1):
            (x0, y0), (x1, y1), (x2, y2) = uvs[0], uvs[k], uvs[k + 1]
            total += abs((x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)) * 0.5
    return total


def surface_area(obj):
    me = obj.data
    total = 0.0
    scale = obj.matrix_world.to_scale()
    for poly in me.polygons:
        verts = [me.vertices[i].co for i in poly.vertices]
        for k in range(1, len(verts) - 1):
            a, b, c = verts[0], verts[k], verts[k + 1]
            total += (b - a).cross(c - a).length * 0.5
    return total * abs(scale.x * scale.y)


# ============================================================== UV (native pack)
def ensure_lightmap_layer(obj):
    me = obj.data
    if UV_LAYER in me.uv_layers:
        me.uv_layers.remove(me.uv_layers[UV_LAYER])
    me.uv_layers.new(name=UV_LAYER)
    me.uv_layers.active = me.uv_layers[UV_LAYER]


def project_continuous_charts(targets):
    """Smart-project continuous charts via a welded throwaway copy per mesh.

    Imported office furniture is split at most edges. Projecting the live mesh
    gives one island per triangle. Welding on a copy, projecting, then copying
    UVs back by face position keeps stills' custom normals intact.
    """
    coll = bpy.context.scene.collection
    missing = 0
    for obj in targets:
        ensure_lightmap_layer(obj)
        dup = obj.copy()
        dup.data = obj.data.copy()
        coll.objects.link(dup)
        bm = bmesh.new()
        bm.from_mesh(dup.data)
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-4)
        bm.to_mesh(dup.data)
        bm.free()

        ensure_lightmap_layer(dup)
        bpy.ops.object.select_all(action="DESELECT")
        dup.select_set(True)
        bpy.context.view_layer.objects.active = dup
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        # Continuous charts; packing margin is applied later in pixel fraction.
        bpy.ops.uv.smart_project(
            angle_limit=math.radians(66.0),
            island_margin=0.0,
            area_weight=0.0,
            correct_aspect=True,
            scale_to_bounds=False,
        )
        bpy.ops.object.mode_set(mode="OBJECT")

        src = {}
        layer = dup.data.uv_layers[UV_LAYER]
        for poly in dup.data.polygons:
            uvs = [layer.data[li].uv.copy() for li in poly.loop_indices]
            src.setdefault(face_key(dup.data, poly), []).append(uvs)
        bpy.data.objects.remove(dup, do_unlink=True)

        layer = obj.data.uv_layers[UV_LAYER]
        for poly in obj.data.polygons:
            found = src.get(face_key(obj.data, poly))
            if not found:
                missing += 1
                continue
            uvs = found.pop(0) if len(found) > 1 else found[0]
            for li, uv in zip(poly.loop_indices, uvs):
                layer.data[li].uv = uv
    return missing


def _uv_range(layer):
    us = [layer.data[i].uv[0] for i in range(len(layer.data))]
    vs = [layer.data[i].uv[1] for i in range(len(layer.data))]
    return min(us), max(us), min(vs), max(vs)


def make_uv_bake_proxy(targets, resolution, margin_px):
    """Join targets, unwrap+pack Lightmap UVs, return the proxy (caller bakes it)."""
    t0 = time.time()
    gutter = 2 * margin_px
    margin_uv = gutter / float(resolution)

    for obj in targets:
        ensure_lightmap_layer(obj)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in targets:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = targets[0]
    bpy.ops.object.duplicate()
    copies = [o for o in bpy.context.selected_objects if o.type == "MESH"]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(copies) > 1:
        bpy.ops.object.select_all(action="DESELECT")
        for obj in copies:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = copies[0]
        bpy.ops.object.join()
        copies = [bpy.context.view_layer.objects.active]
    proxy = copies[0]
    proxy.name = "__lightmap_proxy"

    ensure_lightmap_layer(proxy)
    bpy.ops.object.select_all(action="DESELECT")
    proxy.select_set(True)
    bpy.context.view_layer.objects.active = proxy
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    # smart_project alone yields ~70% coverage on this joined mesh. pack_islands
    # and average_islands_scale both corrupt it (charts collapse or areas explode
    # to 1e28) - measured in Blender 5.2.2 on the office sector.
    bpy.ops.uv.smart_project(
        angle_limit=math.radians(66.0),
        island_margin=0.0,
        area_weight=0.0,
        correct_aspect=True,
        scale_to_bounds=False,
    )
    bpy.ops.object.mode_set(mode="OBJECT")

    layer = proxy.data.uv_layers[UV_LAYER]
    u0, u1, v0, v1 = _uv_range(layer)
    # Fit into 0-1 with a pixel gutter. Avoid pack_islands on this mesh.
    span = max(u1 - u0, v1 - v0, 1e-9)
    pad = margin_uv
    scale = (1.0 - 2.0 * pad) / span
    for i in range(len(layer.data)):
        u, v = layer.data[i].uv
        layer.data[i].uv = ((u - u0) * scale + pad, (v - v0) * scale + pad)
    u0, u1, v0, v1 = _uv_range(layer)
    print(f"  uv        smart_project fitted "
          f"U[{u0:.3f},{u1:.3f}] V[{v0:.3f},{v1:.3f}]")

    for uv_layer in proxy.data.uv_layers:
        uv_layer.active_render = (uv_layer.name == UV_LAYER)
        uv_layer.active = (uv_layer.name == UV_LAYER)

    # Coverage of the proxy alone (the atlas truth).
    lm = proxy.data.uv_layers[UV_LAYER]
    coverage = 0.0
    for poly in proxy.data.polygons:
        uvs = [lm.data[li].uv for li in poly.loop_indices]
        for k in range(1, len(uvs) - 1):
            (x0, y0), (x1, y1), (x2, y2) = uvs[0], uvs[k], uvs[k + 1]
            coverage += abs((x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)) * 0.5

    print(f"  uv        joined smart_project "
          f"(fraction margin {margin_uv:.6f} = {gutter} px @ {resolution}) "
          f"coverage {coverage * 100:.1f}% in {time.time() - t0:.1f} s")
    return proxy, coverage


def copy_lightmap_uvs_from_proxy(proxy, targets):
    """Write proxy Lightmap UVs back onto the originals for GLB export."""
    layer = proxy.data.uv_layers[UV_LAYER]
    packed = {}
    for poly in proxy.data.polygons:
        uvs = [layer.data[li].uv.copy() for li in poly.loop_indices]
        packed.setdefault(face_centroid_key(proxy, poly, world=False), []).append(uvs)

    missing = 0
    for obj in targets:
        if UV_LAYER in obj.data.uv_layers:
            obj.data.uv_layers.remove(obj.data.uv_layers[UV_LAYER])
        obj.data.uv_layers.new(name=UV_LAYER)
        dst = obj.data.uv_layers[UV_LAYER]
        for poly in obj.data.polygons:
            key = face_centroid_key(obj, poly, world=True)
            found = packed.get(key)
            if not found:
                missing += 1
                continue
            uvs = found.pop(0) if len(found) > 1 else found[0]
            if len(uvs) != len(poly.loop_indices):
                missing += 1
                continue
            for li, uv in zip(poly.loop_indices, uvs):
                dst.data[li].uv = uv
        for lyr in obj.data.uv_layers:
            lyr.active_render = (lyr.name == UV_LAYER)
    if missing:
        print(f"  uv        {missing} faces failed UV copy-back (export may miss them)")
    return missing


# ============================================================== materials / proxy
def bake_materials(targets):
    mats, seen = [], set()
    for obj in targets:
        for mat in obj.data.materials:
            if mat is not None and mat.name not in seen:
                seen.add(mat.name)
                mats.append(mat)
    return mats


def install_bake_targets(materials, image):
    installed = []
    for mat in materials:
        tree = getattr(mat, "node_tree", None)
        if tree is None:
            continue
        previous_active = tree.nodes.active
        tex = tree.nodes.new("ShaderNodeTexImage")
        tex.label = tex.name = "LightmapBake"
        tex.image = image
        uvm = tree.nodes.new("ShaderNodeUVMap")
        uvm.uv_map = UV_LAYER
        tree.links.new(uvm.outputs["UV"], tex.inputs["Vector"])
        tree.nodes.active = tex
        installed.append((tree, tex, uvm, previous_active))
    return installed


def remove_bake_targets(installed):
    for tree, tex, uvm, previous_active in installed:
        tree.nodes.remove(tex)
        tree.nodes.remove(uvm)
        try:
            tree.nodes.active = previous_active
        except (ReferenceError, TypeError):
            pass


def make_bake_proxy(targets):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in targets:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = targets[0]
    bpy.ops.object.duplicate()
    copies = [o for o in bpy.context.selected_objects if o.type == "MESH"]
    if len(copies) > 1:
        bpy.ops.object.select_all(action="DESELECT")
        for obj in copies:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = copies[0]
        bpy.ops.object.join()
        copies = [bpy.context.view_layer.objects.active]
    proxy = copies[0]
    proxy.name = "__lightmap_proxy"
    return proxy


# ============================================================== cycles
# Prefer CUDA before OPTIX: Maxwell (e.g. GTX 980) has CUDA but not OptiX.
_GPU_COMPUTE_TYPES = ("CUDA", "OPTIX", "HIP", "ONEAPI", "METAL")


def enable_cycles_device(scene, device):
    """Set Cycles to CPU or the first available GPU compute backend."""
    device = (device or "CPU").upper()
    if device not in ("CPU", "GPU"):
        raise RuntimeError(f"--device must be CPU or GPU, got {device!r}")

    bpy.ops.preferences.addon_enable(module="cycles")
    prefs = bpy.context.preferences.addons["cycles"].preferences

    if device == "CPU":
        scene.cycles.device = "CPU"
        return "CPU"

    for compute_type in _GPU_COMPUTE_TYPES:
        try:
            prefs.compute_device_type = compute_type
        except TypeError:
            continue
        prefs.get_devices()
        gpus = [d for d in prefs.devices if d.type == compute_type]
        if not gpus:
            continue
        for d in prefs.devices:
            d.use = d.type == compute_type
        scene.cycles.device = "GPU"
        names = ", ".join(d.name for d in gpus)
        print(f"  device    GPU ({compute_type}): {names}")
        return f"GPU/{compute_type}"

    raise RuntimeError(
        "No Cycles GPU compute devices found. Tried: "
        + ", ".join(_GPU_COMPUTE_TYPES)
    )


def configure_cycles(scene, samples, min_samples, bounces, adaptive, guiding,
                     device="CPU"):
    previous = {
        "engine": scene.render.engine,
        "device": scene.cycles.device,
        "samples": scene.cycles.samples,
        "adaptive": scene.cycles.use_adaptive_sampling,
        "adaptive_threshold": scene.cycles.adaptive_threshold,
        "adaptive_min": scene.cycles.adaptive_min_samples,
        "denoise": scene.cycles.use_denoising,
        "max_bounces": scene.cycles.max_bounces,
        "diffuse": scene.cycles.diffuse_bounces,
        "glossy": scene.cycles.glossy_bounces,
        "transmission": scene.cycles.transmission_bounces,
        "volume": scene.cycles.volume_bounces,
        "guiding": scene.cycles.use_guiding,
        "light_tree": scene.cycles.use_light_tree,
        "clamp_indirect": scene.cycles.sample_clamp_indirect,
        "guiding_training": scene.cycles.guiding_training_samples,
    }
    scene.render.engine = "CYCLES"
    device_label = enable_cycles_device(scene, device)
    scene.cycles.samples = samples
    if adaptive and adaptive > 0.0:
        scene.cycles.use_adaptive_sampling = True
        scene.cycles.adaptive_threshold = adaptive
        scene.cycles.adaptive_min_samples = min_samples
    else:
        scene.cycles.use_adaptive_sampling = False
    scene.cycles.use_denoising = False
    scene.cycles.max_bounces = max(1, bounces)
    scene.cycles.diffuse_bounces = bounces
    scene.cycles.glossy_bounces = 0
    scene.cycles.transmission_bounces = 0
    scene.cycles.volume_bounces = 0
    # Do not crush indirect energy in shadow regions.
    scene.cycles.sample_clamp_indirect = 0.0
    scene.cycles.use_light_tree = True
    scene.cycles.use_guiding = bool(guiding)
    if guiding:
        scene.cycles.use_surface_guiding = True
        scene.cycles.guiding_training_samples = min(256, max(64, min_samples * 2))
    previous["device_label"] = device_label
    return previous


def restore_cycles(scene, previous):
    scene.render.engine = previous["engine"]
    scene.cycles.device = previous["device"]
    scene.cycles.samples = previous["samples"]
    scene.cycles.use_adaptive_sampling = previous["adaptive"]
    scene.cycles.adaptive_threshold = previous["adaptive_threshold"]
    scene.cycles.adaptive_min_samples = previous["adaptive_min"]
    scene.cycles.use_denoising = previous["denoise"]
    scene.cycles.max_bounces = previous["max_bounces"]
    scene.cycles.diffuse_bounces = previous["diffuse"]
    scene.cycles.glossy_bounces = previous["glossy"]
    scene.cycles.transmission_bounces = previous["transmission"]
    scene.cycles.volume_bounces = previous["volume"]
    scene.cycles.use_guiding = previous["guiding"]
    scene.cycles.use_light_tree = previous["light_tree"]
    scene.cycles.sample_clamp_indirect = previous["clamp_indirect"]
    scene.cycles.guiding_training_samples = previous["guiding_training"]


def do_bake(proxy, margin):
    bpy.ops.object.select_all(action="DESELECT")
    proxy.select_set(True)
    bpy.context.view_layer.objects.active = proxy
    bpy.ops.object.bake(
        type="DIFFUSE",
        pass_filter={"DIRECT", "INDIRECT"},
        use_clear=True,
        margin=margin,
        margin_type="EXTEND",
        use_selected_to_active=False,
    )


# ============================================================== encode
def srgb_encode(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1.0 / 2.4) - 0.055)


def luminance(rgb):
    return rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def read_image(image):
    width, height = image.size
    buf = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(buf)
    return buf.reshape(height, width, 4)


def save_atlas(rgb, path, scale, auto_scale):
    peak = float(np.percentile(rgb, 99.9))
    if auto_scale and peak > 0:
        scale = 1.0 / peak
    encoded = srgb_encode(rgb * scale)
    pixels = np.rint(encoded * 255.0).astype(np.uint8)
    Image.fromarray(pixels[::-1], mode="RGB").save(path)
    return scale


# ============================================================== main
def main():
    started = time.time()
    scene = bpy.context.scene

    quality = opt("--quality", "preview")
    if quality not in QUALITY:
        raise RuntimeError(f"Unknown --quality {quality!r}; pick one of {list(QUALITY)}")
    preset = dict(QUALITY[quality])
    if opt("--resolution"):
        preset["resolution"] = int(opt("--resolution"))
    if opt("--samples"):
        preset["samples"] = int(opt("--samples"))
    if opt("--bounces"):
        preset["bounces"] = int(opt("--bounces"))
    if flag("--direct-only"):
        preset["bounces"] = 0

    device = (opt("--device", "CPU") or "CPU").upper()
    if device not in ("CPU", "GPU"):
        raise RuntimeError(f"--device must be CPU or GPU, got {device!r}")

    resolution = preset["resolution"]
    samples = preset["samples"]
    min_samples = preset["min_samples"]
    bounces = preset["bounces"]
    adaptive = preset["adaptive"]
    guiding = preset["guiding"]
    pack_margin = int(opt("--margin", str(BAKE_MARGIN_PX)))
    # Bake margin scales with atlas size relative to the pack resolution.
    margin = max(1, int(round(pack_margin * resolution / PACK_RESOLUTION)))
    if resolution >= PACK_RESOLUTION:
        margin = pack_margin

    out_path = opt("--out") or os.path.join(
        resolve_levels_dir(), LEVEL_ID, f"{SECTOR_ID}_lightmap.png")
    encode_scale = float(opt("--encode-scale", "1.0"))
    auto_scale = flag("--auto-scale") or opt("--encode-scale") is None

    print(f"BAKE_V2 quality={quality} atlas={resolution}x{resolution} "
          f"samples<={samples} min={min_samples} adaptive={adaptive} "
          f"bounces={bounces} guiding={guiding} margin={margin}px "
          f"device={device}")

    targets, skipped = targets_to_bake(opt("--only"))
    if not targets:
        raise RuntimeError("No mesh objects to bake.")
    materials = bake_materials(targets)
    print(f"  targets   {len(targets)} meshes, {len(materials)} materials")
    dropped = ", ".join(f"{n} {k}" for k, n in skipped.items() if n)
    if dropped:
        print(f"  skipped   {dropped}")

    states = {obj.name: uv_state(obj) for obj in targets}
    reuse = all(UV_LAYER in obj.data.uv_layers for obj in targets) and not flag("--repack")

    for stale in [i for i in bpy.data.images if i.name.startswith(ATLAS_IMAGE)]:
        bpy.data.images.remove(stale)
    atlas = bpy.data.images.new(ATLAS_IMAGE, resolution, resolution,
                                alpha=False, float_buffer=True)

    installed = install_bake_targets(materials, atlas)
    previous = configure_cycles(
        scene, samples, min_samples, bounces, adaptive, guiding, device=device)


    # One joined proxy is unwrapped, packed, baked, then UVs are copied back.
    # Packing against PACK_RESOLUTION so preview and high share a layout.
    t0 = time.time()
    if reuse:
        # Still need a bake proxy with those UVs.
        proxy = make_bake_proxy(targets)
        for layer in proxy.data.uv_layers:
            layer.active_render = (layer.name == UV_LAYER)
            layer.active = (layer.name == UV_LAYER)
        coverage = sum(uv_area(obj) for obj in targets)
        print(f"  uv        reusing existing {UV_LAYER} on bake proxy "
              f"(--repack to rebuild)")
        join_time = time.time() - t0
    else:
        proxy, coverage = make_uv_bake_proxy(targets, PACK_RESOLUTION, pack_margin)
        join_time = time.time() - t0

    area_m2 = sum(surface_area(obj) for obj in targets)
    texels = coverage * resolution * resolution
    density = area_m2 / texels if texels else 0.0
    if coverage > 1.001:
        raise RuntimeError(f"Lightmap UVs overlap: islands sum to {coverage:.3f}")
    print(f"  coverage  islands cover {coverage * 100:.1f}% of the atlas")
    print(f"  density   {area_m2:.0f} m2 over {texels:,.0f} texels -> "
          f"{np.sqrt(density) * 100:.1f} cm/texel")

    for obj in targets:
        obj.hide_render = True
    bpy.context.view_layer.update()
    try:
        t0 = time.time()
        do_bake(proxy, margin)
        bake_time = time.time() - t0
        if not reuse:
            copy_lightmap_uvs_from_proxy(proxy, targets)
    finally:
        restore_cycles(scene, previous)
        remove_bake_targets(installed)
        for obj in targets:
            obj.hide_render = False
        bpy.data.objects.remove(proxy, do_unlink=True)
        for obj in targets:
            restore_uv_state(obj, states[obj.name])
            # Keep Lightmap layer present for export even after restore of active/render.
            if UV_LAYER in obj.data.uv_layers:
                pass

    data = read_image(atlas)
    rgb = data[:, :, :3]
    lum = luminance(rgb)
    used = lum > 0.0
    print(f"  bake      {bake_time:.1f} s sampling, {join_time:.1f} s join, "
          f"{time.time() - started:.1f} s total")
    print(f"  range     mean {lum.mean():.4f}  p50 {np.percentile(lum, 50):.4f}  "
          f"p95 {np.percentile(lum, 95):.4f}  max {lum.max():.3f}")
    print(f"  used      {used.mean() * 100:.1f}% of the atlas is non-black")

    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
    scale = save_atlas(rgb, out_path, encode_scale, auto_scale)
    atlas.filepath_raw = out_path
    atlas.file_format = "PNG"
    atlas.use_fake_user = True

    meta_path = os.path.splitext(out_path)[0] + ".json"
    with open(meta_path, "w") as fh:
        json.dump({
            "image": os.path.basename(out_path),
            "resolution": resolution,
            "quality": quality,
            "pipeline": "bake_lightmaps_v2",
            "samples": samples,
            "adaptive_min_samples": min_samples,
            "adaptive_threshold": adaptive,
            "path_guiding": guiding,
            "diffuse_bounces": bounces,
            "uv_layer": UV_LAYER,
            "uv_pack": "joined smart_project (FRACTION margin, scale_to_bounds)",
            "coverage": round(coverage, 5),
            "encode": "srgb",
            "encode_scale": scale,
            "value": "irradiance/pi, linear, albedo excluded",
            "bake_margin_px": margin,
            "bake_margin_type": "EXTEND",
            "pack_resolution": PACK_RESOLUTION,
            "pack_margin_px": pack_margin,
            "pack_gutter_px": 2 * pack_margin,
        }, fh, indent=2)
    print(f"  atlas     {out_path} ({os.path.getsize(out_path) / 1024:.0f} KB, "
          f"encode scale {scale:.4g})")
    print(f"  meta      {meta_path}")

    raw_path = os.path.splitext(out_path)[0] + "_raw.npz"
    np.savez_compressed(raw_path, rgb=rgb.astype(np.float32), encode_scale=np.float32(scale))
    print(f"  raw       {raw_path}")

    if not flag("--no-denoise"):
        denoiser = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "denoise_lightmap.py")
        subprocess.run(["python3", denoiser, out_path, out_path], check=True)
        print(f"  denoise   applied via {denoiser}")

    if flag("--no-save"):
        print("BAKE_V2_OK (not saved)")
        return
    bpy.ops.wm.save_mainfile()
    print(f"BAKE_V2_OK saved {bpy.data.filepath}")


if __name__ == "__main__":
    main()
