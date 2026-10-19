"""Build the ColdAudit office level from scratch.

This is the source of truth for the level geometry (the .blend contains no embedded
scripts). It clears the scene, rebuilds the two-storey office, and optionally saves
the .blend. Run it with the exporter in one Blender session:

    blender --background \
        --python tools/build_office_level.py \
        --python tools/export_level_glb.py

Save path defaults to ~/Work/office-level.blend; override with OFFICE_BLEND_PATH.

Furniture that is hand-modelled rather than boxed comes from content/models - one
origin-centred GLB per asset, written by tools/export_models.py - so those files must
be present for the build to run.

Facade openings are aligned to the interior walls: every opening sits fully inside a
bay, with a solid pier beside any wall that meets the facade, so no wall terminates
inside a window or doorway.
"""

import bpy, bmesh, math, os, re
from mathutils import Vector


def repo_root():
    """Repo root, whether this runs from tools/ or from an interactive session."""
    try:
        return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    except NameError:
        return os.getcwd()


# Poly Haven CC0 textures, keyed by material name. Each entry is the diffuse PNG plus
# the real-world span one repeat of it covers, as published by Poly Haven, so the UV
# pass at the end of the build can keep every surface life-sized.
#
# Stored as PNG, not the JPEG Poly Haven ships: the raylib build this project links
# has no JPEG decoder (LoadImage on a .jpg returns a 0x0 image), and a GLB embeds the
# image verbatim, so a JPEG would arrive in the engine untextured.
def _tex(asset):
    return os.path.join(repo_root(), "content", "textures", asset,
                        asset + "_diffuse.png")

TEXTURES = {
    "Floor_Laminate": (_tex("laminate_floor_02"),     1.70),
    "Wall_Interior":  (_tex("white_stucco"),          2.00),
    # Cast concrete, smooth: the slabs and stairs keep the marks of the boards they
    # were formed against, while outside and on the plaza the finish is poured flat. A
    # grooved panel texture was tried on the facade and read as timber at 4 m a repeat.
    "Slab_Concrete":  (_tex("concrete_slab_wall_02"), 2.10),
    "Site_Concrete":  (_tex("concrete_wall_009"),     1.80),
    "Wall_Exterior":  (_tex("concrete_wall_009"),     1.80),
    "Desk_Wood":      (_tex("oak_wood_planks"),       1.20),
    "Table_Dark":     (_tex("stair_planks"),          1.20),
    "Dark_Leather":   (_tex("leather_red_02"),        0.60),
    "Accent_Blue":    (_tex("blue_metal_plate"),      2.50),
    # Leaves are a good deal smaller than a planter, so this one is tiled below its
    # real 2.3 m to keep the foliage in scale with the 0.7 m box it wraps.
    "Plant":          (_tex("forest_leaves_03"),      1.20),
}

# ============================================================== cleanup
for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
for coll in list(bpy.data.collections):
    bpy.data.collections.remove(coll)
