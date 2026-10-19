"""Bake the office level's static lighting into one lightmap atlas, priced by --quality.

The atlas is what the game can sample instead of running the level's lighting every
frame. What goes in it is fixed by three measurements, not by taste:

  * ``bake(type="DIFFUSE", pass_filter={"DIRECT","INDIRECT"})`` with the COLOR pass
    off stores **incoming irradiance / pi**, per RGB channel, with the surface's own
    albedo excluded (a 0.5 grey plane bakes exactly the same number as a white one)
    and the *light's* colour kept (a red light bakes red). So the shader multiplies
    the atlas by its own albedo map and does not divide by pi again.
  * Cycles denoising does nothing to a bake - with the same seed, denoise on and
    denoise off write bit-identical texels. Quality is therefore only ever
    samples x texels, which is why the presets are a resolution and a sample count.
  * Cost is **not** proportional to texels the way a naive estimate says. Cycles
    spends ~150 ms per object getting ready to bake it, whatever the resolution and
    sample count, so baking the level's 353 objects costs 53 s before a sample is
    taken; the same geometry joined into one object costs about a second. This tool
    therefore bakes a temporary joined copy. That is not only 55x faster, it is also
    the *only correct* way: the bake margin is dilated once per object, so 353
    objects x 4 px floods the atlas from 20% coverage to 80% and doubles the
    brightness. The copy shares mesh data, materials and UV layers, so the atlas is
    the one the originals would have baked.

The atlas is stored in a second UV layer named ``Lightmap`` (glTF TEXCOORD_1, which
raylib loads as ``mesh.texcoords2``). It is deliberately **not** wired into any
material: raylib loads each material's textures into their own GPU image, so an
atlas shared by twenty materials would be uploaded twenty times. The game binds it
to a texture unit by hand, the way BasicLighting binds the shadow cubes.

Two traps worth restating, both of which silently corrupt something else:

  * The level's stored Cycles settings are 4096 samples, adaptive, 12 bounces with
    denoising - a bake inherits them, which is a three-hour bake. They are set
    explicitly here and restored before saving.
  * The unwrap writes into the *active* UV layer, and the level's materials sample
    their base-colour textures through the active UV layer too. The active layer is
    put back afterwards, or the stills would render through lightmap UVs.
  * A bake reads the *render* UV layer, which is not the same thing as the active
    one: the packer leaves ``UVMap`` flagged, so the bake would otherwise run through
    the tiling UVs and smear repeated geometry across the atlas. The render flag is
    moved to ``Lightmap`` for the bake and put back afterwards.

Two more that were found the hard way and are worth not repeating:

  * A bake margin is dilated **once per object baked**, so 353 objects x 4 px floods
    the atlas from 20% to 80% coverage and doubles every value in it. One joined
    proxy dilates once, which is the other reason to bake joined.
  * The joined proxy is a duplicate sitting on top of the originals, and every shadow
    ray then hits the original at distance zero: the level bakes itself black. The
    originals are hidden for the bake and restored before saving.

Usage::

    blender -b ~/Work/office-level.blend --python tools/bake_lightmaps.py -- \\
        --quality worst

    # Full pipeline. Note that everything after ``--`` belongs to the bake script, so
    # the export has to be its own invocation - Blender would silently ignore a
    # ``--python`` placed after it.
    blender -b --factory-startup \\
        --python tools/build_office_level.py \\
        --python tools/apply_render_setup.py
    blender -b ~/Work/office-level.blend --python tools/bake_lightmaps.py -- --quality low
    blender -b ~/Work/office-level.blend --python tools/export_level_glb.py

Options (after ``--``): --quality worst|low|medium|high, --resolution PX,
--samples N, --bounces N, --direct-only, --only SUBSTR, --out PATH,
--encode-scale S, --repack, --margin PX, --panel-emission-scale S, --no-save,
--no-denoise.

The atlas is denoised with OIDN (tools/denoise_lightmap.py) after saving; without it the
per-texel bake noise is magnified into visible grain on walls and ceilings, because those
surfaces are lit indirectly and the atlas is stretched over square metres.
"""

import json
import math
import os
import sys
import time

import bmesh
import bpy
import numpy as np
from PIL import Image

