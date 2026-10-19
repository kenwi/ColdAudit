"""Export every modelled office asset as its own origin-centred GLB.

Run it either way:

    blender -b ~/Work/office_assets.blend -P ~/Work/ColdAudit/tools/export_models.py
    blender -b ~/Work/office_assets.blend -P .../export_models.py -- --dry-run

    # re-import every exported model and check origins, geometry, materials:
    blender -b --factory-startup -P .../export_models.py -- --verify

    # or, from a live Blender session:
    exec(open("/home/kenwi/Work/ColdAudit/tools/export_models.py").read())

Convention (identical to the existing reception_desk.glb):

  * one .glb per asset, named lowercase_snake_case after the object
  * the object's location is zeroed just for the export, so the exported node has
    no transform: the model's origin is (0,0,0) and its geometry is exactly the
    asset-local coordinates it was modelled in.  Every object in the source scene
    is built with identity rotation and scale, which this script asserts.
  * objects that merely instance another asset's mesh (the rack's
    Server_1U_U06/_U12, Server_2U_U14) are skipped - they are the same model.
  * render-only objects (the studio cove) are skipped.
  * _manifest.json records where each asset sat in the review layout, plus its
    local bounding box, size, material list and embedded image list, so the
    level builder can place models and sub-parts (doors, drawers) without
    re-deriving the offsets from the .blend.

Nothing in the source scene is modified: transforms are restored after each
export, and the .blend is never saved by this script.
"""

import json
import math
import os
import re
import struct
import sys

import bpy

# --------------------------------------------------------------------------- #
# configuration
# --------------------------------------------------------------------------- #

# resolved against this file's directory (or the .blend's, if embedded)
TARGET_DIR = "../content/models"

# render-only / non-asset objects, never exported
EXCLUDE = {"BG_Cove"}

# object name -> file stem, only where the mechanical snake_case name is cryptic
RENAME = {"CM_Body": "coffee_machine"}

# exported even when its geometry duplicates an already-exported mesh
KEEP_DUPLICATE_MESHES = False

# set False when importing this file as a module
RUN_ON_LOAD = True

GLTF_OPTIONS = {
    "export_format": "GLB",
    "use_selection": True,
    "export_apply": True,
    "export_yup": True,
    "export_materials": "EXPORT",
    "export_image_format": "AUTO",   # keeps source PNGs as PNG (Raylib has no JPEG decoder)
    "export_texture_dir": "",
    "export_cameras": False,
    "export_lights": False,
    "export_animations": False,
    "export_skins": False,
    "export_morph": False,
}

# `--` separated CLI arguments understood by this script
FLAGS = {"--dry-run", "--keep-duplicates", "--quiet", "--verify"}


# --------------------------------------------------------------------------- #
# helpers
# --------------------------------------------------------------------------- #

def script_dir():
    """Directory of this script, whether run from disk or from a .blend text block."""
    try:
        p = os.path.abspath(__file__)
        d = os.path.dirname(p)
        if not d.endswith(".blend") and os.path.isdir(d):
            return d
    except NameError:
        pass
    if bpy.data.filepath:
        return os.path.dirname(os.path.abspath(bpy.data.filepath))
    raise RuntimeError("cannot resolve the script directory; save the .blend first")


def resolve_dir(path):
    return path if os.path.isabs(path) else os.path.normpath(os.path.join(script_dir(), path))


def snake(name):
    """ReceptionDesk -> reception_desk, PCTower -> pc_tower, Server_1U -> server_1u."""
    s = re.sub(r"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", "_", name)
    s = re.sub(r"[^0-9A-Za-z]+", "_", s)
    return re.sub(r"_+", "_", s).strip("_").lower()


def file_stem(obj):
    return RENAME.get(obj.name, snake(obj.name))


def cli_flags():
    argv = sys.argv
    if "--" in argv:
        argv = argv[argv.index("--") + 1:]
    else:
        argv = []
    unknown = [a for a in argv if a.startswith("-") and a not in FLAGS]
    if unknown:
        raise SystemExit("unknown option(s): %s (known: %s)" % (", ".join(unknown), ", ".join(sorted(FLAGS))))
    return set(argv)


def local_bbox(obj):
    """Bounding box of the object's geometry in its own (origin-centred) space."""
    if not obj.data.vertices:
        return None
    xs = [v.co.x for v in obj.data.vertices]
    ys = [v.co.y for v in obj.data.vertices]
    zs = [v.co.z for v in obj.data.vertices]
    return [min(xs), min(ys), min(zs)], [max(xs), max(ys), max(zs)]


