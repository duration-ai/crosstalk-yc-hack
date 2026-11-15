#!/usr/bin/env bash
# Renders avatars headless: one PNG per avatar JSON (avatar left, stock model right, and the
# source photo first when a .jpg/.jpeg/.png with the same name sits next to the JSON).
#   AVATAR_DIR  folder of avatar JSONs (default Builds/avatars), or "stub" for the offline path:
#               the bundled test photo through InMemoryGameBackend's stub, rendered as stub.png
#   RENDER_DIR  output folder (default Builds/avatar-renders)
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"
if [[ "${AVATAR_DIR:-}" != "stub" ]]; then
  AVATAR_DIR="$(realpath "${AVATAR_DIR:-$REPO_ROOT/Builds/avatars}")"
fi
export AVATAR_DIR
export RENDER_DIR="$(realpath -m "${RENDER_DIR:-$REPO_ROOT/Builds/avatar-renders}")"
mkdir -p "$REPO_ROOT/Builds" "$RENDER_DIR"
log="$REPO_ROOT/Builds/unity-avatar-renders.log"
xvfb-run -a "$UNITY_PATH" -batchmode -quit -projectPath "$PROJECT_PATH" \
  -executeMethod Dogpatch.EditorTools.AvatarRenders.Run -logFile "$log" || { tail -60 "$log"; exit 1; }
grep '\[AvatarRenders\]' "$log"