# ---- config (override before exec, or edit here) ----
LEVEL_ID = "office"
SECTOR_ID = "office"
LEVELS_DIR = None  # absolute override for content/levels

# The UV layer the atlas lives in, and so the texture coordinate set the engine
# reads (TEXCOORD_1 -> raylib mesh.texcoords2).
UV_LAYER = "Lightmap"

# Baked into the sector GLB by export_level_glb.py, and note that Ground still
# occludes light here - it just gets no texels of its own.
EXCLUDE_NAMES = {"Ground"}

ATLAS_IMAGE = "office_lightmap"

# Measured on this machine, Cycles CPU, baking the level as ONE joined object:
# 512^2 x 8 spp in 0.85 s, 1024^2 x 16 in 4.5 s, 2048^2 x 32 in 46 s - about
# 0.35 us per texel per sample plus ~8 s of fixed cost (open blend, pack, join,
# save). Quoted for a joined bake, because baking the same atlas one object at a
# time costs ~150 ms *per object* (53 s for this level) whatever the resolution
# and sample count.
MICROS_PER_TEXEL_SAMPLE = 0.35

# The bake's own dilation, in atlas pixels. The packer already leaves a gap
# between islands; this grows each island's edge colour outwards so bilinear
# sampling at a UV seam cannot pick up the cleared background.
BAKE_MARGIN_PX = 4

# Interactive tiers, in atlas pixels and samples. `worst` is the cheapest bake that
# still says something true - 512^2 at 8 samples is about a second of sampling, so
# a whole run is dominated by Blender's fixed costs and works as a smoke test of the
# UVs, the materials and the export. The floor, if even that is too slow, is
# --resolution 256 --samples 4 --direct-only.
QUALITY = {
    "worst":  {"resolution": 512,  "samples": 8,  "bounces": 1},
    "low":    {"resolution": 1024, "samples": 16, "bounces": 1},
    "medium": {"resolution": 2048, "samples": 32, "bounces": 2},
    "high":   {"resolution": 4096, "samples": 64, "bounces": 2},
}

# One bounce vs two moves the atlas by ~6% in brightness (measured), which is not
# worth its cost, so only `medium` and above pay for the second.
DEFAULT_QUALITY = "worst"

# Materials whose emission stands in for the ceiling panels. The level lights each
# room twice - an emissive panel box and, since apply_render_setup.py, an area
# light the size of that box - which is how the stills look. --panel-emission-scale
# 0 bakes the rooms from the area lights alone: fewer, less noisy light sources.
EMISSIVE_PANEL_PREFIX = "Ceiling_Light"


# ============================================================== argument handling
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
    blend_path = bpy.data.filepath
    if blend_path:
        levels = os.path.dirname(os.path.dirname(os.path.abspath(blend_path)))
        if os.path.isdir(levels):
            return levels
    raise RuntimeError("Cannot resolve content/levels. Set LEVELS_DIR to an absolute path.")


def hhmmss(seconds):
    if seconds < 60:
        return f"{seconds:.1f} s"
    if seconds < 3600:
        return f"{int(seconds // 60)}m {seconds % 60:04.1f}s"
    return f"{int(seconds // 3600)}h {int(seconds // 60) % 60:02d}m"


# ============================================================== gathering
def targets_to_bake(only):
    """Every mesh the sector export would carry, minus anything filtered out."""
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


def uv_area(obj):
    """Sum of |UV triangle areas| of the Lightmap layer, as a fraction of the atlas.

    Overlapping islands would still sum above 1.0, so this doubles as the check
    that the packer actually laid every face out somewhere unique.
    """
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
    """World-space area in m^2, for reporting texel density."""
    me = obj.data
    total = 0.0
    scale = obj.matrix_world.to_scale()
    for poly in me.polygons:
        verts = [me.vertices[i].co for i in poly.vertices]
        for k in range(1, len(verts) - 1):
            a, b, c = verts[0], verts[k], verts[k + 1]
            n = (b - a).cross(c - a)
            total += n.length * 0.5
    return total * abs(scale.x * scale.y)  # meshes are scaled uniformly here


