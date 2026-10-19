"""Export the current Blender scene as ColdAudit sector GLBs.

ColdAudit sectors are *world-authored*: the mesh keeps its absolute Blender
coordinates and the glTF Y-up conversion makes it land upright in the engine.

The scene is joined into several objects rather than one, and that is not an
optimisation detail - it is the hard limit of the renderer. The engine loads
sectors with raylib, whose ``Mesh.indices`` is ``unsigned short *``, and raylib's
glTF loader copies a primitive's indices down to that type without a bounds check:

    model.meshes[i].indices = RL_MALLOC(attribute->count*sizeof(unsigned short));
    LOAD_ATTRIBUTE_CAST(attribute, 1, unsigned int, model.meshes[i].indices, unsigned short);

So a primitive with more than 65,535 vertices silently wraps: vertex 65,536 is
drawn as vertex 0, and the primitive turns into triangles between the wrong
vertices - long spikes across the level. The engine's collision mesh is cooked
from the same (uint16) indices, so it inherits the corruption, which is how a
level that renders garbage also drops the player through the floor.

One joined object means one primitive *per material*, so a single heavy material
(33 chairs' worth of Furniture_Dark, say) is enough on its own. Batching by
triangle count keeps every primitive in a batch under 3 x MAX_TRIS_PER_CHUNK
vertices - three per triangle being the most an exporter can ever split to - and
so safely under 65,535.

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

# Triangle budget per exported object. The exporter may split every triangle's
# vertices, so a batch of N triangles cannot produce a primitive above 3N
# vertices; 20,000 triangles keeps that under 60,000, below the 65,535 that
# raylib's uint16 indices can address.
MAX_TRIS_PER_CHUNK = 20000


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


def triangles(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def pack_sources(sources, max_tris):
    """Greedy batch of sources, each batch under max_tris triangles.

    Order comes from the scene, so the batching is stable between runs. A single
    object heavier than the budget becomes a batch of its own: it cannot be split
    any further here, and one object is normally one part, not a material's whole
    share of the level.
    """
    batches = []
    current = []
    count = 0
    for obj in sources:
        tris = triangles(obj)
        if current and count + tris > max_tris:
            batches.append(current)
            current = []
            count = 0
        current.append(obj)
        count += tris
    if current:
        batches.append(current)
    return batches


def duplicate_and_join(objects):
    """Duplicate the given objects and join them, leaving the originals untouched."""
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]

    bpy.ops.object.duplicate()
    duplicates = [o for o in bpy.context.selected_objects if o.type == "MESH"]

    bpy.ops.object.select_all(action="DESELECT")
    for obj in duplicates:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = duplicates[0]
    bpy.ops.object.join()

    joined = bpy.context.view_layer.objects.active
    # Bake the object transform into the mesh so every batch is authored in the
    # same world space as the level, with an identity transform on its node. The
    # engine's collision soup only ever applies the model transform, so vertices
    # that are already absolute are the one form every batch can agree on.
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return joined


def main():
    out_dir = os.path.join(resolve_levels_dir(), LEVEL_ID)
    os.makedirs(out_dir, exist_ok=True)
    out_path = os.path.join(out_dir, f"{SECTOR_ID}.glb")

    sources = gather_sources()
    if not sources:
        raise RuntimeError("No exportable meshes found in the scene.")

    materials = {slot.material.name for o in sources for slot in o.material_slots if slot.material}

    batches = pack_sources(sources, MAX_TRIS_PER_CHUNK)
    chunks = []
    for i, batch in enumerate(batches):
        joined = duplicate_and_join(batch)
        joined.name = f"{SECTOR_ID}_{i:02d}"
        chunks.append(joined)

    bpy.ops.object.select_all(action="DESELECT")
    for chunk in chunks:
        chunk.select_set(True)
    bpy.context.view_layer.objects.active = chunks[0]

    bpy.ops.export_scene.gltf(
        filepath=out_path,
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
    )

    tris = sum(len(c.data.polygons) for c in chunks)
    heaviest = max((triangles(c) for c in chunks), default=0)
    mins = [min(v.co[i] for c in chunks for v in c.data.vertices) for i in range(3)]
    maxs = [max(v.co[i] for c in chunks for v in c.data.vertices) for i in range(3)]
    for chunk in chunks:
        bpy.data.objects.remove(chunk, do_unlink=True)

    print(f"Exported : {out_path}")
    print(f"Sources  : {len(sources)} meshes, {len(materials)} materials")
    print(f"Faces    : {tris}")
    print(f"Chunks   : {len(chunks)} objects, heaviest {heaviest} triangles")
    print(f"Size XYZ : {maxs[0] - mins[0]:.2f} x {maxs[1] - mins[1]:.2f} x {maxs[2] - mins[2]:.2f} (Blender, Z-up)")


if __name__ == "__main__":
    main()
