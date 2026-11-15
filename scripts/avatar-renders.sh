#!/usr/bin/env bash
# Renders avatars headless: one PNG per avatar JSON (avatar left, stock model right, and the
# source photo first when a .jpg/.jpeg/.png with the same name sits next to the JSON).
#   AVATAR_DIR  folder of avatar JSONs (default Builds/avatars)
#   RENDER_DIR  output folder (default Builds/avatar-renders)
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"
export AVATAR_DIR="$(realpath "${AVATAR_DIR:-$REPO_ROOT/Builds/avatars}")"
export RENDER_DIR="$(realpath -m "${RENDER_DIR:-$REPO_ROOT/Builds/avatar-renders}")"
mkdir -p "$REPO_ROOT/Builds" "$RENDER_DIR"
log="$REPO_ROOT/Builds/unity-avatar-renders.log"
xvfb-run -a "$UNITY_PATH" -batchmode -quit -projectPath "$PROJECT_PATH" \
  -executeMethod Dogpatch.EditorTools.AvatarRenders.Run -logFile "$log" || { tail -60 "$log"; exit 1; }
grep '\[AvatarRenders\]' "$log"