# ============================================================== UV atlas
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
    """Position of a face's corners, independent of which vertex index it uses.

    Welding a copy changes indices and can rotate a face's loop, so the key is
    canonicalised to start at the smallest corner.
    """
    pts = [(round(v.x, 4), round(v.y, 4), round(v.z, 4))
           for v in (me.vertices[i].co for i in poly.vertices)]
    k = min(range(len(pts)), key=lambda i: pts[i])
    return tuple(pts[k:] + pts[:k])


def pack_lightmap_uvs(targets):
    """Give every target a Lightmap layer of continuous charts, packed into 0-1.

    ``lightmap_pack`` gives every face its own island. That is fatal here: the
    level's meshes are split at most edges (the chair has 10.5k verts for 5.7k
    tris), so each triangle gets ~3 texels, bakes a single value, and jumps
    against its neighbours - a per-face mosaic that reads as crumpled foil on the
    chairs and as triangles across the racks. More resolution does not fix it
    (still tiled at 0.5 cm/texel); continuity does.

    The originals cannot simply be welded: welding recomputes their custom split
    normals (32% of the desk's loop normals moved by up to 60 degrees), which
    would change the stills. So the weld happens on a throwaway copy, the copy is
    projected with ``smart_project``, and only the resulting UVs are copied back
    by face position.

    ``pack_islands`` does not scale islands in this Blender, so the charts are
    shelf packed here from their bounding boxes, area-normalised first, with a
    proportional gap like lightmap_pack's margin. The layout does not depend on
    the atlas resolution: it is packed once, and ``--repack`` only when it has to
    change.
    """
    t0 = time.time()
    for obj in targets:
        me = obj.data
        if UV_LAYER in me.uv_layers:
            me.uv_layers.remove(me.uv_layers[UV_LAYER])
        me.uv_layers.new(name=UV_LAYER)
        me.uv_layers.active = me.uv_layers[UV_LAYER]

    # --- charts from welded copies -------------------------------------------
    coll = bpy.context.scene.collection
    missing = 0
    for obj in targets:
        dup = obj.copy()
        dup.data = obj.data.copy()
        coll.objects.link(dup)
        bm = bmesh.new()
        bm.from_mesh(dup.data)
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-4)
        bm.to_mesh(dup.data)
        bm.free()

        me = dup.data
        if UV_LAYER in me.uv_layers:
            me.uv_layers.remove(me.uv_layers[UV_LAYER])
        me.uv_layers.new(name=UV_LAYER)
        me.uv_layers.active = me.uv_layers[UV_LAYER]

        bpy.ops.object.select_all(action="DESELECT")
        dup.select_set(True)
        bpy.context.view_layer.objects.active = dup
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.0,
                                 area_weight=0.0, correct_aspect=True,
                                 scale_to_bounds=False)
        bpy.ops.object.mode_set(mode="OBJECT")

        src = {}
        for poly in me.polygons:
            uvs = [me.uv_layers[UV_LAYER].data[li].uv.copy()
                   for li in poly.loop_indices]
            src.setdefault(face_key(me, poly), []).append(uvs)
        bpy.data.objects.remove(dup, do_unlink=True)

        me = obj.data
        layer = me.uv_layers[UV_LAYER]
        for poly in me.polygons:
            found = src.get(face_key(me, poly))
            if not found:
                missing += 1  # duplicate/degenerate face, no area to bake
                continue
            uvs = found.pop(0) if len(found) > 1 else found[0]
            for li, uv in zip(poly.loop_indices, uvs):
                layer.data[li].uv = uv

    # --- charts: faces sharing a corner with the same UV ----------------------
    charts = []
    for obj in targets:
        me = obj.data
        layer = me.uv_layers[UV_LAYER]
        parent = list(range(len(me.polygons)))

        def find(x):
            while parent[x] != x:
                parent[x] = parent[parent[x]]
                x = parent[x]
            return x

        buckets = {}
        for poly in me.polygons:
            for li in poly.loop_indices:
                v = me.vertices[me.loops[li].vertex_index].co
                uv = layer.data[li].uv
                key = (round(v.x, 4), round(v.y, 4), round(v.z, 4),
                       round(uv[0], 5), round(uv[1], 5))
                buckets.setdefault(key, []).append(poly.index)
        for ids in buckets.values():
            r = find(ids[0])
            for i in ids[1:]:
                ri = find(i)
                if ri != r:
                    parent[ri] = r

        groups = {}
        for poly in me.polygons:
            groups.setdefault(find(poly.index), []).extend(poly.loop_indices)
        for lis in groups.values():
            uv = np.array([layer.data[li].uv[:] for li in lis], dtype=np.float64)
            charts.append([uv[:, 0].min(), uv[:, 1].min(),
                           uv[:, 0].max(), uv[:, 1].max(), obj.name, lis, uv])

    # --- shelf pack, area normalised -----------------------------------------
    # The gap is proportional, like lightmap_pack's margin: 23k charts in a
    # 4096 atlas average ~20 px, so a uniform 8 px gap would be a quarter of
    # every chart and at 512 it would not fit at all.
    area = sum((c[2] - c[0]) * (c[3] - c[1]) for c in charts)
    k = (0.60 / area) ** 0.5  # aim at 60% chart area, then fit the shelves
    order = sorted(range(len(charts)), key=lambda i: -(charts[i][3] - charts[i][1]))
    x = y = high = wide = 0.0
    place = {}
    for i in order:
        u0, v0, u1, v1 = charts[i][:4]
        w, h = (u1 - u0) * k, (v1 - v0) * k
        g = 0.03 * max(w, h)
        if x + w + 2 * g > 1.0 and x > 0.0:
            y += high
            x, high = 0.0, 0.0
        place[i] = (x + g - u0 * k, y + g - v0 * k)
        x += w + 2 * g
        wide = max(wide, x)
        high = max(high, h + 2 * g)
    fit = min(1.0 / max(wide, 1e-9), 1.0 / max(y + high, 1e-9))
    scale = k * fit
    used = area * scale * scale
    print(f"  uv        unwrapped {len(targets)} meshes into {len(charts):,} charts, "
          f"{used*100:.0f}% of the atlas in {time.time()-t0:.1f} s")
    if missing:
        print(f"  uv        {missing} zero-area faces had no source face")

    # --- write back -----------------------------------------------------------
    by_obj = {}
    for i, c in enumerate(charts):
        by_obj.setdefault(c[4], []).append(i)
    for obj in targets:
        me = obj.data
        layer = me.uv_layers[UV_LAYER]
        for i in by_obj.get(obj.name, []):
            u0, v0, u1, v1, _, lis, uv = charts[i]
            ox, oy = place[i]
            for li, u, v in zip(lis, (uv[:, 0] * k + ox) * fit,
                                (uv[:, 1] * k + oy) * fit):
                layer.data[li].uv = (u, v)

    # A bake reads the render map, not the active one, and the unwrap leaves the
    # tiling layer flagged - so baking here would smear repeated geometry across
    # the atlas. restore_uv_state() puts the flag back after the bake.
    for obj in targets:
        layers = obj.data.uv_layers
        for layer in layers:
            layer.active_render = (layer.name == UV_LAYER)


