"""Inspect a sector GLB the way the engine's renderer and physics see it.

The engine loads sectors through Raylib, so the file has to satisfy Raylib, not just
Blender and glTF:

  * ``Mesh.indices`` is ``unsigned short *``, and Raylib's glTF loader copies a
    primitive's indices into that type with a plain C cast -
    ``LOAD_ATTRIBUTE_CAST(attribute, 1, unsigned int, mesh.indices, unsigned short)``
    in ``rmodels.c`` - with no bounds check. Blender writes 32-bit indices as soon as a
    primitive needs them, so **a primitive with more than 65,535 vertices silently
    wraps**: vertex 65,536 is drawn as vertex 0 and the primitive becomes triangles
    between unrelated vertices.
  * Raylib bakes each glTF node's *world* transform into the mesh vertices and leaves
    ``model.transform`` at identity, so node transforms are applied here too.

ColdAudit then cooks its collision from the same (uint16) indices
(``ModelTriangleSoup`` -> ``ModelCollisionBuilder``), which is how a level that renders
as spikes also throws the player through the floor. Box3D cooks that garbage happily -
a failed cook is *not* what a corrupted sector looks like.

This script rebuilds that soup offline: per-primitive vertex counts, whether anything
is over the limit, and what a ray dropped through a point would hit. ``--dump`` writes
the soup for ``tools/SectorCookTest``, which cooks it in Box3D for real and fires rays
at it, no game launch needed.

Usage
-----
    python3 tools/sector_soup.py content/levels/office/office.glb
    python3 tools/sector_soup.py --spawn -1.5 8.6 --dump /tmp/soup.bin office.glb
    python3 tools/sector_soup.py --compare old.glb new.glb   # authored geometry same?
"""

import argparse
import json
import math
import struct
import sys

COMPONENT = {
    5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2),
    5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4),
}
TYPE_SIZE = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}
U16 = 5123
U32 = 5125
FLOAT = 5126
# The renderer's hard limit, and therefore ours.
MAX_VERTICES_PER_PRIMITIVE = 65535
# The office sector's player spawn (LevelLoadFeature), on the glTF ground plane.
DEFAULT_SPAWN = (-1.5, 8.6)


def load_glb(path):
    with open(path, "rb") as fh:
        data = fh.read()
    magic, _, length = struct.unpack_from("<III", data, 0)
    if magic != 0x46546C67:
        raise ValueError(f"{path}: not a GLB")
    gltf, binary = None, b""
    offset = 12
    while offset < length:
        chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
        body = data[offset + 8: offset + 8 + chunk_len]
        if chunk_type == 0x4E4F534A:
            gltf = json.loads(body)
        elif chunk_type == 0x004E4942:
            binary = body
        offset += 8 + chunk_len + ((4 - chunk_len % 4) % 4 if chunk_len % 4 else 0)
    if gltf is None:
        raise ValueError(f"{path}: no JSON chunk")
    return gltf, binary


def read_accessor(gltf, binary, index):
    acc = gltf["accessors"][index]
    fmt, size = COMPONENT[acc["componentType"]]
    ncomp = TYPE_SIZE[acc["type"]]
    if "bufferView" not in acc:
        return [(0.0,) * ncomp] * acc["count"]
    view = gltf["bufferViews"][acc["bufferView"]]
    base = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = view.get("byteStride") or ncomp * size
    out = []
    for i in range(acc["count"]):
        vals = struct.unpack_from("<" + fmt * ncomp, binary, base + i * stride)
        out.append(vals if ncomp > 1 else vals[0])
    return out


def mat_identity():
    return [1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0]


def mat_mul(a, b):
    out = [0.0] * 16
    for c in range(4):
        for r in range(4):
            out[c * 4 + r] = sum(a[k * 4 + r] * b[c * 4 + k] for k in range(4))
    return out


def mat_from_trs(t, r, s):
    x, y, z, w = r
    xx, yy, zz = x * x, y * y, z * z
    xy, xz, yz = x * y, x * z, y * z
    wx, wy, wz = w * x, w * y, w * z
    sx, sy, sz = s
    return [
        (1 - 2 * (yy + zz)) * sx, (2 * (xy + wz)) * sx, (2 * (xz - wy)) * sx, 0.0,
        (2 * (xy - wz)) * sy, (1 - 2 * (xx + zz)) * sy, (2 * (yz + wx)) * sy, 0.0,
        (2 * (xz + wy)) * sz, (2 * (yz - wx)) * sz, (1 - 2 * (xx + yy)) * sz, 0.0,
        t[0], t[1], t[2], 1.0,
    ]


def node_local(node):
    if "matrix" in node:
        return list(node["matrix"])
    return mat_from_trs(node.get("translation", [0, 0, 0]),
                        node.get("rotation", [0, 0, 0, 1]),
                        node.get("scale", [1, 1, 1]))