for block in (bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
    for b in list(block):
        if b.users == 0:
            block.remove(b)

scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'

# ============================================================== collections
ROOT = bpy.data.collections.new("OfficeBuilding")
scene.collection.children.link(ROOT)
def sub(name):
    c = bpy.data.collections.new(name); ROOT.children.link(c); return c
C_STRUCT = sub("Structure")
C_WALLS  = sub("Interior_Walls")
C_STAIRS = sub("Stairs")
C_FURN   = sub("Furniture")
C_ROOF   = sub("Roof")
C_LIGHT  = sub("Lights")

# ============================================================== materials
def _sock(node, ident):
    for s in node.inputs:
        if s.identifier == ident:
            return s
    return None

def make_mat(name, color, rough=0.85, metal=0.0, emis=None, emis_strength=1.5):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    s = _sock(bsdf, 'Base Color')
    if s: s.default_value = (color[0], color[1], color[2], 1.0)
    s = _sock(bsdf, 'Metallic')
    if s: s.default_value = metal
    s = _sock(bsdf, 'Roughness')
    if s: s.default_value = rough
    if emis is not None:
        s = _sock(bsdf, 'Emission Color') or _sock(bsdf, 'Emission')
        if s: s.default_value = (emis[0], emis[1], emis[2], 1.0)
        s = _sock(bsdf, 'Emission Strength')
        if s: s.default_value = emis_strength
    m.diffuse_color = (color[0], color[1], color[2], 1.0)
    return m

def make_tex_mat(name, rough=0.75, metal=0.0, fallback=(0.62, 0.52, 0.42)):
    """Principled material whose Base Color is the texture TEXTURES[name] names.

    Only albedo is wired: the exporter packs glTF metallicRoughness as G=rough,
    B=metal, while the engine's shader reads its mraMap as r=metal, g=rough,
    b=ao, so a roughness map here would show up as full metalness. Add one only
    alongside a matching repack in pbr.fs / BasicLighting.
    """
    image_path = TEXTURES[name][0]
    if not os.path.isfile(image_path):
        print("WARN: texture missing, flat colour instead:", image_path)
        return make_mat(name, fallback, rough, metal)

    img = bpy.data.images.load(image_path, check_existing=True)
    img.colorspace_settings.name = 'sRGB'

    m = bpy.data.materials.new(name)
    m.use_nodes = True
    tree = m.node_tree
    bsdf = next(n for n in tree.nodes if n.type == 'BSDF_PRINCIPLED')

    tex = tree.nodes.new('ShaderNodeTexImage')
    tex.label = tex.name = "BaseColor"
    tex.image = img
    tex.location = (-460, 240)
    tex.interpolation = 'Linear'
    # No Mapping node: UVs already carry the tiling, so the glTF stays transform-free.
    tree.links.new(tex.outputs['Color'], _sock(bsdf, 'Base Color'))

    s = _sock(bsdf, 'Roughness')
    if s: s.default_value = rough
    s = _sock(bsdf, 'Metallic')
    if s: s.default_value = metal

    m.diffuse_color = (fallback[0], fallback[1], fallback[2], 1.0)
    return m

M = {}
# Textured: everything a player reads as a material. What stays flat is deliberate -
# glass, screens, LEDs and the light panels are transparent or emissive, and the metal
# is flat because Poly Haven's whole metal library is weathered or rusty, which would
# make clean office steelwork look corroded.
M['slab']    = make_tex_mat("Slab_Concrete",  rough=0.90, fallback=(0.55, 0.55, 0.54))
# One material for both storeys. The ceilings are the undersides of the slab and roof
# boxes, so a floor finish can never bleed onto a ceiling.
M['floor']   = make_tex_mat("Floor_Laminate", rough=0.55)
M['wall']    = make_tex_mat("Wall_Interior",  rough=0.90, fallback=(0.87, 0.87, 0.85))
M['ext']     = make_tex_mat("Wall_Exterior",  rough=0.85, fallback=(0.72, 0.71, 0.68))
M['glass']   = make_mat("Glass",             (0.62, 0.76, 0.86), 0.12)
M['wood']    = make_tex_mat("Desk_Wood",      rough=0.60, fallback=(0.44, 0.29, 0.16))
M['wood2']   = make_tex_mat("Table_Dark",     rough=0.60, fallback=(0.26, 0.17, 0.10))
M['metal']   = make_mat("Metal",             (0.56, 0.57, 0.59), 0.35, 0.9)
M['dark']    = make_mat("Furniture_Dark",    (0.10, 0.11, 0.13), 0.5, 0.5)
# Upholstery only: monitor stands, the security console and the planters keep the matte
# dark plastic above, which leather would be wrong on.
M['leather'] = make_tex_mat("Dark_Leather",   rough=0.45, fallback=(0.30, 0.14, 0.11))
M['screen']  = make_mat("Screen",            (0.04, 0.12, 0.20), 0.3, 0.0, (0.15, 0.55, 0.9), 3.0)
M['server']  = make_mat("Server_Rack",       (0.06, 0.07, 0.09), 0.5, 0.7)
M['led']     = make_mat("LED_Green",         (0.10, 0.80, 0.20), 0.4, 0.0, (0.20, 1.0, 0.30), 6.0)
M['rail']    = make_mat("Railing",           (0.24, 0.25, 0.27), 0.4, 0.8)
M['stairs']  = M['slab']     # cast in the same concrete as the slabs
M['accent']  = make_tex_mat("Accent_Blue",    rough=0.55, fallback=(0.08, 0.32, 0.62))
M['plant']   = make_tex_mat("Plant",          rough=0.85, fallback=(0.14, 0.42, 0.17))
M['ground']  = make_mat("Ground",            (0.20, 0.24, 0.19), 1.0)
M['site']    = make_tex_mat("Site_Concrete",  rough=0.90, fallback=(0.62, 0.62, 0.60))
M['panel']   = make_mat("Ceiling_Light",     (0.95, 0.95, 0.92), 0.5, 0.0, (1.0, 0.97, 0.92), 4.0)

# Imported assets (see import_model below) are re-slotted onto these where the names
# match, so a model never drags in a duplicate "Table_Dark.001" carrying the same PNG.
MATERIALS_BY_NAME = {m.name: m for m in M.values()}

# A look that only an asset has - the chair's plain shells, say - is not in MATERIALS_BY_NAME
# at all, and the importer only de-duplicates against the level's own materials. So the
# first import of such a material is remembered here by name stem, and every later import
# of the same look is re-slotted onto it. Without this, thirty-odd chair imports leave
# Chair_Grey.001 ... Chair_Grey.033 behind, all of them in use.
IMPORTED_MATERIALS = {}

# Objects that arrived from content/models. They are UV'd per material when they are
# modelled, so the world-space box projection at the end of the build must leave them
# alone.
IMPORTED = []

# ============================================================== geometry helpers
def _finalize(ob):
    # bake rotation + scale into the mesh, keeping location as the object origin
    m = ob.matrix_basis.copy()
    m.translation = (0.0, 0.0, 0.0)
    ob.data.transform(m)
    ob.rotation_euler = (0.0, 0.0, 0.0)
    ob.scale = (1.0, 1.0, 1.0)

def _uv_box(ob, tile):
    """Overwrite UVs with a world-space box projection: each face is projected onto the
    plane it faces, so a textured surface stays life-sized however the object is placed
    or rotated.

    Per face rather than per object, so floors, walls and ceilings tile continuously -
    including a wall and the slab above it, which meet at right angles.

    Call after _finalize, which bakes scale and rotation into the mesh and leaves only
    the object's location between mesh coordinates and world space.
    """
    me = ob.data
    uvl = me.uv_layers[0] if me.uv_layers else me.uv_layers.new(name="UVMap")
    ox, oy, oz = ob.location
    for poly in me.polygons:
        n = poly.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for li in poly.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            w = (ox + co.x, oy + co.y, oz + co.z)
            u, v = ((w[1], w[2]) if ax == 0 else
                    (w[0], w[2]) if ax == 1 else (w[0], w[1]))
            uvl.data[li].uv = (u / tile, v / tile)

def texture_uvs():
    """Project real-world UVs onto every object whose material has a texture.

    Runs once at the end of the build, when all rotation and scale is baked out.
    """
    for ob in bpy.data.objects:
        if ob.type != 'MESH' or not ob.data.materials:
            continue
        if ob in IMPORTED:
            continue
        mat = ob.data.materials[0]
        if mat is not None and mat.name in TEXTURES:
            _uv_box(ob, TEXTURES[mat.name][1])

def box(name, center, size, mat, coll):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0, calc_uvs=True); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    ob.scale = (size[0], size[1], size[2])
    ob.location = center
    coll.objects.link(ob)
    if mat is not None:
        ob.data.materials.append(mat)
    _finalize(ob)
    return ob