# ============================================================== materials
def bake_materials(targets):
    mats = []
    seen = set()
    for obj in targets:
        for mat in obj.data.materials:
            if mat is not None and mat.name not in seen:
                seen.add(mat.name)
                mats.append(mat)
    return mats


def install_bake_targets(materials, image):
    """Point every material's bake at the shared atlas, and remember the mess."""
    installed = []
    for mat in materials:
        tree = getattr(mat, "node_tree", None)
        if tree is None:
            continue
        previous_active = tree.nodes.active
        tex = tree.nodes.new("ShaderNodeTexImage")
        tex.label = tex.name = "LightmapBake"
        tex.image = image
        # Bake through the second UV layer explicitly rather than relying on
        # whichever layer happens to be active.
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
    """One joined copy of the targets, baked in place of 353 separate objects.

    Cycles spends ~150 ms per object getting ready to bake it - independent of
    resolution and samples - so 353 objects cost 53 s before a single sample is
    taken, while the same geometry joined costs about a second. The copy shares
    the originals' mesh data, materials and UV layers, so the atlas comes out
    identical; it is deleted before the file is saved.

    **The originals must be hidden while it is baked.** The copy sits exactly on
    top of them, so every shadow ray leaves a texel and hits the original surface
    at distance zero: the whole level bakes itself black (2.5% of the atlas lit,
    against 45% with the originals hidden). A normal bake is not affected by
    occlusion, which is why the atlas looked plausible while the light was gone.
    """
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


