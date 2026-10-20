#!/usr/bin/env bash
# Full office rebuild: build level -> bake (remote) -> export sector GLB.
#
# Usage (from anywhere):
#   tools/full_rebuild.sh kenwi@bletchley --quality preview --device GPU --repack --denoise
#   tools/full_rebuild.sh kenwi@bletchley --export-models --quality preview --device GPU --repack
#
# Bake args after the host are passed through to bake_transfer.sh.
# --export-models is optional and re-exports content/models from office_assets.blend.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

EXPORT_MODELS=0
BAKE_ARGS=()

usage() {
  cat <<'EOF'
Usage: tools/full_rebuild.sh <user@host> [options]

Options:
  --export-models    Re-export content/models from ~/Work/office_assets.blend
  (all other flags)  Passed through to bake_transfer.sh
                     (--quality, --device, --repack, --denoise, ...)

Example:
  tools/full_rebuild.sh user@host --quality preview --device GPU --repack --denoise
EOF
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
    echo "Missing SSH target." >&2
    usage >&2
    exit 2
    ;;
esac

REMOTE="$1"
shift

while [[ $# -gt 0 ]]; do
  case "$1" in
    --export-models)
      EXPORT_MODELS=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      BAKE_ARGS+=("$1")
      shift
      ;;
  esac
done

if [[ "$EXPORT_MODELS" -eq 1 ]]; then
  echo "==> export models"
  blender -b ~/Work/office_assets.blend -P "$ROOT/tools/export_models.py"
else
  echo "==> skip model export (pass --export-models to rebuild content/models)"
fi

echo "==> build level + render setup"
blender --background --factory-startup \
  --python "$ROOT/tools/build_office_level.py" \
  --python "$ROOT/tools/apply_render_setup.py"

echo "==> bake + pull"
"$ROOT/tools/bake_transfer.sh" "$REMOTE" "${BAKE_ARGS[@]+"${BAKE_ARGS[@]}"}"

echo "==> export sector GLB"
blender -b ~/Work/office-level.blend --python "$ROOT/tools/export_level_glb.py"

echo "Done."
ls -la \
  "$ROOT/content/levels/office/office.glb" \
  "$ROOT/content/levels/office/office_lightmap.png" \
  "$ROOT/content/levels/office/office_lightmap.json"