def segment(name, p1, p2, z0, z1, thickness, mat, coll):
    dx = p2[0] - p1[0]; dy = p2[1] - p1[1]
    L = math.hypot(dx, dy)
    if L < 1e-5 or (z1 - z0) < 1e-5:
        return None
    ob = box(name, ((p1[0] + p2[0]) / 2.0, (p1[1] + p2[1]) / 2.0, (z0 + z1) / 2.0),
             (L, thickness, z1 - z0), mat, coll)
    ob.rotation_euler = (0.0, 0.0, math.atan2(dy, dx))
    _finalize(ob)
    return ob

def wall(name, p1, p2, z0, z1, thickness, mat, coll, doors=(), windows=()):
    """Build a wall along p1->p2 with full-height door gaps and sill/header windows.

    doors/windows are (a, b) distances measured along p1->p2.
    """
    dx = p2[0] - p1[0]; dy = p2[1] - p1[1]
    L = math.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    def pt(d):
        return (p1[0] + ux * d, p1[1] + uy * d)
    casts = [(a, b, 'door') for (a, b) in doors] + [(a, b, 'window') for (a, b) in windows]
    cuts = {0.0, L}
    for (a, b, t) in casts:
        cuts.add(max(0.0, min(L, a))); cuts.add(max(0.0, min(L, b)))
    cuts = sorted(cuts)
    sill = z0 + 0.95; head = z0 + 2.35
    idx = 0
    for i in range(len(cuts) - 1):
        a, b = cuts[i], cuts[i + 1]
        if b - a < 1e-4:
            continue
        mid = (a + b) / 2.0
        zranges = [(z0, z1)]
        for (oa, ob_, t) in casts:
            if oa - 1e-6 <= mid <= ob_ + 1e-6:
                zranges = [] if t == 'door' else [(z0, min(sill, z1)), (max(head, z0), z1)]
                break
        for (za, zb) in zranges:
            if zb - za > 1e-4:
                segment("%s_%02d" % (name, idx), pt(a), pt(b), za, zb, thickness, mat, coll)
                idx += 1

def railing(name, p1, p2, z0, z1, mat, coll):
    dx = p2[0] - p1[0]; dy = p2[1] - p1[1]
    L = math.hypot(dx, dy)
    segment(name + "_top", p1, p2, z1 - 0.06, z1, 0.07, mat, coll)
    segment(name + "_mid", p1, p2, (z0 + z1) / 2.0 - 0.03, (z0 + z1) / 2.0 + 0.03, 0.05, mat, coll)
    n = max(2, int(L / 1.2) + 1)
    for i in range(n):
        t = i / (n - 1.0)
        box("%s_post_%02d" % (name, i), (p1[0] + dx * t, p1[1] + dy * t, (z0 + z1) / 2.0),
            (0.07, 0.07, z1 - z0), mat, coll)

# ============================================================== dimensions
W2, D2 = 15.0, 10.0
FH, ST, EXT, INT = 3.2, 0.3, 0.25, 0.15
GF0, GF1 = 0.0, FH
MF0, MF1 = GF1, GF1 + ST
UF0, UF1 = MF1, MF1 + FH
RF0, RF1 = UF1, UF1 + ST

STAIR_X0, STAIR_X1 = 8.4, 13.4
STAIR_Y0, STAIR_Y1 = 3.4, 5.6
OPEN = dict(x0=8.2, x1=13.6, y0=2.8, y1=6.2)

# Pier left between an opening edge and a wall face. Interior walls are INT/2 = 0.075
# from their centreline, so 0.325 keeps openings clear of the walls below.
PIER = 0.325

# ============================================================== slabs + ground
# Ground is a Blender-only backdrop (the exporter drops it by name). Site_Plinth is real
# level geometry: it gives the building an exterior floor, so stepping out of the entrance
# puts the player on a plaza instead of dropping them out of the world.
# Its top sits 0.12 m below the ground floor, a step the mover climbs comfortably.
box("Ground", (0, 0, -0.32), (90, 70, 0.04), M['ground'], C_STRUCT)
box("Site_Plinth", (0, 0, -0.27), (50, 36, 0.30), M['site'], C_STRUCT)
box("Slab_GroundFloor", (0, 0, GF0 - ST / 2), (2 * W2, 2 * D2, ST), M['slab'], C_STRUCT)
box("FloorFinish_Ground", (0, 0, GF0 + 0.01), (2 * W2 - 0.5, 2 * D2 - 0.5, 0.02), M['floor'], C_STRUCT)

# second-floor slab, minus the stair opening
z2 = (MF0 + MF1) / 2.0
box("Slab_Level2_A", (-3.4, 0, z2), (23.2, 20, ST), M['slab'], C_STRUCT)
box("Slab_Level2_B", (14.3, 0, z2), (1.4, 20, ST), M['slab'], C_STRUCT)
box("Slab_Level2_C", (10.9, -3.6, z2), (5.4, 12.8, ST), M['slab'], C_STRUCT)
box("Slab_Level2_D", (10.9, 8.1, z2), (5.4, 3.8, ST), M['slab'], C_STRUCT)
box("FloorFinish_Upper_A", (-3.5, 0, MF1 + 0.01), (23.0, 19.5, 0.02), M['floor'], C_STRUCT)
box("FloorFinish_Upper_B", (14.3, 0, MF1 + 0.01), (1.3, 19.5, 0.02), M['floor'], C_STRUCT)

