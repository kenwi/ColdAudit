import bpy
import json
import os

TARGET_DIR = "../content/levels"


def clean_filename(name):
    invalid = '<>:"/\\|?*'
    for ch in invalid:
        name = name.replace(ch, "_")
    return name.strip() or "mesh"


def resolve_script_dir():
    """Directory used to resolve relative TARGET_DIR.

    When the script is run from disk, use __file__. When it is an embedded
    Blender text block, __file__ looks like path/to/file.blend/script.py, so
    fall back to the saved .blend file's directory.
    """
    try:
        script_path = os.path.abspath(__file__)
        script_dir = os.path.dirname(script_path)
        # Embedded text blocks: dirname ends with ".blend" (the blend file itself)
        if not script_dir.endswith(".blend") and os.path.isdir(script_dir):
            return script_dir
    except NameError:
        pass

    blend_path = bpy.data.filepath
    if blend_path:
        return os.path.dirname(os.path.abspath(blend_path))

    raise RuntimeError(
        "Cannot resolve relative TARGET_DIR. Save the .blend file or set TARGET_DIR to an absolute path."
    )


def serialize_id_property(value):
    """Convert Blender ID properties into JSON-safe Python values."""
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float, str)):
        return value
    if hasattr(value, "to_list"):
        return [serialize_id_property(v) for v in value.to_list()]
    if hasattr(value, "keys") and not isinstance(value, (str, bytes)):
        return {str(k): serialize_id_property(value[k]) for k in value.keys()}
    try:
        return list(value)
    except TypeError:
        return str(value)


def object_transform(obj):
    return {
        "location": [float(v) for v in obj.location],
        "rotation_euler": [float(v) for v in obj.rotation_euler],
        "rotation_quaternion": [float(v) for v in obj.rotation_quaternion],
        "rotation_mode": obj.rotation_mode,
        "scale": [float(v) for v in obj.scale],
        "matrix_world": [
            [float(v) for v in row]
            for row in obj.matrix_world
        ],
    }


def object_custom_properties(obj):
    props = {}
    for key in obj.keys():
        if key == "_RNA_UI":
            continue
        props[key] = serialize_id_property(obj[key])
    return props


def write_metadata(obj, json_path):
    payload = {
        "name": obj.name,
        "transform": object_transform(obj),
        "custom_properties": object_custom_properties(obj),
    }
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2)
        f.write("\n")


def main():
    target_dir = TARGET_DIR
    if not os.path.isabs(target_dir):
        target_dir = os.path.normpath(os.path.join(resolve_script_dir(), target_dir))
    os.makedirs(target_dir, exist_ok=True)
    meshes = [
        o for o in bpy.context.scene.objects
        if o.type == "MESH" and not o.hide_get()
    ]

    if not meshes:
        print("No meshes found in the scene.")
        return

    for obj in meshes:
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj

        bpy.ops.object.duplicate()  # duplicate so we don't mutate the original mesh
        dup = bpy.context.active_object
        bpy.ops.object.select_all(action="DESELECT")
        dup.select_set(True)
        bpy.context.view_layer.objects.active = dup

        base_name = clean_filename(obj.name)
        path = os.path.join(target_dir, base_name + ".glb")
        json_path = os.path.join(target_dir, base_name + ".json")
        bpy.ops.export_scene.gltf(
            filepath=path,
            export_format="GLB",
            use_selection=True,
            export_apply=True,
        )
        write_metadata(obj, json_path)
        bpy.ops.object.delete()
        print(f"Exported: {path}")
        print(f"Metadata: {json_path}")

    print(f"Done. Exported {len(meshes)} meshes to {target_dir}")

if __name__ == "__main__":
    main()
