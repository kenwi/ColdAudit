"""Build the ColdAudit office level from scratch.

This is the source of truth for the level geometry (the .blend contains no embedded
scripts). It clears the scene, rebuilds the two-storey office, and optionally saves
the .blend. Run it with the exporter in one Blender session:

    blender --background \
        --python tools/build_office_level.py \
        --python tools/export_level_glb.py

Save path defaults to ~/Work/office-level.blend; override with OFFICE_BLEND_PATH.

Facade openings are aligned to the interior walls: every opening sits fully inside a
bay, with a solid pier beside any wall that meets the facade, so no wall terminates
inside a window or doorway.
"""

import bpy, bmesh, math, os
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
def desk(name, cx, cy, w=1.6, d=0.8, mat=None, z0=0.0):
    m = mat or M['wood']
    box(name + "_top", (cx, cy, z0 + 0.74), (w, d, 0.04), m, C_FURN)
    for k, (sx, sy) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
        box("%s_leg%d" % (name, k), (cx + sx * (w / 2 - 0.06), cy + sy * (d / 2 - 0.06), z0 + 0.36),
            (0.05, 0.05, 0.72), M['metal'], C_FURN)

def chair(name, cx, cy, back_dy=0.2, z0=0.0):
    box(name + "_seat", (cx, cy, z0 + 0.45), (0.44, 0.44, 0.06), M['leather'], C_FURN)
    box(name + "_back", (cx, cy + back_dy, z0 + 0.72), (0.44, 0.06, 0.5), M['leather'], C_FURN)
    box(name + "_pole", (cx, cy, z0 + 0.22), (0.06, 0.06, 0.42), M['metal'], C_FURN)
    box(name + "_base", (cx, cy, z0 + 0.03), (0.4, 0.4, 0.04), M['metal'], C_FURN)

def monitor(name, cx, cy, z0=0.0):
    box(name + "_foot", (cx, cy, z0 + 0.78), (0.18, 0.14, 0.04), M['dark'], C_FURN)
    box(name + "_pole", (cx, cy, z0 + 0.88), (0.04, 0.04, 0.2), M['dark'], C_FURN)
    box(name + "_panel", (cx, cy, z0 + 1.06), (0.54, 0.04, 0.34), M['screen'], C_FURN)

def workstation(name, cx, cy, z0=0.0):
    desk(name + "_desk", cx, cy, z0=z0)
    monitor(name + "_monitor", cx, cy - 0.18, z0=z0)
    chair(name + "_chair", cx, cy + 0.75, back_dy=0.22, z0=z0)

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

# ============================================================== GROUND: reception + entry
box("Reception_Counter_A", (-4.0, -4.85, 0.55), (6.0, 0.7, 1.1), M['wood2'], C_FURN)
box("Reception_Counter_B", (-7.0, -6.2, 0.55), (0.7, 2.7, 1.1), M['wood2'], C_FURN)
box("Reception_Counter_Top", (-4.0, -4.85, 1.11), (6.2, 0.8, 0.06), M['accent'], C_FURN)
box("Reception_Counter_TopB", (-7.0, -6.2, 1.11), (0.8, 2.9, 0.06), M['accent'], C_FURN)
box("Reception_Sign_Panel", (-13.7, -5.75, 2.1), (0.15, 3.2, 0.9), M['accent'], C_FURN)
chair("Reception_Chair", -5.5, -3.9, back_dy=0.22)
# waiting area
box("Waiting_Sofa_Base", (-11.5, -8.6, 0.28), (2.2, 0.85, 0.35), M['leather'], C_FURN)
box("Waiting_Sofa_Back", (-11.5, -9.0, 0.62), (2.2, 0.2, 0.75), M['leather'], C_FURN)
# West of the entrance doorway (Ext_South_GF opens x -3.0 .. -0.075), not in it: the
# plant used to stand at x -2.2 and blocked the way out.
plant("Reception_Plant", -4.3, -9.3, 0.0)
plant("Reception_Plant2", -13.2, -9.0, 0.0)

# ============================================================== GROUND: workspace
for i, x in enumerate([3.0, 7.0, 11.0]):
    for j, y in enumerate([-8.5, -5.5]):
        workstation("WS_%d%d" % (i, j), x, y)
plant("WS_Plant", 13.2, -2.6, 0.0)

# ============================================================== GROUND: security room
box("Security_Monitor_Wall", (-13.85, 5.75, 2.1), (0.1, 6.4, 2.4), M['dark'], C_FURN)
for r, z in enumerate([1.75, 2.45]):
    for c, y in enumerate([3.0, 4.8, 6.6, 8.4]):
        box("Security_Screen_%d%d" % (r, c), (-13.7, y, z), (0.06, 1.5, 0.55), M['screen'], C_FURN)
