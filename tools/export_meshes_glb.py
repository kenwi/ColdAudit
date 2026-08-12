import bpy
import os

TARGET_DIR = ""
EXPORT_CURVES = True

def clean_filename(name):
    invalid = '<>:"/\\|?*'
    for ch in invalid:
        name = name.replace(ch, "_")
    return name.strip() or "mesh"

def main():
    os.makedirs(TARGET_DIR, exist_ok=True)
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

        path = os.path.join(TARGET_DIR, clean_filename(obj.name) + ".glb")
        bpy.ops.export_scene.gltf(
            filepath=path,
            export_format="GLB",
            use_selection=True,
            export_apply=True,
        )
        bpy.ops.object.delete()
        print(f"Exported: {path}")

    print(f"Done. Exported {len(meshes)} meshes to {TARGET_DIR}")

if __name__ == "__main__":
    main()