def glb_summary(path):
    """Read back the GLB's JSON chunk: node transform, materials, embedded images."""
    with open(path, "rb") as f:
        magic, version, _length = struct.unpack("<III", f.read(12))
        if magic != 0x46546C67:
            raise ValueError("not a GLB: %s" % path)
        chunk_len, chunk_type = struct.unpack("<II", f.read(8))
        doc = json.loads(f.read(chunk_len).decode("utf-8"))
    nodes = doc.get("nodes", [])
    return {
        "generator": doc.get("asset", {}).get("generator"),
        "nodes": [n.get("name") for n in nodes],
        "node_transform": [
            {k: n.get(k) for k in ("translation", "rotation", "scale")}
            for n in nodes
        ],
        "materials": [m.get("name") for m in doc.get("materials", [])],
        "images": [(i.get("name"), i.get("mimeType")) for i in doc.get("images", [])],
    }


def gltf_kwargs():
    """Only pass options this Blender's exporter actually accepts."""
    supported = set(bpy.ops.export_scene.gltf.get_rna_type().properties.keys())
    return {k: v for k, v in GLTF_OPTIONS.items() if k in supported}


def export_one(obj, path, kwargs):
    """Export a single object with its location zeroed, then restore it."""
    original = tuple(obj.location)
    try:
        obj.location = (0.0, 0.0, 0.0)
        bpy.context.view_layer.update()
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.export_scene.gltf(filepath=path, **kwargs)
    finally:
        obj.location = original
        bpy.context.view_layer.update()


# --------------------------------------------------------------------------- #
# main
# --------------------------------------------------------------------------- #

def verify_all(out_dir, manifest, quiet=False):
    """Re-import every exported GLB and check what actually came back.

    Verifies, per file: a single root object with an empty transform (origin 0),
    vertex/face counts and material list matching the manifest, and that the
    embedded textures are PNG (Raylib-cs 8 has no JPEG decoder).
    """
    problems = []
    for entry in manifest:
        path = os.path.join(out_dir, entry["file"])
        if not os.path.exists(path):
            problems.append((entry["file"], "missing"))
            continue

        for name, mime in entry.get("glb_images", []):
            if mime != "image/png":
                problems.append((entry["file"], "texture %s is %s, not PNG" % (name, mime)))

        before = {k: set(getattr(bpy.data, k)) for k in ("objects", "meshes", "materials", "images")}
        try:
            bpy.ops.import_scene.gltf(filepath=path)
            imported = [o for o in bpy.data.objects if o not in before["objects"]]
            roots = [o for o in imported if o.parent is None]
            if len(roots) != 1:
                problems.append((entry["file"], "expected 1 root object, found %d" % len(roots)))
            else:
                r = roots[0]
                if (tuple(round(v, 6) for v in r.location) != (0.0, 0.0, 0.0)
                        or tuple(round(v, 6) for v in r.scale) != (1.0, 1.0, 1.0)
                        or any(abs(v) > 1e-6 for v in r.rotation_euler)):
                    problems.append((entry["file"],
                                     "root has a transform (loc=%s rot=%s scale=%s)"
                                     % (tuple(r.location), tuple(r.rotation_euler), tuple(r.scale))))
            meshes = [o.data for o in imported if o.type == "MESH"]
            verts = sum(len(m.vertices) for m in meshes)
            tris = sum(len(m.polygons) for m in meshes)   # glTF geometry is triangles only
            if tris != entry.get("tris"):
                problems.append((entry["file"], "%d triangles, manifest says %s"
                                 % (tris, entry.get("tris"))))
            if verts < entry["verts"]:
                problems.append((entry["file"], "vertex count dropped: %d < %d"
                                 % (verts, entry["verts"])))
            # the importer may suffix names (.001) when a name is taken
            got = sorted({re.sub(r"\.\d+$", "", m.name) for m in
                          (mat for o in imported if o.type == "MESH" for mat in o.data.materials) if m})
            if got != sorted(entry["materials"]):
                problems.append((entry["file"], "materials %s, manifest says %s"
                                 % (got, sorted(entry["materials"]))))
        finally:
            for o in [o for o in bpy.data.objects if o not in before["objects"]]:
                bpy.data.objects.remove(o, do_unlink=True)
            for kind in ("meshes", "materials", "images"):
                coll = getattr(bpy.data, kind)
                for d in [x for x in coll if x not in before[kind] and x.users == 0]:
                    coll.remove(d)

    print()
    print("verified %d models, %d problem(s)" % (len(manifest), len(problems)))
    for name, why in problems:
        print("  PROBLEM %-28s %s" % (name, why))
    return problems