def scale_panel_emission(scale):
    """Dim the emissive ceiling panels for the bake; returns what to put back."""
    if scale == 1.0:
        return []
    restore = []
    for mat in bpy.data.materials:
        if not mat.name.startswith(EMISSIVE_PANEL_PREFIX):
            continue
        tree = getattr(mat, "node_tree", None)
        if tree is None:
            continue
        for node in tree.nodes:
            if node.type != "BSDF_PRINCIPLED":
                continue
            strength = next((s for s in node.inputs if s.name == "Emission Strength"), None)
            if strength is not None and not strength.is_linked:
                restore.append((strength, strength.default_value))
                strength.default_value *= scale
    return restore


# ============================================================== baking
def configure_cycles(scene, samples, bounces):
    """Set up a bake, remembering the level's own (much more expensive) settings."""
    previous = {
        "engine": scene.render.engine,
        "samples": scene.cycles.samples,
        "adaptive": scene.cycles.use_adaptive_sampling,
        "denoise": scene.cycles.use_denoising,
        "max_bounces": scene.cycles.max_bounces,
        "diffuse": scene.cycles.diffuse_bounces,
        "glossy": scene.cycles.glossy_bounces,
        "transmission": scene.cycles.transmission_bounces,
        "volume": scene.cycles.volume_bounces,
    }
    bpy.ops.preferences.addon_enable(module="cycles")
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    # Adaptive sampling would make the sample count an upper bound and the cost
    # unpredictable; a bake wants the number it was told.
    scene.cycles.use_adaptive_sampling = False
    scene.cycles.use_denoising = False
    scene.cycles.max_bounces = max(1, bounces)
    scene.cycles.diffuse_bounces = bounces
    scene.cycles.glossy_bounces = 0
    scene.cycles.transmission_bounces = 0
    scene.cycles.volume_bounces = 0
    return previous


def restore_cycles(scene, previous):
    scene.render.engine = previous["engine"]
    scene.cycles.samples = previous["samples"]
    scene.cycles.use_adaptive_sampling = previous["adaptive"]
    scene.cycles.use_denoising = previous["denoise"]
    scene.cycles.max_bounces = previous["max_bounces"]
    scene.cycles.diffuse_bounces = previous["diffuse"]
    scene.cycles.glossy_bounces = previous["glossy"]
    scene.cycles.transmission_bounces = previous["transmission"]
    scene.cycles.volume_bounces = previous["volume"]


def do_bake(proxy, margin):
    bpy.ops.object.select_all(action="DESELECT")
    proxy.select_set(True)
    bpy.context.view_layer.objects.active = proxy
    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"DIRECT", "INDIRECT"},
                        use_clear=True, margin=margin, use_selected_to_active=False)


def read_image(image):
    width, height = image.size
    buf = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(buf)
    return buf.reshape(height, width, 4)


# ============================================================== encoding
def srgb_encode(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1.0 / 2.4) - 0.055)


def luminance(rgb):
    return rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)


def save_atlas(rgb, path, scale, auto_scale):
    """Write the linear bake out as an sRGB-encoded 8-bit PNG.

    sRGB rather than a straight linear scale because an office's irradiance sits
    low in the range, where linear 8-bit bands visibly; sRGB spends the bits where
    the values actually are. Row order flips: Blender's first row is the bottom.

    Auto-scaling normalises the 99.9th percentile to 1.0, so a lit ceiling panel or
    a lamp seen directly clips but the surfaces that carry the look do not. The
    scale is written into the filename's sibling report and must be divided back
    out by whatever reads the atlas.
    """
    peak = float(np.percentile(rgb, 99.9))
    if auto_scale and peak > 0:
        scale = 1.0 / peak
    encoded = srgb_encode(rgb * scale)
    pixels = np.rint(encoded * 255.0).astype(np.uint8)
    Image.fromarray(pixels[::-1], mode="RGB").save(path)
    return scale


