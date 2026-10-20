#!/usr/bin/env bash
# Run a v2 lightmap bake on a remote host and pull results into this repo.
#
# Usage:
#   tools/bake_transfer.sh kenwi@bletchley --quality preview
#   tools/bake_transfer.sh kenwi@bletchley --quality medium --device GPU
#   tools/bake_transfer.sh kenwi@bletchley --quality high --device GPU --repack
#
# Syncs bake tools (+ textures if missing remotely), bakes, then copies the
# lightmap atlas (+ blend) back locally.
#
# With --denoise, OIDN runs on the remote during the bake (oidnDenoise must be
# on the remote PATH). The pulled atlas is already denoised.

set -euo pipefail

REMOTE_ROOT="${BAKE_REMOTE_ROOT:-~/Work/ColdAudit}"
REMOTE_BLEND="${BAKE_REMOTE_BLEND:-~/Work/office-level.blend}"
LOCAL_BLEND="${BAKE_LOCAL_BLEND:-$HOME/Work/office-level.blend}"

QUALITY="preview"
DEVICE="CPU"
REPACK=0
NO_DENOISE=1
SYNC_TEXTURES=0

usage() {
  cat <<'EOF'
Usage: tools/bake_transfer.sh <user@host> [options]

Options:
  --quality <name>   preview | medium | high | production (default: preview)
  --device <name>    CPU | GPU (default: CPU)
  --repack           Rebuild Lightmap UVs before baking
  --denoise          Run OIDN on the remote during bake (needs oidnDenoise there)
  --no-denoise       Skip denoise (default)
  --sync-textures    Always rsync content/textures/ before baking
  -h, --help         Show this help

Environment:
  BAKE_REMOTE_ROOT     Remote repo root (default: ~/Work/ColdAudit)
  BAKE_REMOTE_BLEND    Remote .blend path (default: ~/Work/office-level.blend)
  BAKE_LOCAL_BLEND     Local .blend path (default: ~/Work/office-level.blend)
EOF
}

denoise_label() {
  if [[ "$NO_DENOISE" -eq 0 ]]; then
    echo on
  else
    echo off
  fi
}

if [[ $# -lt 1 ]]; then
  usage >&2
  exit 2
fi

case "$1" in
  -h|--help)
    usage
    exit 0
    ;;
  -*)
    echo "Missing SSH target. Example: $0 kenwi@bletchley --quality preview" >&2
    usage >&2
    exit 2
    ;;
esac

REMOTE="$1"
shift

while [[ $# -gt 0 ]]; do
  case "$1" in
    --quality)
      QUALITY="${2:?--quality needs a value}"
      shift 2
      ;;
    --device)
      DEVICE="${2:?--device needs a value}"
      shift 2
      ;;
    --repack)
      REPACK=1
      shift
      ;;
    --denoise)
      NO_DENOISE=0
      shift
      ;;
    --no-denoise)
      NO_DENOISE=1
      shift
      ;;
    --sync-textures)
      SYNC_TEXTURES=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

DEVICE="$(echo "$DEVICE" | tr '[:lower:]' '[:upper:]')"
case "$DEVICE" in
  CPU|GPU) ;;
  *)
    echo "--device must be CPU or GPU (got $DEVICE)" >&2
    exit 2
    ;;
esac

case "$QUALITY" in
  preview|medium|high|production) ;;
  *)
    echo "--quality must be preview|medium|high|production (got $QUALITY)" >&2
    exit 2
    ;;
esac

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOCAL_OFFICE="$ROOT/content/levels/office"

BAKE_ARGS=(--quality "$QUALITY" --device "$DEVICE")
if [[ "$REPACK" -eq 1 ]]; then
  BAKE_ARGS+=(--repack)
fi
if [[ "$NO_DENOISE" -eq 1 ]]; then
  BAKE_ARGS+=(--no-denoise)
fi

# Expand remote ~ for rsync destinations that need it.
REMOTE_ROOT_EXPANDED="$(ssh "$REMOTE" "echo $REMOTE_ROOT")"
REMOTE_BLEND_EXPANDED="$(ssh "$REMOTE" "echo $REMOTE_BLEND")"

echo "==> sync tools -> $REMOTE:$REMOTE_ROOT_EXPANDED/tools/"
ssh "$REMOTE" "mkdir -p $REMOTE_ROOT/tools $REMOTE_ROOT/content/levels/office $REMOTE_ROOT/content/textures"
rsync -az --progress \
  "$ROOT/tools/bake_lightmaps_v2.py" \
  "$ROOT/tools/denoise_lightmap.py" \
  "$REMOTE:$REMOTE_ROOT_EXPANDED/tools/"

