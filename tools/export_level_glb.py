"""Export the current Blender scene as one ColdAudit sector GLB.

ColdAudit sectors are *world-authored*: the mesh keeps its absolute Blender
coordinates and the glTF Y-up conversion makes it land upright in the engine.
Unlike ``export_meshes_glb.py`` (which writes one GLB + sidecar JSON per prop),
this joins every visible mesh into a single object so the sector is one node
with one primitive per material, instead of one node per box.

Usage
-----
    blender content/levels/<level>/<level>.blend \
        --background --python tools/export_level_glb.py

Writes ``content/levels/<LEVEL_ID>/<SECTOR_ID>.glb``.
"""

import bpy
import os

# ---- config (override before exec, or edit here) ----
LEVEL_ID = "office"
SECTOR_ID = "office"
# Exact object names to leave out (scene helpers, not part of the level shell).
EXCLUDE_NAMES = {"Ground"}
# Name prefixes to leave out.
EXCLUDE_PREFIXES = ()
LEVELS_DIR = None  # absolute override for content/levels


def resolve_levels_dir():
    if LEVELS_DIR:
        return LEVELS_DIR

    try:
        script_path = os.path.abspath(__file__)
        script_dir = os.path.dirname(script_path)
        repo_root = os.path.dirname(script_dir)
        candidate = os.path.join(repo_root, "content", "levels")
        if os.path.isdir(candidate):
            return candidate
    except NameError:
        pass

    blend_path = bpy.data.filepath
    if blend_path:
        # <repo>/content/levels/<level>/<level>.blend
        levels = os.path.dirname(os.path.dirname(os.path.abspath(blend_path)))
        if os.path.isdir(levels):
            return levels

    raise RuntimeError("Cannot resolve content/levels. Set LEVELS_DIR to an absolute path.")


def gather_sources():
    scene = bpy.context.scene
    sources = []
    for obj in scene.objects:
        if obj.type != "MESH":
            continue
        if obj.hide_get():
            continue
        if obj.name in EXCLUDE_NAMES:
            continue
        if any(obj.name.startswith(prefix) for prefix in EXCLUDE_PREFIXES):
            continue
        sources.append(obj)
    return sources


def join_duplicates(sources):
    """Duplicate the sources and join them, leaving the originals untouched."""
    bpy.ops.object.select_all(action="DESELECT")
    for obj in sources:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = sources[0]

    bpy.ops.object.duplicate()
    duplicates = [o for o in bpy.context.selected_objects if o.type == "MESH"]

    bpy.ops.object.select_all(action="DESELECT")
    for obj in duplicates:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = duplicates[0]
    bpy.ops.object.join()

    return bpy.context.view_layer.objects.active


def main():
    out_dir = os.path.join(resolve_levels_dir(), LEVEL_ID)
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, f"{SECTOR_ID}.glb")

    sources = gather_sources()
    if not sources:
        raise RuntimeError("No exportable meshes found in the scene.")

    materials = {slot.material.name for o in sources for slot in o.material_slots if slot.material}
    joined = join_duplicates(sources)
    joined.name = SECTOR_ID

    bpy.ops.object.select_all(action="DESELECT")
    joined.select_set(True)
    bpy.context.view_layer.objects.active = joined

    bpy.ops.export_scene.gltf(
        filepath=out_path,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
    )

    dims = joined.dimensions
    tris = len(joined.data.polygons)
    bpy.data.objects.remove(joined, do_unlink=True)

    print(f"Exported : {out_path}")
    print(f"Sources  : {len(sources)} meshes, {len(materials)} materials")
    print(f"Faces    : {tris}")
    print(f"Size XYZ : {dims.x:.2f} x {dims.y:.2f} x {dims.z:.2f} (Blender, Z-up)")


if __name__ == "__main__":
    main()