def main():
    started = time.time()
    scene = bpy.context.scene

    quality = opt("--quality", "worst")
    if quality not in QUALITY:
        raise RuntimeError(f"Unknown --quality {quality!r}; pick one of {list(QUALITY)}")
    preset = dict(QUALITY[quality])
    if flag("--direct-only"):
        preset["bounces"] = 0
    if opt("--resolution"):
        preset["resolution"] = int(opt("--resolution"))
    if opt("--samples"):
        preset["samples"] = int(opt("--samples"))
    if opt("--bounces"):
        preset["bounces"] = int(opt("--bounces"))
    resolution = preset["resolution"]
    samples = preset["samples"]
    bounces = preset["bounces"]
    margin = int(opt("--margin", str(BAKE_MARGIN_PX)))
    out_path = opt("--out") or os.path.join(
        resolve_levels_dir(), LEVEL_ID, f"{SECTOR_ID}_lightmap.png")
    encode_scale = float(opt("--encode-scale", "1.0"))
    # Default to auto: the level's irradiance spans ~29x (p50 0.13 to max 29), so a
    # fixed 1.0 scale clips a quarter of the atlas. --encode-scale pins it instead.
    auto_scale = flag("--auto-scale") or opt("--encode-scale") is None
    panel_scale = float(opt("--panel-emission-scale", "1.0"))

    print(f"BAKE quality={quality} atlas={resolution}x{resolution} "
          f"samples={samples} bounces={bounces} margin={margin}px")

    targets, skipped = targets_to_bake(opt("--only"))
    if not targets:
        raise RuntimeError("No mesh objects to bake.")
    materials = bake_materials(targets)
    print(f"  targets   {len(targets)} meshes, {len(materials)} materials"
          + (f"  (only names containing {opt('--only')!r})" if opt("--only") else ""))
    dropped = ", ".join(f"{n} {k}" for k, n in skipped.items() if n)
    if dropped:
        print(f"  skipped   {dropped}")

    # --- UVs -----------------------------------------------------------------
    # Repacking is most of a `worst` run (the weld + smart_project pass is ~20 s)
    # and it invalidates every atlas baked against the previous layout, so it only
    # happens when the layout has to change: on a fresh pack, or after editing the
    # models. Otherwise the existing Lightmap layer is reused, which also makes a
    # re-bake comparable with the one before it.
    t0 = time.time()
    states = {obj.name: uv_state(obj) for obj in targets}
    packed = all(UV_LAYER in obj.data.uv_layers for obj in targets)
    if packed and flag("--repack"):
        packed = False
    if packed:
        pack_time = 0.0
        for obj in targets:
            obj.data.uv_layers.active = obj.data.uv_layers[UV_LAYER]
            for layer in obj.data.uv_layers:
                layer.active_render = (layer.name == UV_LAYER)
        print(f"  uv        reusing the existing {UV_LAYER} layer (--repack to rebuild)")
    else:
        pack_lightmap_uvs(targets)
        pack_time = time.time() - t0
    coverage = sum(uv_area(obj) for obj in targets)
    area_m2 = sum(surface_area(obj) for obj in targets)
    texels = coverage * resolution * resolution
    density = area_m2 / texels if texels else 0.0
    if coverage > 1.001:
        raise RuntimeError(f"Lightmap UVs overlap: islands sum to {coverage:.3f} of "
                           "the atlas. Do not bake - fix the packing first.")
    print(f"  coverage  islands cover {coverage * 100:.1f}% of the atlas")
    print(f"  density   {area_m2:.0f} m2 of surface over {texels:,.0f} texels -> "
          f"{density:.3f} m2/texel ({np.sqrt(density) * 100:.1f} cm across)")

    # --- what the other tiers would cost -------------------------------------
    # Each tier brings its own resolution, so the texel count is per tier; using
    # this run's texels for all of them reports medium at 512^2 (2 s) when it is
    # really 2048^2 x 32 (35 s).
    print("  cost      " + "  ".join(
        f"{tier}:{hhmmss(coverage * p['resolution'] ** 2 * p['samples']
                         * MICROS_PER_TEXEL_SAMPLE / 1e6)}"
        for tier, p in QUALITY.items()) + "  (sampling only, + ~8 s fixed)")
    estimate = texels * samples * MICROS_PER_TEXEL_SAMPLE / 1e6
    print(f"  estimate  {texels:,.0f} texels x {samples} spp x "
          f"{MICROS_PER_TEXEL_SAMPLE} us = {hhmmss(estimate)} for this tier")

    # --- atlas ---------------------------------------------------------------
    for stale in [i for i in bpy.data.images if i.name.startswith(ATLAS_IMAGE)]:
        bpy.data.images.remove(stale)
    atlas = bpy.data.images.new(ATLAS_IMAGE, resolution, resolution,
                                alpha=False, float_buffer=True)

    installed = install_bake_targets(materials, atlas)
    emission_restore = scale_panel_emission(panel_scale)
    if panel_scale != 1.0:
        print(f"  panels    emission x{panel_scale} for the bake")
    previous_cycles = configure_cycles(scene, samples, bounces)

    t0 = time.time()
    proxy = make_bake_proxy(targets)
    join_time = time.time() - t0
    # Hide the originals: the proxy is their duplicate in the same place, and
    # otherwise every shadow ray stops at zero distance on the original surface.
    # hide_render is restored in the finally block, before the file is saved.
    for obj in targets:
        obj.hide_render = True
    try:
        t0 = time.time()
        do_bake(proxy, margin)
        bake_time = time.time() - t0
    finally:
        restore_cycles(scene, previous_cycles)
        remove_bake_targets(installed)
        for socket, value in emission_restore:
            socket.default_value = value
        for obj in targets:
            obj.hide_render = False
        bpy.data.objects.remove(proxy, do_unlink=True)
        for obj in targets:
            restore_uv_state(obj, states[obj.name])

    # --- report and encode ---------------------------------------------------
    data = read_image(atlas)
    rgb = data[:, :, :3]
    lum = luminance(rgb)
    used = lum > 0.0
    clipped = float(np.mean(np.any(rgb > 1.0, axis=2))) * 100.0
    print(f"  bake      {bake_time:.1f} s sampling, {join_time:.1f} s join, "
          f"{time.time() - started:.1f} s total")
    print(f"  range     mean {lum.mean():.4f}  p50 {np.percentile(lum, 50):.4f}  "
          f"p95 {np.percentile(lum, 95):.4f}  max {lum.max():.3f}  "
          f"(irradiance/pi, linear)")
    print(f"  used      {used.mean() * 100:.1f}% of the atlas is non-black")
    if clipped:
        print(f"  clipping  {clipped:.2f}% of texels are above 1.0 at "
              f"--encode-scale 1.0")

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    scale = save_atlas(rgb, out_path, encode_scale, auto_scale)
    atlas.filepath_raw = out_path
    atlas.file_format = "PNG"
    atlas.use_fake_user = True  # keep the bake viewable in the saved .blend
    # The atlas is stored sRGB-encoded and divided by this scale by whatever reads
    # it, so the number has to survive with the image. A sidecar JSON is what the
    # engine can read without a Blender dependency.
    meta_path = os.path.splitext(out_path)[0] + ".json"
    with open(meta_path, "w") as fh:
        json.dump({
            "image": os.path.basename(out_path),
            "resolution": resolution,
            "quality": quality,
            "samples": samples,
            "diffuse_bounces": bounces,
            "uv_layer": UV_LAYER,
            "coverage": round(coverage, 5),
            "encode": "srgb",
            "encode_scale": scale,
            "value": "irradiance/pi, linear, albedo excluded",
            "bake_margin_px": margin,
        }, fh, indent=2)
    print(f"  atlas     {out_path} ({os.path.getsize(out_path) / 1024:.0f} KB, "
          f"sRGB-encoded, encode scale {scale:.4g}"
          + (", auto" if auto_scale else "") + ")")
    print(f"  meta      {meta_path}")

    # OIDN pass. The bake is only 32-64 spp and the noise is uncorrelated per texel;
    # magnified over a wall it reads as mottling. Denoising preserves the mean (checked
    # to 0.1%) and leaves the layout untouched, so nothing else needs re-exporting.
    if not flag("--no-denoise"):
        denoiser = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "denoise_lightmap.py")
        try:
            import subprocess
            subprocess.run(["python3", denoiser, out_path, out_path], check=True)
        except Exception as exc:  # noqa: BLE001 - denoise is optional
            print(f"  denoise   skipped ({exc}); the raw atlas is still usable")

    if flag("--no-save"):
        print("BAKE_OK (not saved)")
        return
    bpy.ops.wm.save_mainfile()
    print(f"BAKE_OK saved {bpy.data.filepath}")


if __name__ == "__main__":
    main()