def main():
    flags = cli_flags()
    dry_run = "--dry-run" in flags
    quiet = "--quiet" in flags
    keep_dupes = KEEP_DUPLICATE_MESHES or "--keep-duplicates" in flags

    out_dir = resolve_dir(TARGET_DIR)
    os.makedirs(out_dir, exist_ok=True)

    if "--verify" in flags:
        manifest_path = os.path.join(out_dir, "_manifest.json")
        with open(manifest_path, encoding="utf-8") as f:
            manifest = json.load(f)["models"]
        print("verifying %d models in %s" % (len(manifest), out_dir))
        problems = verify_all(out_dir, manifest, quiet)
        return [], problems

    kwargs = gltf_kwargs()

    scene_objects = sorted(bpy.context.scene.objects, key=lambda o: o.name)
    manifest, exported, skipped = [], [], []

    print("source scene : %s" % (bpy.data.filepath or "<unsaved>"))
    print("target dir   : %s" % out_dir)
    print("exporter opts: %s" % ", ".join("%s=%r" % kv for kv in sorted(kwargs.items())))
    print()

    for obj in scene_objects:
        if obj.type != "MESH":
            skipped.append((obj.name, "not a mesh (%s)" % obj.type.lower()))
            continue
        if obj.name in EXCLUDE:
            skipped.append((obj.name, "render-only (EXCLUDE)"))
            continue
        if not obj.visible_get():
            skipped.append((obj.name, "hidden in the viewport"))
            continue

        rot = tuple(round(math.degrees(v), 4) for v in obj.rotation_euler)
        scale = tuple(round(v, 6) for v in obj.scale)
        if any(abs(v) > 1e-6 for v in rot) or any(abs(v - 1.0) > 1e-6 for v in scale):
            raise SystemExit(
                "%s is not identity-rotated/scaled (rot=%s scale=%s); "
                "origin-0 export assumes the asset was modelled at the origin"
                % (obj.name, rot, scale)
            )

        mesh = obj.data
        sharers = [o for o in scene_objects if o.type == "MESH" and o.data is mesh]
        if len(sharers) > 1 and not keep_dupes:
            canonical = min(o.name for o in sharers)   # the first name alphabetically wins
            if canonical != obj.name:
                skipped.append((obj.name, "same mesh as %s" % canonical))
                continue

        stem = file_stem(obj)
        path = os.path.join(out_dir, stem + ".glb")
        if not dry_run:
            export_one(obj, path, kwargs)
        info = {} if dry_run else glb_summary(path)
        if info and info["node_transform"] and any(info["node_transform"][0].values()):
            raise SystemExit("%s exported with a non-empty node transform: %s"
                             % (stem, info["node_transform"][0]))

        bbox = local_bbox(obj)
        mesh.calc_loop_triangles()
        entry = {
            "file": stem + ".glb",
            "object": obj.name,
            "mesh": mesh.name,
            "layout_location": [round(v, 6) for v in obj.location],
            "layout_rotation_deg": [round(v, 4) for v in rot],
            "instances": [
                {"object": o.name, "layout_location": [round(v, 6) for v in o.location]}
                for o in sharers if o is not obj
            ],
            "verts": len(mesh.vertices),
            "faces": len(mesh.polygons),
            "tris": len(mesh.loop_triangles),
            "materials": sorted({m.name for m in mesh.materials if m}),
            "bbox_local_min": [round(v, 5) for v in bbox[0]],
            "bbox_local_max": [round(v, 5) for v in bbox[1]],
            "size": [round(bbox[1][i] - bbox[0][i], 5) for i in range(3)],
            "bytes": 0 if dry_run else os.path.getsize(path),
        }
        if info:
            entry["glb_node"] = info["nodes"]
            entry["glb_images"] = info["images"]
        manifest.append(entry)
        exported.append(entry)
        if not quiet:
            print("  %-26s %-22s %6d f  %s" % (
                stem + ".glb", obj.name, entry["faces"],
                "dry-run" if dry_run else "%.1f MB" % (entry["bytes"] / 1e6)))

    if not dry_run:
        manifest_path = os.path.join(out_dir, "_manifest.json")
        with open(manifest_path, "w", encoding="utf-8") as f:
            json.dump({
                "source": bpy.data.filepath,
                "convention": "object location zeroed for export; model origin is (0,0,0)",
                "models": manifest,
            }, f, indent=2, sort_keys=False)
            f.write("\n")

    print()
    print("exported %d models, %d objects skipped" % (len(exported), len(skipped)))
    for name, why in skipped:
        print("  skipped %-26s %s" % (name, why))
    if not dry_run:
        total = sum(e["bytes"] for e in exported)
        print("total %.1f MB in %s" % (total / 1e6, out_dir))
        print("manifest: _manifest.json")
    return exported, skipped


if RUN_ON_LOAD:
    main()
