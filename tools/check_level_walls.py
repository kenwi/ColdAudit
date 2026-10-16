"""Sanity-check the office level shell for facade openings and wall alignment.

Run under Blender against the level .blend (or any .blend with ``Ext_*`` facades):

    blender --background ~/Work/office-level.blend --python tools/check_level_walls.py

Checks, per facade and storey:

  * full-height segments tile the run with no overlaps, so the only gaps are
    designed openings (a window shows a sill + matching header, a door shows
    neither)
  * every window sill has a header with the same span
  * no wall or prop terminates inside a window opening

Exits non-zero when a problem is found.
"""

import sys

import bpy
from mathutils import Vector

FLOOR_TOL = 0.06  # storey match tolerance (m)
NEAR = 0.7        # how close to a facade counts as "against the facade" (m)
MIN_OVERLAP = 0.03  # u/height overlap before it counts as intruding (m)
EXT_HALF = 0.125  # half of the exterior wall thickness

FACADES = {
    "South": dict(prefix="Ext_South", u="x", perp="y", side=-1.0),
    "North": dict(prefix="Ext_North", u="x", perp="y", side=1.0),
    "West": dict(prefix="Ext_West", u="y", perp="x", side=-1.0),
    "East": dict(prefix="Ext_East", u="y", perp="x", side=1.0),
}


class Box:
    __slots__ = ("name", "collections", "lo", "hi", "perp_lo", "perp_hi", "z0", "z1")

    def __init__(self, obj, u_axis, perp_axis):
        corners = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
        self.name = obj.name
        self.collections = ",".join(c.name for c in obj.users_collection)
        self.lo = min(getattr(c, u_axis) for c in corners)
        self.hi = max(getattr(c, u_axis) for c in corners)
        self.perp_lo = min(getattr(c, perp_axis) for c in corners)
        self.perp_hi = max(getattr(c, perp_axis) for c in corners)
        self.z0 = min(c.z for c in corners)
        self.z1 = max(c.z for c in corners)


def collect():
    out = []
    for obj in bpy.context.scene.objects:
        if obj.type != "MESH":
            continue
        out.append(obj)
    return out


def analyse(name, cfg, objects):
    u_axis, perp_axis = cfg["u"], cfg["perp"]
    boxes = [Box(o, u_axis, perp_axis) for o in objects]
    segs = [b for b in boxes if b.name.startswith(cfg["prefix"])]
    problems = []
    inventory = []

    if not segs:
        return [f"{name}: no {cfg['prefix']}* segments found"], inventory

    umin = min(b.lo for b in segs)
    umax = max(b.hi for b in segs)
    perp_center = sum((b.perp_lo + b.perp_hi) / 2.0 for b in segs) / len(segs)
    perp_inner = perp_center - cfg["side"] * EXT_HALF
    # A band just inside the facade; slightly into the wall itself so a wall meeting
    # the facade head-on is included.
    near_lo = perp_inner - (0.3 if cfg["side"] < 0 else NEAR)
    near_hi = perp_inner + (NEAR if cfg["side"] < 0 else 0.3)

    # A storey is found from its full-height runs: segments spanning >= 2 m of height.
    floors = sorted({round(b.z0, 2) for b in segs if b.z1 - b.z0 >= 2.0})
    if not floors:
        return [f"{name}: no full-height {cfg['prefix']}* segments found"], inventory

    for storey in floors:
        top = max(b.z1 for b in segs if abs(b.z0 - storey) < FLOOR_TOL)
        band = [b for b in segs if b.z0 >= storey - FLOOR_TOL and b.z1 <= top + FLOOR_TOL]
        full = sorted([b for b in band
                       if abs(b.z0 - storey) < FLOOR_TOL and abs(b.z1 - top) < FLOOR_TOL],
                      key=lambda b: b.lo)
        sills = sorted([b for b in band
                        if abs(b.z0 - storey) < FLOOR_TOL and b.z1 < top - 0.5],
                       key=lambda b: b.lo)
        heads = sorted([b for b in band
                        if abs(b.z1 - top) < FLOOR_TOL and b.z0 > storey + 0.5],
                       key=lambda b: b.lo)

        # 1. full-height segments tile the run; each gap is a window or a door
        cursor = umin
        for b in full:
            if b.lo > cursor + FLOOR_TOL:
                u0, u1 = cursor, b.lo
                is_win = any(abs(s.lo - u0) < 0.05 and abs(s.hi - u1) < 0.05 for s in sills)
                inventory.append((name, storey, "window" if is_win else "door/opening",
                                  round(u0, 2), round(u1, 2)))
            elif b.lo < cursor - FLOOR_TOL:
                problems.append(
                    f"{name} z{storey}: full-height segments overlap at u={b.lo:.2f} "
                    f"({b.name} starts before the run returns to {cursor:.2f})")
            cursor = max(cursor, b.hi)
        if cursor < umax - FLOOR_TOL:
            inventory.append((name, storey, "door/opening", round(cursor, 2), round(umax, 2)))

        # 2. sills pair with headers, and nothing sits inside the resulting void
        for s in sills:
            match = [h for h in heads if abs(h.lo - s.lo) < 1e-3 and abs(h.hi - s.hi) < 1e-3]
            if not match:
                problems.append(
                    f"{name} z{storey}: sill {s.name} u[{s.lo:.2f},{s.hi:.2f}] "
                    f"Z[{s.z0:.2f},{s.z1:.2f}] has no matching header")
                continue
            for h in match:
                void_u0, void_u1 = s.lo, s.hi
                void_z0, void_z1 = s.z1, h.z0
                for b in boxes:
                    if b.name.startswith(cfg["prefix"]) or b.name == "Ground":
                        continue
                    if b.perp_hi < near_lo or b.perp_lo > near_hi:
                        continue
                    u_overlap = min(b.hi, void_u1) - max(b.lo, void_u0)
                    z_overlap = min(b.z1, void_z1) - max(b.z0, void_z0)
                    if u_overlap > MIN_OVERLAP and z_overlap > MIN_OVERLAP:
                        problems.append(
                            f"{name} z{storey}: {b.collections or '?'} object {b.name} "
                            f"intrudes into window u[{void_u0:.2f},{void_u1:.2f}] "
                            f"Z[{void_z0:.2f},{void_z1:.2f}] "
                            f"(overlap {u_overlap:.2f} x {z_overlap:.2f})")

    return problems, inventory


def main():
    objects = collect()
    problems = []
    inventory = []
    for name, cfg in FACADES.items():
        p, i = analyse(name, cfg, objects)
        problems += p
        inventory += i

    print("=== facade openings ===")
    for facade, storey, kind, u0, u1 in inventory:
        print(f"  {facade:6} z{storey:.1f}  {kind:13} u[{u0:7.2f},{u1:7.2f}]  width={u1 - u0:.2f}")

    print("\n=== problems ===")
    if not problems:
        print("  none")
    for p in problems:
        print("  - " + p)

    print(f"\n{'LEVEL SHELL OK' if not problems else f'{len(problems)} PROBLEM(S)'}")
    sys.exit(1 if problems else 0)


main()