# ============================================================== exterior walls
# Opening layout is pinned to the interior walls: each opening stops PIER short of the
# nearest wall face (walls are INT thick, so faces sit INT/2 either side of centre), and
# no wall meets a facade inside a window or doorway.
#
#   South GF   divider x=0       entrance opening ends at the divider's west face
#   South UF   glass x=5, x=13   windows stop 0.36 before each glass face
#   North GF   dividers x=-6,1,8 window2 stops before x=1; window3 is split by x=8
#   North UF   dividers x=-4,0,4 window2 stops before x=-4; window3 before x=4
#   E/W   GF   corridor y=+-1.5  windows already clear of the corridor
#   E/W   UF   corridor y=+-1.5  middle window removed (corridor end now solid)
HALF = INT / 2
wall("Ext_South_GF", (-W2, -D2), (W2, -D2), GF0, GF1, EXT, M['ext'], C_STRUCT,
     doors=[(12.0, 15.0 - HALF)],                 # x  -3.000 .. -0.075
     windows=[(3, 7), (19, 25)])                  # x -12..-8, 4..10
wall("Ext_North_GF", (-W2, D2), (W2, D2), GF0, GF1, EXT, M['ext'], C_STRUCT,
     windows=[(4, 8),                             # x -11.0 .. -7.0
              (12, 16 - HALF - PIER),             # x  -3.0 ..  0.6   (divider x=1)
              (20, 23 - HALF - PIER),             # x   5.0 ..  7.6   (divider x=8)
              (23 + HALF + PIER, 26)])            # x   8.4 .. 11.0
wall("Ext_East_GF", (W2, -D2), (W2, D2), GF0, GF1, EXT, M['ext'], C_STRUCT,
     windows=[(3, 7), (12, 17)])                  # y -7..-3, 2..7
wall("Ext_West_GF", (-W2, -D2), (-W2, D2), GF0, GF1, EXT, M['ext'], C_STRUCT,
     windows=[(3, 7), (12, 17)])
# upper floor
wall("Ext_South_UF", (-W2, -D2), (W2, -D2), UF0, UF1, EXT, M['ext'], C_STRUCT,
     windows=[(2, 6), (8, 12),
              (16, 20 - 0.4),                     # x   1.0 ..  4.6   (glass x=5)
              (22, 28 - 0.4)])                    # x   7.0 .. 12.6   (glass x=13)
wall("Ext_North_UF", (-W2, D2), (W2, D2), UF0, UF1, EXT, M['ext'], C_STRUCT,
     windows=[(2, 6),                             # x -13.0 .. -9.0
              (8, 11 - HALF - PIER),              # x  -7.0 .. -4.4   (divider x=-4)
              (16, 19 - HALF - PIER - 0.2),       # x   1.0 ..  3.4   (divider x=4)
              (22, 26)])                          # x   7.0 .. 11.0
wall("Ext_East_UF", (W2, -D2), (W2, D2), UF0, UF1, EXT, M['ext'], C_STRUCT,
     windows=[(2, 6), (14, 18)])                  # y -8..-4, 4..8
wall("Ext_West_UF", (-W2, -D2), (-W2, D2), UF0, UF1, EXT, M['ext'], C_STRUCT,
     windows=[(2, 6), (14, 18)])

# entrance glazing / vestibule frame
box("Entrance_Canopy", (0, -D2 - 0.9, 3.0), (6.0, 1.8, 0.2), M['ext'], C_STRUCT)
box("Entrance_Col_L", (-2.8, -D2 - 1.6, 1.5), (0.25, 0.25, 3.0), M['ext'], C_STRUCT)
box("Entrance_Col_R", (2.8, -D2 - 1.6, 1.5), (0.25, 0.25, 3.0), M['ext'], C_STRUCT)
box("Entrance_Step", (0, -D2 - 0.7, -0.06), (6.0, 1.6, 0.12), M['slab'], C_STRUCT)

# ============================================================== interior walls (ground)
# Corridor walls run the full width so they meet the end facades (no walk-through gap).
wall("GF_Corridor_S", (-W2, -1.5), (W2, -1.5), GF0, GF1, INT, M['wall'], C_WALLS,
     doors=[(4, 7), (19, 22)])
wall("GF_Corridor_N", (-W2, 1.5), (W2, 1.5), GF0, GF1, INT, M['wall'], C_WALLS,
     doors=[(4, 7), (12, 15), (18, 20.5), (24, 27)])
wall("GF_Div_Reception_Workspace", (0, -10), (0, -1.5), GF0, GF1, INT, M['wall'], C_WALLS,
     doors=[(4, 6.5)])
wall("GF_Div_Security_Server", (-6, 1.5), (-6, 10), GF0, GF1, INT, M['wall'], C_WALLS)
wall("GF_Div_Server_Break", (1, 1.5), (1, 10), GF0, GF1, INT, M['wall'], C_WALLS)
wall("GF_Div_Break_Stair", (8, 1.5), (8, 10), GF0, GF1, INT, M['wall'], C_WALLS,
     doors=[(2.5, 4.5)])

# ============================================================== interior walls (upper)
wall("UF_Corridor_S", (-W2, -1.5), (W2, -1.5), UF0, UF1, INT, M['wall'], C_WALLS,
     doors=[(5, 8), (19, 22)])
wall("UF_Corridor_N", (-W2, 1.5), (W2, 1.5), UF0, UF1, INT, M['wall'], C_WALLS,
     doors=[(5, 8), (12, 14), (16, 18), (21, 24)])
wall("UF_Div_Conference_Offices", (-4, 1.5), (-4, 10), UF0, UF1, INT, M['wall'], C_WALLS)
wall("UF_Div_Office_Office", (0, 1.5), (0, 10), UF0, UF1, INT, M['wall'], C_WALLS)
wall("UF_Div_Offices_StairHall", (4, 1.5), (4, 10), UF0, UF1, INT, M['wall'], C_WALLS)
# glass meeting room on the upper open floor
wall("UF_Glass_W", (5, -10), (5, -3.5), UF0, UF1, 0.08, M['glass'], C_WALLS, doors=[(2, 4.5)])
wall("UF_Glass_E", (13, -10), (13, -3.5), UF0, UF1, 0.08, M['glass'], C_WALLS)
wall("UF_Glass_N", (5, -3.5), (13, -3.5), UF0, UF1, 0.08, M['glass'], C_WALLS)

