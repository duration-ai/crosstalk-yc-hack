#!/usr/bin/env bash
# Renders avatars headless: one PNG per avatar (avatar left, stock model right, and the source
# photo first when there is one). Pick the avatars with one of:
#   AVATAR_PLAYERS  comma-separated playerIds fetched from AVATAR_URL (default the live deployment);
#                   PHOTO_DIR may hold <playerId>.jpg/.jpeg/.png to put beside each
#   AVATAR_DIR      folder of avatar JSONs (default Builds/avatars), photos named like the JSON next
#                   to it; or "stub" for the offline path (the bundled test photo through
#                   InMemoryGameBackend's stub, rendered as stub.png)
#   RENDER_DIR      output folder (default Builds/avatar-renders)
# GLBs are cached on disk by URL (Polyfork remix bakes are budgeted per IP), shared with the game.
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"
if [[ -z "${AVATAR_PLAYERS:-}" && "${AVATAR_DIR:-}" != "stub" ]]; then
  export AVATAR_DIR="$(realpath "${AVATAR_DIR:-$REPO_ROOT/Builds/avatars}")"
fi
[[ -n "${PHOTO_DIR:-}" ]] && export PHOTO_DIR="$(realpath "$PHOTO_DIR")"
export RENDER_DIR="$(realpath -m "${RENDER_DIR:-$REPO_ROOT/Builds/avatar-renders}")"
mkdir -p "$REPO_ROOT/Builds" "$RENDER_DIR"
preserve_project_settings
log="$REPO_ROOT/Builds/unity-avatar-renders.log"
xvfb-run -a "$UNITY_PATH" -batchmode -quit -projectPath "$PROJECT_PATH" \
  -executeMethod Dogpatch.EditorTools.AvatarRenders.Run -logFile "$log" || { tail -60 "$log"; exit 1; }
grep '\[AvatarRenders\]' "$log"