box("Security_Console", (-11.6, 5.75, 0.38), (1.8, 5.0, 0.76), M['dark'], C_FURN)
box("Security_Console_Top", (-11.6, 5.75, 0.78), (1.9, 5.2, 0.06), M['wood2'], C_FURN)
chair("Security_Chair_1", -10.4, 4.4, back_dy=-0.22)
chair("Security_Chair_2", -10.4, 7.1, back_dy=-0.22)
# Cabinet sits in the solid flank between the north window and the server divider.
box("Security_Server", (-6.5, 9.2, 0.9), (0.7, 0.8, 1.8), M['server'], C_FURN)

# ============================================================== GROUND: server room
for r, y in enumerate([4.5, 8.0]):
    for c, x in enumerate([-5.0, -3.5, -2.0, -0.5]):
        nm = "Rack_%d%d" % (r, c)
        box(nm, (x, y, 1.0), (0.6, 1.0, 2.0), M['server'], C_FURN)
        box(nm + "_led", (x - 0.31, y, 1.55), (0.02, 0.85, 0.08), M['led'], C_FURN)
        box(nm + "_top", (x, y, 2.03), (0.65, 1.05, 0.06), M['metal'], C_FURN)
box("Server_Room_AC", (-5.5, 9.5, 1.4), (1.0, 0.6, 2.8), M['metal'], C_FURN)

# ============================================================== GROUND: break room
box("Break_Counter", (4.5, 9.55, 0.45), (5.5, 0.7, 0.9), M['accent'], C_FURN)
box("Break_Counter_Top", (4.5, 9.55, 0.92), (5.6, 0.8, 0.06), M['wood2'], C_FURN)
box("Break_Table", (4.5, 5.5, 0.74), (2.4, 1.2, 0.06), M['wood'], C_FURN)
box("Break_Table_Leg", (4.5, 5.5, 0.37), (0.15, 0.15, 0.74), M['metal'], C_FURN)
for k, (dx, dy) in enumerate(((-0.9, -0.9), (0.9, -0.9), (-0.9, 0.9), (0.9, 0.9))):
    chair("Break_Chair_%d" % k, 4.5 + dx, 5.5 + dy, back_dy=0.22 if dy > 0 else -0.22)

# ============================================================== GROUND: stairwell rails
railing("STAIR_Rail_Outer", (STAIR_X0, STAIR_Y1 + 0.12), (STAIR_X1, STAIR_Y1 + 0.12), GF0, GF0 + 1.0, M['rail'], C_STAIRS)
railing("STAIR_Rail_Inner", (STAIR_X0, STAIR_Y0 - 0.12), (STAIR_X1, STAIR_Y0 - 0.12), GF0, GF0 + 1.0, M['rail'], C_STAIRS)

# ============================================================== UPPER: open workspace
for i, x in enumerate([-11.0, -7.0, -3.0]):
    for j, y in enumerate([-8.5, -5.5]):
        workstation("UF_WS_%d%d" % (i, j), x, y, z0=UF0)
plant("UF_WS_Plant", -13.0, -2.6, UF0)

# ============================================================== UPPER: glass meeting room interior
box("Meet_Table", (9.0, -6.75, UF0 + 0.74), (2.6, 4.0, 0.08), M['wood'], C_FURN)
box("Meet_Table_Leg", (9.0, -6.75, UF0 + 0.37), (0.2, 0.2, 0.74), M['metal'], C_FURN)
for k, (dx, dy) in enumerate(((-1.6, -1.4), (1.6, -1.4), (-1.6, 1.4), (1.6, 1.4))):
    chair("Meet_Chair_%d" % k, 9.0 + dx, -6.75 + dy, back_dy=0.22 if dy > 0 else -0.22, z0=UF0)

# ============================================================== UPPER: conference room
box("Conf_Table", (-9.0, 5.75, UF0 + 0.74), (6.0, 2.0, 0.1), M['wood2'], C_FURN)
box("Conf_Table_Leg", (-9.0, 5.75, UF0 + 0.37), (0.25, 0.25, 0.74), M['metal'], C_FURN)
for k, (dx, dy) in enumerate(((-2.4, -1.5), (-0.8, -1.5), (0.8, -1.5), (2.4, -1.5),
                              (-2.4, 1.5), (-0.8, 1.5), (0.8, 1.5), (2.4, 1.5))):
    chair("Conf_Chair_%d" % k, -9.0 + dx, 5.75 + dy, back_dy=0.22 if dy > 0 else -0.22, z0=UF0)
box("Conf_Screen", (-13.8, 5.75, UF0 + 2.0), (0.1, 3.0, 1.6), M['screen'], C_FURN)

# ============================================================== UPPER: offices
desk("Office_West_Desk", -2.0, 4.0, 1.8, 0.9, z0=UF0)
chair("Office_West_Chair", -2.0, 3.0, back_dy=-0.22, z0=UF0)
monitor("Office_West_Monitor", -2.4, 3.85, z0=UF0)
desk("Office_East_Desk", 2.0, 4.0, 1.8, 0.9, z0=UF0)
chair("Office_East_Chair", 2.0, 3.0, back_dy=-0.22, z0=UF0)
monitor("Office_East_Monitor", 1.6, 3.85, z0=UF0)
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