# ============================================================== roof
# The roof deck is also the upper floor's ceiling, so it is concrete like the other
# slabs - not the facade material, which would paint the ceiling with the exterior.
box("Roof_Slab", (0, 0, RF0 + ST / 2), (2 * W2, 2 * D2, ST), M['slab'], C_ROOF)
box("Roof_Parapet", (0, 0, RF1 + 0.25), (2 * W2 + 0.3, 2 * D2 + 0.3, 0.5), M['ext'], C_ROOF)

# ============================================================== stairs
n = 18
rise = 3.5 / n
tread = (STAIR_X1 - STAIR_X0) / n
for i in range(n):
    z_top = (i + 1) * rise
    cx = STAIR_X0 + tread * (i + 0.5)
    box("Stair_Step_%02d" % i, (cx, (STAIR_Y0 + STAIR_Y1) / 2.0, z_top / 2.0),
        (tread, STAIR_Y1 - STAIR_Y0, z_top), M['stairs'], C_STAIRS)
# guard rail around the upper stair opening
railing("UF_Rail_South", (OPEN['x0'], OPEN['y0']), (OPEN['x1'], OPEN['y0']), UF0, UF0 + 1.05, M['rail'], C_STRUCT)
railing("UF_Rail_North", (OPEN['x0'], OPEN['y1']), (OPEN['x1'], OPEN['y1']), UF0, UF0 + 1.05, M['rail'], C_STRUCT)
railing("UF_Rail_West", (OPEN['x0'], OPEN['y0']), (OPEN['x0'], OPEN['y1']), UF0, UF0 + 1.05, M['rail'], C_STRUCT)

# ============================================================== furniture builders
# The chairs are hand-modelled now too - see chair() below, in the imported models
# section, alongside the desks, monitors and their computer kits.

def light(name, loc, energy=250.0):
    ld = bpy.data.lights.new(name, type='POINT')
    ld.energy = energy
    ld.shadow_soft_size = 2.0
    ob = bpy.data.objects.new(name, ld)
    ob.location = loc
    C_LIGHT.objects.link(ob)

def ceiling_panel(name, cx, cy, z):
    box(name, (cx, cy, z), (1.6, 0.5, 0.06), M['panel'], C_LIGHT)
    light(name + "_light", (cx, cy, z - 0.35))

def plant(name, cx, cy, z0):
    box(name + "_pot", (cx, cy, z0 + 0.25), (0.4, 0.4, 0.5), M['dark'], C_FURN)
    box(name + "_leaf", (cx, cy, z0 + 0.85), (0.7, 0.7, 0.9), M['plant'], C_FURN)

# ============================================================== imported models
# Hand-modelled furniture lives in content/models, one origin-centred GLB per asset
# (see tools/export_models.py, and _manifest.json for every asset's footprint). An
# asset is modelled with its origin at the base centre of its own local axes - x across
# its width, -y out of its front face, z up from the floor - so placing one is a
# location and a yaw. No scaling, no offset bookkeeping: the model is the size it is.
#
# The models were scaled up 20% in office_assets.blend (they read small in game next to
# the architecture), so every dimension this file reads off a model is multiplied by
# MODEL_SCALE. The level itself does not scale - rooms, walls and the boxed furniture are
# unchanged - so these constants are what keep the modelled pieces sitting on, and
# standing at the right distance from, the things they belong to.
MODEL_SCALE = 1.2
MODEL_DIR = os.path.join(repo_root(), "content", "models")

def import_model(filename, name, location, rot_z=0.0, collection=None):
    """Import an exported asset and place it, its origin at its base centre."""
    path = os.path.join(MODEL_DIR, filename)
    if not os.path.isfile(path):
        raise RuntimeError("missing %s - run tools/export_models.py first" % path)

    before = set(bpy.data.objects)
    before_mats = set(bpy.data.materials)
    before_imgs = set(bpy.data.images)
    bpy.ops.import_scene.gltf(filepath=path)
    imported = [o for o in bpy.data.objects if o not in before]

    for ob in imported:
        for coll in list(ob.users_collection):
            coll.objects.unlink(ob)
        (collection or C_FURN).objects.link(ob)
        for slot in ob.material_slots:
            mat = slot.material
            if mat is None:
                continue
            # The importer has already suffixed a clashing name ("Table_Dark.001"), so
            # compare on the stem.
            stem = re.sub(r"\.\d+$", "", mat.name)
            replacement = MATERIALS_BY_NAME.get(stem) or IMPORTED_MATERIALS.get(stem)
            if replacement is not None:
                slot.material = replacement
            else:
                IMPORTED_MATERIALS[stem] = mat

    # The import's own copies of those materials (and their packed textures) are now
    # unreferenced: drop them so the level keeps one material per look.
    for mat in [m for m in bpy.data.materials if m not in before_mats and m.users == 0]:
        bpy.data.materials.remove(mat)
    for img in [i for i in bpy.data.images if i not in before_imgs and i.users == 0]:
        bpy.data.images.remove(img)

    root = next(o for o in imported if o.parent is None)
    root.name = name
    root.location = location
    # The glTF importer leaves the root in QUATERNION rotation mode, where setting
    # rotation_euler does nothing at all. Switch mode first, or a yaw is silently lost.
    root.rotation_mode = 'XYZ'
    root.rotation_euler = (0.0, 0.0, rot_z)
    IMPORTED.extend(imported)
    return root