def apply_matrix(m, p):
    x, y, z = p
    return (m[0] * x + m[4] * y + m[8] * z + m[12],
            m[1] * x + m[5] * y + m[9] * z + m[13],
            m[2] * x + m[6] * y + m[10] * z + m[14])


def world_matrices(gltf):
    nodes = gltf.get("nodes", [])
    world = [None] * len(nodes)

    def walk(i, parent):
        m = mat_mul(parent, node_local(nodes[i]))
        world[i] = m
        for child in nodes[i].get("children", []):
            walk(child, m)

    root = mat_identity()
    for scene in gltf.get("scenes", []):
        for i in scene.get("nodes", []):
            walk(i, root)
    for i in range(len(nodes)):  # Raylib uses every node, scene or not.
        if world[i] is None:
            walk(i, root)
    return world


def build_soup(path, emulate_raylib=True):
    """Every primitive Raylib turns into a mesh, as Raylib would hold it."""
    gltf, binary = load_glb(path)
    world = world_matrices(gltf)
    vertices, indices, primitives = [], [], []

    for node_index, node in enumerate(gltf.get("nodes", [])):
        if "mesh" not in node:
            continue
        m = world[node_index]
        for prim in gltf["meshes"][node["mesh"]].get("primitives", []):
            if prim.get("mode", 4) != 4:
                continue
            attrs = prim.get("attributes", {})
            if "POSITION" not in attrs:
                continue
            pos = gltf["accessors"][attrs["POSITION"]]
            if pos["type"] != "VEC3" or pos["componentType"] != FLOAT:
                continue

            base = len(vertices)
            vertices.extend(apply_matrix(m, p)
                            for p in read_accessor(gltf, binary, attrs["POSITION"]))

            component_type = U16
            if "indices" in prim:
                acc = gltf["accessors"][prim["indices"]]
                component_type = acc["componentType"]
                values = read_accessor(gltf, binary, prim["indices"])
                if emulate_raylib and component_type == U32:
                    values = [v & 0xFFFF for v in values]
                count = (acc["count"] // 3) * 3
                indices.extend(base + int(values[i]) for i in range(count))
                triangles = count // 3
            else:
                indices.extend(base + i for i in range(pos["count"]))
                triangles = pos["count"] // 3

            primitives.append({
                "node": node.get("name", str(node_index)),
                "material": (gltf["materials"][prim["material"]]["name"]
                             if prim.get("material") is not None else "(none)"),
                "vertices": pos["count"],
                "triangles": triangles,
                "component_type": component_type,
            })

    return gltf, vertices, indices, primitives


def triangle_set(vertices, indices, precision=4):
    out = set()
    for t in range(0, len(indices), 3):
        out.add(frozenset(
            tuple(round(c, precision) for c in vertices[indices[t + k]]) for k in range(3)))
    return out


def fmt_component(ctype):
    return {5121: "u8", 5123: "u16", 5125: "u32"}.get(ctype, str(ctype))


def report(path, spawn):
    gltf, vertices, indices, primitives = build_soup(path)
    over = [p for p in primitives if p["vertices"] > MAX_VERTICES_PER_PRIMITIVE]
    u32 = [p for p in primitives if p["component_type"] == U32]

    print(f"=== {path}")
    print(f"    nodes={len(gltf.get('nodes', []))} meshes={len(gltf.get('meshes', []))} "
          f"primitives={len(primitives)} soup verts={len(vertices)} tris={len(indices) // 3}")
    print(f"    primitives over {MAX_VERTICES_PER_PRIMITIVE} verts: {len(over)}"
          f"   with 32-bit indices: {len(u32)}")
    for p in sorted(primitives, key=lambda p: -p["vertices"])[:6]:
        flag = "  <== WRAPS" if p["vertices"] > MAX_VERTICES_PER_PRIMITIVE else ""
        print(f"      {p['vertices']:>8} v {p['triangles']:>8} t  "
              f"{fmt_component(p['component_type']):>3}  {p['material']}{flag}")

    max_edge, spikes = 0.0, 0
    for t in range(0, len(indices), 3):
        tri = [vertices[indices[t + k]] for k in range(3)]
        for a in range(3):
            d = math.dist(tri[a], tri[(a + 1) % 3])
            max_edge = max(max_edge, d)
            spikes += d > 20.0
    print(f"    longest triangle edge: {max_edge:.1f} m   edges > 20 m: {spikes}")

    hits = probe(vertices, indices, spawn[0], spawn[1])
    if not hits:
        print(f"    surfaces over {spawn}: none")
    else:
        hits.sort()
        band = [h for h in hits if -2.0 < h < 1.0]
        print(f"    surfaces over {spawn}: {len(hits)}  lowest y={hits[0]:+.3f} "
              f"highest y={hits[-1]:+.3f}")
        print(f"      in the floor band (-2, 1): {len(band)}"
              + (f"  -> floor at y={max(band):+.3f}" if band else "  -> NO FLOOR"))

    if over:
        print(f"    PROBLEM: {len(over)} primitive(s) exceed the renderer's uint16 indices; "
              f"those render and collide as garbage. Split the sector (see tools/export_level_glb.py).")
    print()
    return not over


def probe(vertices, indices, x, z):
    """Heights of every triangle crossing the vertical line through (x, z)."""
    hits = []
    for t in range(0, len(indices), 3):
        a, b, c = (vertices[indices[t + k]] for k in range(3))
        det = (b[2] - c[2]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[2] - c[2])
        if abs(det) < 1e-12:
            continue
        u = ((b[2] - c[2]) * (x - c[0]) + (c[0] - b[0]) * (z - c[2])) / det
        v = ((c[2] - a[2]) * (x - c[0]) + (a[0] - c[0]) * (z - c[2])) / det
        w = 1.0 - u - v
        if u < -1e-9 or v < -1e-9 or w < -1e-9:
            continue
        hits.append(u * a[1] + v * b[1] + w * c[1])
    return hits


def dump(path, out_path):
    """Write the soup for tools/SectorCookTest: meshes kept separate, as Raylib makes them."""
    gltf, binary = load_glb(path)
    world = world_matrices(gltf)
    meshes = []

    for node_index, node in enumerate(gltf.get("nodes", [])):
        if "mesh" not in node:
            continue
        m = world[node_index]
        for prim in gltf["meshes"][node["mesh"]].get("primitives", []):
            attrs = prim.get("attributes", {})
            if "POSITION" not in attrs:
                continue
            pos = gltf["accessors"][attrs["POSITION"]]
            if pos["type"] != "VEC3" or pos["componentType"] != FLOAT:
                continue
            verts = [apply_matrix(m, p) for p in read_accessor(gltf, binary, attrs["POSITION"])]
            if "indices" in prim:
                acc = gltf["accessors"][prim["indices"]]
                values = read_accessor(gltf, binary, prim["indices"])
                if acc["componentType"] == U32:
                    values = [v & 0xFFFF for v in values]
                count = (acc["count"] // 3) * 3
                idx = [int(values[i]) for i in range(count)]
            else:
                idx = list(range(pos["count"]))
            if len(verts) >= 3 and len(idx) >= 3:
                meshes.append((verts, idx))

    with open(out_path, "wb") as fh:
        fh.write(struct.pack("<I", len(meshes)))
        for verts, idx in meshes:
            fh.write(struct.pack("<II", len(verts), len(idx) // 3))
            fh.write(struct.pack("<%df" % (len(verts) * 3), *(c for v in verts for c in v)))
            fh.write(struct.pack("<%di" % len(idx), *idx))

    print(f"wrote {out_path}: {len(meshes)} meshes, "
          f"{sum(len(v) for v, _ in meshes)} verts, {sum(len(i) // 3 for _, i in meshes)} tris")


def compare(a, b, tolerance=1e-3):
    """Authored geometry (32-bit indices, no truncation) identical between two files?

    Compared through a metre-scale tolerance rather than equal floats: baking an object's
    origin into its vertices moves them by a few microns, and rounding to any fixed number
    of decimals flips the odd coordinate that happens to sit on a boundary.
    """
    _, va, ia, _ = build_soup(a, emulate_raylib=False)
    _, vb, ib, _ = build_soup(b, emulate_raylib=False)
    ta, tb = triangle_set(va, ia), triangle_set(vb, ib)

    print(f"{a}: {len(ia) // 3} tris, {len(ta)} unique")
    print(f"{b}: {len(ib) // 3} tris, {len(tb)} unique")

    if ta == tb:
        print("authored geometry identical: True")
        return True

    centroid = lambda tri: tuple(sum(c[i] for c in tri) / 3 for i in range(3))
    only_a = [centroid(tri) for tri in ta - tb]
    only_b = [centroid(tri) for tri in tb - ta]
    worst = max((min(math.dist(p, q) for q in only_b) for p in only_a), default=0.0)
    same = worst <= tolerance

    print(f"  differing triangles: {len(only_a)} in first, {len(only_b)} in second")
    print(f"  worst displacement: {worst * 1000:.4f} mm (tolerance {tolerance * 1000:.1f} mm)")
    print(f"authored geometry identical: {same}")
    return same


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("files", nargs="+")
    parser.add_argument("--spawn", nargs=2, type=float, default=DEFAULT_SPAWN,
                        metavar=("X", "Z"), help="point to drop a vertical probe through")
    parser.add_argument("--dump", metavar="OUT.bin",
                        help="write the Raylib soup for tools/SectorCookTest")
    parser.add_argument("--compare", action="store_true",
                        help="with exactly two files, check their authored geometry matches")
    args = parser.parse_args(argv)

    if args.compare:
        if len(args.files) != 2:
            parser.error("--compare needs exactly two files")
        return 0 if compare(args.files[0], args.files[1]) else 1

    if args.dump:
        if len(args.files) != 1:
            parser.error("--dump needs exactly one file")
        dump(args.files[0], args.dump)
        return 0

    ok = True
    for path in args.files:
        ok = report(path, tuple(args.spawn)) and ok
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
