"""Scale every modelled asset, and the showreel rig, by a factor - in place.

This is what changed the models on 2026-10-19: they were **scaled up 20%** (`--factor 1.2`)
because they read small in game next to the architecture. Geometry *and* UVs are scaled,
so the models get bigger while their textures keep their real-world size; that matters,
because the level's own box-projected geometry uses the same per-material metre spans, so
model and level surfaces stay consistent. Leaving the UVs alone would stretch every
texture by the factor instead.

The whole asset scene scales, not just the meshes: the review row's placements, the light
rig (position, size, and energy by factor-squared, so the illumination is unchanged), the
backdrop cove, and the showreel camera's path, focus distance and clip range. That keeps
the row in proportion and the showreel framing identical. The camera's rotation is
constant, so its rotation curves are left alone.

Two things must follow a run, or the level will not match the models:

 1. The level builder's `MODEL_SCALE` constant must be set to the same factor - it is
    what turns the dimensions the level reads off a model (desk worktop, rack rail
    height and unit pitch, the reception counter's front face) into placements.
 2. Re-export the models and rebuild the level:
        blender -b ~/Work/office_assets.blend -P tools/export_models.py
        blender --background --factory-startup --python tools/build_office_level.py \
                --python tools/export_level_glb.py

Run:

    blender -b ~/Work/office_assets.blend -P tools/scale_assets.py -- --factor 1.2

The factor is deliberately compulsory and has no default: running this twice with 1.2
scales by 1.44. Back up the `.blend` and note the old `MODEL_SCALE` before you run it.
"""

import sys

import bpy

S = None
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
for i, a in enumerate(argv):
    if a == "--factor" and i + 1 < len(argv):
        S = float(argv[i + 1])
    elif a.startswith("--factor="):
        S = float(a.split("=", 1)[1])
if S is None or S <= 0.0:
    print("scale_assets: nothing was changed")
    raise RuntimeError("scale_assets: --factor <n> is required, e.g. -- --factor 1.2")

PROBES = ("Desk_Standing", "ServerRack_42U", "Workstation_Wide", "ReceptionDesk",
          "Server_1U", "BG_Cove")


def local_size(obj):
    """Dimensions from the raw vertices - obj.dimensions reads stale after a mesh edit."""
    co = [v.co for v in obj.data.vertices]
    return tuple(max(c[i] for c in co) - min(c[i] for c in co) for i in range(3))


def fcurves(action):
    """F-curves of an action, 4.4+/5.x (no .fcurves) or older."""
    if hasattr(action, "fcurves"):
        return list(action.fcurves)
    out = []
    for layer in action.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                out.extend(bag.fcurves)
    return out


def actions_of(obj):
    ad = obj.animation_data
    if not ad:
        return []
    acts = [ad.action] if ad.action else []
    for track in ad.nla_tracks:
        for strip in track.strips:
            if getattr(strip, "action", None):
                acts.append(strip.action)
    return acts


before = {n: local_size(bpy.data.objects[n]) for n in PROBES if n in bpy.data.objects}

nmesh = nvert = nuv = 0
for me in bpy.data.meshes:
    if me.users == 0:
        continue                                  # unused leftovers stay untouched
    for v in me.vertices:
        v.co *= S
    for layer in me.uv_layers:
        for d in layer.data:
            d.uv *= S
            nuv += 1
    nmesh += 1
    nvert += len(me.vertices)

nobj = 0
for obj in bpy.data.objects:
    if obj.type in ("MESH", "LIGHT", "CAMERA"):
        obj.location = obj.location * S
        nobj += 1

nlight = 0
for obj in bpy.data.objects:
    if obj.type != "LIGHT":
        continue
    lt = obj.data
    for attr in ("size", "size_y", "shadow_soft_size"):
        if hasattr(lt, attr):
            setattr(lt, attr, getattr(lt, attr) * S)
    lt.energy *= S * S                            # same illuminance at S times the distance
    nlight += 1

ncam = 0
for obj in bpy.data.objects:
    if obj.type != "CAMERA":
        continue
    cd = obj.data
    cd.clip_start *= S
    cd.clip_end *= S
    cd.dof.focus_distance *= S
    ncam += 1

nkey = 0
for obj in bpy.data.objects:
    for action in actions_of(obj):
        for fc in fcurves(action):
            if not fc.data_path.endswith("location"):
                continue
            for kp in fc.keyframe_points:
                kp.co[1] *= S
                kp.handle_left[1] *= S
                kp.handle_right[1] *= S
                nkey += 1

print("scaled x%.4f: %d meshes, %d verts, %d uv corners" % (S, nmesh, nvert, nuv))
print("  placements: %d objects, %d lights, %d cameras, %d location keys"
      % (nobj, nlight, ncam, nkey))
for name, was in before.items():
    now = local_size(bpy.data.objects[name])
    print("  %-16s %s -> %s" % (name, tuple(round(v, 4) for v in was),
                                tuple(round(v, 4) for v in now)))

bpy.ops.wm.save_mainfile()
print("saved", bpy.data.filepath)