# Writable worktop height of the modelled standing desk, so a kit can be dropped on it.
DESK_TOP = 0.74 * MODEL_SCALE

COMPUTERS = {
    "single": "workstation_single.glb",
    "wide":   "workstation_wide.glb",
    "laptop": "laptop_setup.glb",
}

def computer(name, cx, cy, z, kit="single", yaw=0.0):
    """A computer arrangement from content/models, sitting on a worktop at z.

    Every kit is modelled with its screens facing -y, so `yaw` is what turns it to
    face its user: 0 for a user on the -y side of the desk, pi for a user on the +y
    side. The kit's origin is its base centre, so that is the whole placement.
    """
    return import_model(COMPUTERS[kit], name, (cx, cy, z), rot_z=yaw)

def workstation(name, cx, cy, z0=0.0, kit="single", yaw=0.0, user_dy=0.75 * MODEL_SCALE):
    """A standing desk from content/models with a computer kit on it and a chair at it.

    The desk is the hand-modelled Desk_Standing - 1.40 x 0.70 m with its worktop at
    0.74 m before scaling, so 1.68 x 0.84 m and 0.888 m as it stands - the same materials
    as the rest of the level, so it replaces the boxed desk one for one.

    `user_dy` is where the chair goes relative to the desk centre: positive puts the
    user on the +y side (the workspace rows), negative on the -y side (the two offices).
    It rides on MODEL_SCALE so the chair keeps about the same clearance in front of a
    bigger desk, which is also where the modelled chairs will want it. The desk and the
    kit turn to face whoever is sitting there, and `yaw` adds a small desk-by-desk
    variation to that.
    """
    facing = math.pi if user_dy >= 0 else 0.0
    import_model("desk_standing.glb", name + "_desk", (cx, cy, z0), rot_z=facing)
    computer(name + "_" + kit, cx, cy, z0 + DESK_TOP, kit, yaw=facing + yaw)
    # The chair stands where its user does, so it looks the other way - at the desk.
    chair(name + "_chair", cx, cy + user_dy, yaw=facing + math.pi, z0=z0)

def chair(name, cx, cy, yaw=0.0, z0=0.0):
    """An office chair from content/models, placed by the middle of its seat.

    The model is OfficeChair - 0.775 x 0.815 m over its casters and 1.216 m to the top of
    the backrest, seat at 0.61 m - so it is the tallest thing in the level and the most
    detailed asset in the set, at 5,724 triangles. Its base is a five-star, which is why
    its bounding box is asymmetric: the casters reach 0.42 m one way and 0.35 m the other
    while the seat itself is centred on the origin.

    It is modelled with its backrest on +y and its front face (the way the sitter looks)
    on -y, so `yaw` 0 leaves it facing -y, as `import_model` does for everything else.
    Chair call sites read that way: a desk chair gets the desk's facing plus pi, a chair
    at the +y side of a table gets 0, and the security pair turn to face their console.
    """
    return import_model("office_chair.glb", name, (cx, cy, z0), rot_z=yaw)

# Rack units are 44.45 mm, and the 42U opening of the modelled rack starts 0.09 m above
# its base - both measured off the model as first built, so both ride on MODEL_SCALE (42 x
# 44.45 = 1.8669 m of opening, matching the mesh). Servers sit in the front half of the
# rack, 0.0945 m from its centre before scaling, which is where the reference instances in
# office_assets.blend sit and puts their fronts 5 cm behind the rack's.
RACK_U = 0.04445 * MODEL_SCALE
RACK_RAIL_Z = 0.09 * MODEL_SCALE
RACK_UNITS = 42
SERVER_SETBACK = 0.0945 * MODEL_SCALE

SERVERS = {1: "server_1u.glb", 2: "server_2u.glb", 3: "server_3u.glb"}


def rack_load(index, bulk=False):
    """The servers in one rack, as (unit, size in U) with unit 1 at the bottom.

    Blocks of servers with gaps between them - the way a working rack is loaded rather
    than a full cabinet - and no two racks alike: the size and the gap both shift as you
    walk up the rack, and each rack starts one step further into the sequence than the
    last. The bulk racks (the room's north row) run to 3U storage boxes, the rest to 1U
    and 2U compute.
    """
    sizes = (1, 2, 1, 1, 3, 1, 2, 1) if bulk else (1, 1, 2, 1, 1, 3, 1, 1)
    units, unit = [], 1 + index % 4
    for n, size in enumerate(sizes):
        if unit + size - 1 > RACK_UNITS:
            break
        units.append((unit, size))
        unit += size + 2 + (index + n) % 3      # 2-4 empty units of airflow between them
    return units


def server_rack(name, cx, cy, yaw=0.0, load=(), z0=0.0):
    """A 42U rack from content/models with an assortment of servers stacked in it.

    `load` is (unit, size) pairs; a server of size u fills units unit .. unit+u-1,
    counting up from the bottom of the opening. Placement is a location and a yaw for
    the rack, and the servers follow it: same footprint, same facing, one rack unit
    higher per unit. The front the servers face is the rack's own -y, so `yaw` turns the
    whole loaded rack to face its aisle.
    """
    import_model("server_rack_42u.glb", name, (cx, cy, z0), rot_z=yaw)
    dx, dy = math.sin(yaw), -math.cos(yaw)      # the rack's local -y, in world terms
    for unit, size in load:
        import_model(SERVERS[size], "%s_U%02d" % (name, unit),
                     (cx + dx * SERVER_SETBACK, cy + dy * SERVER_SETBACK,
                      z0 + RACK_RAIL_Z + (unit - 1) * RACK_U), rot_z=yaw)