if [[ -f "$LOCAL_BLEND" ]]; then
  # Checksum compare: skip the upload when remote already matches local.
  if rsync -azc --dry-run --out-format='%n' \
      "$LOCAL_BLEND" "$REMOTE:$REMOTE_BLEND_EXPANDED" | grep -q .; then
    echo "==> sync blend (changed) -> $REMOTE:$REMOTE_BLEND_EXPANDED"
    rsync -azc --progress "$LOCAL_BLEND" "$REMOTE:$REMOTE_BLEND_EXPANDED"
  else
    echo "==> blend unchanged, skip upload"
  fi
fi

need_textures=0
if [[ "$SYNC_TEXTURES" -eq 1 ]]; then
  need_textures=1
elif ! ssh "$REMOTE" "test -f $REMOTE_ROOT/content/textures/laminate_floor_02/laminate_floor_02_diffuse.png"; then
  need_textures=1
fi
if [[ "$need_textures" -eq 1 ]]; then
  echo "==> sync textures -> $REMOTE:$REMOTE_ROOT_EXPANDED/content/textures/"
  rsync -az --progress "$ROOT/content/textures/" "$REMOTE:$REMOTE_ROOT_EXPANDED/content/textures/"
fi

echo "==> bake on $REMOTE (quality=$QUALITY device=$DEVICE denoise=$(denoise_label))"
# Use already-expanded absolute paths (printf %q would escape '~' and break cd).
# Bake args are %q'd so the remote shell cannot split them. Blender gets
# </dev/null so it cannot steal the rest of the bash -s script on stdin.
remote_root_q=$(printf '%q' "$REMOTE_ROOT_EXPANDED")
remote_blend_q=$(printf '%q' "$REMOTE_BLEND_EXPANDED")
bake_args_q=$(printf '%q ' "${BAKE_ARGS[@]}")
ssh "$REMOTE" "bash -s" <<EOF
set -euo pipefail
cd ${remote_root_q}
BLENDER=\$(ls -1d /opt/blender*/blender 2>/dev/null | sort | tail -n1)
if [[ -z "\$BLENDER" || ! -x "\$BLENDER" ]]; then
  echo "No blender binary under /opt/blender*/blender" >&2
  exit 1
fi
if [[ ${NO_DENOISE} -eq 0 ]] && ! command -v oidnDenoise >/dev/null 2>&1; then
  echo "oidnDenoise not on remote PATH; install OIDN or pass --no-denoise" >&2
  exit 1
fi
echo "Using \$BLENDER"
# --log-level error hides Cycles HIPEW/CUEW "library missing" warnings on
# machines that only have CUDA (or only HIP), which are harmless.
bake_log=\$(mktemp)
trap 'rm -f "\$bake_log"' EXIT
set +e
/usr/bin/time -f "WALL_SEC=%e" "\$BLENDER" -b --log-level error ${remote_blend_q} \\
  -P tools/bake_lightmaps_v2.py -- ${bake_args_q} </dev/null 2>&1 | tee "\$bake_log"
bake_rc=\${PIPESTATUS[0]}
set -e
if [[ \$bake_rc -ne 0 ]] || ! grep -q 'BAKE_V2_OK' "\$bake_log"; then
  echo "Bake failed on remote (blender exit=\$bake_rc, no BAKE_V2_OK)." >&2
  exit 1
fi
EOF

echo "==> pull lightmap -> $LOCAL_OFFICE/"
mkdir -p "$LOCAL_OFFICE"
rsync -az --progress \
  "$REMOTE:$REMOTE_ROOT_EXPANDED/content/levels/office/office_lightmap.png" \
  "$REMOTE:$REMOTE_ROOT_EXPANDED/content/levels/office/office_lightmap.json" \
  "$REMOTE:$REMOTE_ROOT_EXPANDED/content/levels/office/office_lightmap_${QUALITY}.png" \
  "$REMOTE:$REMOTE_ROOT_EXPANDED/content/levels/office/office_lightmap_${QUALITY}.json" \
  "$LOCAL_OFFICE/"

echo "==> pull blend -> $LOCAL_BLEND"
rsync -az --progress "$REMOTE:$REMOTE_BLEND_EXPANDED" "$LOCAL_BLEND"

echo "Done. host=$REMOTE quality=$QUALITY device=$DEVICE denoise=$(denoise_label)"
ls -la \
  "$LOCAL_OFFICE/office_lightmap.png" \
  "$LOCAL_OFFICE/office_lightmap.json" \
  "$LOCAL_OFFICE/office_lightmap_${QUALITY}.png" \
  "$LOCAL_OFFICE/office_lightmap_${QUALITY}.json"