# ============================================================== GROUND: reception + entry
# The counter is the hand-modelled reception desk: 2.48 x 0.94 m and 1.075 m high before
# scaling, so 2.976 x 1.128 m and 1.29 m as it stands. Origin at its base centre, front
# face facing -Y (the entrance), transaction shelf at the front. It replaces the boxed L
# that used to run 6.00 m along x here and 2.70 m back along y at x -7, so it is centred
# on the old counter's main line and its front face stays flush with the old counter front
# at y -5.20. The front face is 0.48 m out from the model's origin, hence the placement.
COUNTER_LINE = -5.20
import_model("reception_desk.glb", "Reception_Counter",
             (-4.0, COUNTER_LINE + 0.48 * MODEL_SCALE, 0.0))
box("Reception_Sign_Panel", (-13.7, -5.75, 2.1), (0.15, 3.2, 0.9), M['accent'], C_FURN)
# Behind the counter, facing it (the chair's default -y). The counter's back face lands at
# y -4.072; the chair's casters reach y -3.96 and its seat front -3.83, so it clears the
# counter by 0.11 m with the seat tucking under it.
chair("Reception_Chair", -4.0, -3.55)
# waiting area
box("Waiting_Sofa_Base", (-11.5, -8.6, 0.28), (2.2, 0.85, 0.35), M['leather'], C_FURN)
box("Waiting_Sofa_Back", (-11.5, -9.0, 0.62), (2.2, 0.2, 0.75), M['leather'], C_FURN)
# West of the entrance doorway (Ext_South_GF opens x -3.0 .. -0.075), not in it: the
# plant used to stand at x -2.2 and blocked the way out.
plant("Reception_Plant", -4.3, -9.3, 0.0)
plant("Reception_Plant2", -13.2, -9.0, 0.0)

# ============================================================== GROUND: workspace
# Standing desks with an assortment of computer kits on top. The kits are ordered so no
# two neighbours repeat, and the laptops sit a little off-square off the way they do in
# a real office. The user sits on the +y side, so desk and kit face that way.
GROUND_KITS = ("wide", "laptop", "single", "wide", "laptop", "single")
for i, x in enumerate([3.0, 7.0, 11.0]):
    for j, y in enumerate([-8.5, -5.5]):
        kit = GROUND_KITS[i * 2 + j]
        yaw = (0.13 if (i + j) % 2 else -0.09) if kit == "laptop" else 0.0
        workstation("WS_%d%d" % (i, j), x, y, kit=kit, yaw=yaw)
plant("WS_Plant", 13.2, -2.6, 0.0)

# ============================================================== GROUND: security room
box("Security_Monitor_Wall", (-13.85, 5.75, 2.1), (0.1, 6.4, 2.4), M['dark'], C_FURN)
for r, z in enumerate([1.75, 2.45]):
    for c, y in enumerate([3.0, 4.8, 6.6, 8.4]):
        box("Security_Screen_%d%d" % (r, c), (-13.7, y, z), (0.06, 1.5, 0.55), M['screen'], C_FURN)
box("Security_Console", (-11.6, 5.75, 0.38), (1.8, 5.0, 0.76), M['dark'], C_FURN)
box("Security_Console_Top", (-11.6, 5.75, 0.78), (1.9, 5.2, 0.06), M['wood2'], C_FURN)
# Both face their console (-x), which the boxed stand-in could not do: it could only turn
# in y, so it sat side-on to its own desk. x -10.2 is as far west as the chair can go
# before its casters reach into the console body, whose east face is at x -10.7.
chair("Security_Chair_1", -10.2, 4.4, yaw=-math.pi / 2)
chair("Security_Chair_2", -10.2, 7.1, yaw=-math.pi / 2)
# Cabinet sits in the solid flank between the north window and the server divider.
box("Security_Server", (-6.5, 9.2, 0.9), (0.7, 0.8, 1.8), M['server'], C_FURN)

# ============================================================== GROUND: server room
# Two rows of hand-modelled 42U racks, facing each other across the aisle and each loaded
# with an assortment of the modelled servers. The old boxed racks' LED strip and lid go
# with them: the rack model brings its own top, and the lights in a rack are its servers'.
for r, y in enumerate([4.5, 8.0]):
    for c, x in enumerate([-5.0, -3.5, -2.0, -0.5]):
        server_rack("Rack_%d%d" % (r, c), x, y,
                    yaw=0.0 if r else math.pi,   # the south row faces the aisle to its north
                    load=rack_load(r * 4 + c, bulk=bool(r)))
box("Server_Room_AC", (-5.5, 9.5, 1.4), (1.0, 0.6, 2.8), M['metal'], C_FURN)

# ============================================================== GROUND: break room
box("Break_Counter", (4.5, 9.55, 0.45), (5.5, 0.7, 0.9), M['accent'], C_FURN)
box("Break_Counter_Top", (4.5, 9.55, 0.92), (5.6, 0.8, 0.06), M['wood2'], C_FURN)
box("Break_Table", (4.5, 5.5, 0.74), (2.4, 1.2, 0.06), M['wood'], C_FURN)
box("Break_Table_Leg", (4.5, 5.5, 0.37), (0.15, 0.15, 0.74), M['metal'], C_FURN)
for k, (dx, dy) in enumerate(((-0.9, -0.9), (0.9, -0.9), (-0.9, 0.9), (0.9, 0.9))):
    chair("Break_Chair_%d" % k, 4.5 + dx, 5.5 + dy, yaw=0.0 if dy > 0 else math.pi)

# ============================================================== GROUND: stairwell rails
railing("STAIR_Rail_Outer", (STAIR_X0, STAIR_Y1 + 0.12), (STAIR_X1, STAIR_Y1 + 0.12), GF0, GF0 + 1.0, M['rail'], C_STAIRS)
railing("STAIR_Rail_Inner", (STAIR_X0, STAIR_Y0 - 0.12), (STAIR_X1, STAIR_Y0 - 0.12), GF0, GF0 + 1.0, M['rail'], C_STAIRS)

# ============================================================== UPPER: open workspace
# The same desks and kits, phased differently so the two floors do not look like the
# same room twice.
UPPER_KITS = ("single", "wide", "laptop", "single", "laptop", "wide")
for i, x in enumerate([-11.0, -7.0, -3.0]):
    for j, y in enumerate([-8.5, -5.5]):
        kit = UPPER_KITS[i * 2 + j]
        yaw = (0.11 if (i + j) % 2 else -0.13) if kit == "laptop" else 0.0
        workstation("UF_WS_%d%d" % (i, j), x, y, z0=UF0, kit=kit, yaw=yaw)
plant("UF_WS_Plant", -13.0, -2.6, UF0)

# ============================================================== UPPER: glass meeting room interior
box("Meet_Table", (9.0, -6.75, UF0 + 0.74), (2.6, 4.0, 0.08), M['wood'], C_FURN)
box("Meet_Table_Leg", (9.0, -6.75, UF0 + 0.37), (0.2, 0.2, 0.74), M['metal'], C_FURN)
for k, (dx, dy) in enumerate(((-1.6, -1.4), (1.6, -1.4), (-1.6, 1.4), (1.6, 1.4))):
    chair("Meet_Chair_%d" % k, 9.0 + dx, -6.75 + dy, yaw=0.0 if dy > 0 else math.pi, z0=UF0)

# ============================================================== UPPER: conference room
box("Conf_Table", (-9.0, 5.75, UF0 + 0.74), (6.0, 2.0, 0.1), M['wood2'], C_FURN)
box("Conf_Table_Leg", (-9.0, 5.75, UF0 + 0.37), (0.25, 0.25, 0.74), M['metal'], C_FURN)
for k, (dx, dy) in enumerate(((-2.4, -1.5), (-0.8, -1.5), (0.8, -1.5), (2.4, -1.5),
                              (-2.4, 1.5), (-0.8, 1.5), (0.8, 1.5), (2.4, 1.5))):
    chair("Conf_Chair_%d" % k, -9.0 + dx, 5.75 + dy, yaw=0.0 if dy > 0 else math.pi, z0=UF0)
box("Conf_Screen", (-13.8, 5.75, UF0 + 2.0), (0.1, 3.0, 1.6), M['screen'], C_FURN)

# ============================================================== UPPER: offices
# Same standing desks, but here the user sits on the -y side of the desk, so the desk,
# the kit and the chair all face the other way. The chairs ride the same MODEL_SCALE
# stand-off as the workspace rows.
workstation("Office_West", -2.0, 4.0, z0=UF0, kit="wide", user_dy=-1.0 * MODEL_SCALE)
workstation("Office_East", 2.0, 4.0, z0=UF0, kit="laptop", user_dy=-1.0 * MODEL_SCALE, yaw=-0.11)
box("Office_West_Shelf", (-3.7, 8.5, UF0 + 0.9), (0.4, 2.0, 1.8), M['wood2'], C_FURN)
box("Office_East_Shelf", (3.7, 8.5, UF0 + 0.9), (0.4, 2.0, 1.8), M['wood2'], C_FURN)

# ============================================================== ceiling lights
for nm, x, y in [("Reception", -7.0, -5.75), ("Workspace", 7.0, -5.75),
                 ("Security", -10.0, 5.75), ("Server", -2.5, 5.75),
                 ("Break", 4.5, 5.75), ("Stairwell", 11.0, 5.75),
                 ("Corridor", 0.0, 0.0)]:
    ceiling_panel("Ceil_GF_" + nm, x, y, GF1 - 0.08)
for nm, x, y in [("Conference", -9.0, 5.75), ("OfficeW", -2.0, 5.75), ("OfficeE", 2.0, 5.75),
                 ("UpperWorkspace", -4.0, -5.75), ("Meeting", 9.0, -6.75),
                 ("StairHall", 9.0, 8.5), ("CorridorUp", 0.0, 0.0)]:
    ceiling_panel("Ceil_UF_" + nm, x, y, UF1 - 0.08)

# sun
sd = bpy.data.lights.new("Sun", type='SUN')
sd.energy = 2.5
so = bpy.data.objects.new("Sun", sd)
so.location = (25, -20, 30)
so.rotation_euler = (math.radians(55), 0.0, math.radians(35))
C_LIGHT.objects.link(so)

# world
world = scene.world or bpy.data.worlds.new("World")
scene.world = world
world.use_nodes = True
bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
bg.inputs[0].default_value = (0.04, 0.05, 0.07, 1.0)

# ============================================================== inspection cameras
def make_cam(name, loc, target, lens=24.0):
    cd = bpy.data.cameras.new(name)
    cd.lens = lens
    cam = bpy.data.objects.new(name, cd)
    cam.location = Vector(loc)
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    scene.collection.objects.link(cam)
    return cam

make_cam("Cam_Reception", (-0.5, -9.3, 1.65), (-8.0, -4.2, 1.2), 22)
make_cam("Cam_Corridor", (-13.6, 0.0, 1.6), (14.0, 0.0, 1.5), 18)
make_cam("Cam_UpperWorkspace", (-13.2, -8.8, 5.2), (5.0, -3.0, 4.2), 22)

# ============================================================== UVs + save
texture_uvs()

bpy.ops.object.select_all(action='DESELECT')

save_path = os.environ.get("OFFICE_BLEND_PATH", os.path.expanduser("~/Work/office-level.blend"))
os.makedirs(os.path.dirname(save_path), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=save_path)

mesh_count = sum(1 for o in bpy.data.objects if o.type == 'MESH')
print("BUILD_OK meshes=%d lights=%d cameras=%d saved=%s" % (
    mesh_count,
    sum(1 for o in bpy.data.objects if o.type == 'LIGHT'),
    sum(1 for o in bpy.data.objects if o.type == 'CAMERA'),
    save_path))
