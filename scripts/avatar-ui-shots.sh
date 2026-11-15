#!/usr/bin/env bash
# Screenshots of every selfie-avatar screen over the game, from the Linux player under Xvfb: the
# corner card, the pause menu chip, the capture screen (bundled test photo), SUITING UP, NO ONE IN
# SHOT, TRY AGAIN, then the avatar in the world. A local fake server answers 422, 503, then a slow
# 200 with AVATAR_PLAYER's live avatar (default live-kubanino).
#   SHOT_DIR  output folder (default Builds/avatar-ui)
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"
out="$(realpath -m "${SHOT_DIR:-$REPO_ROOT/Builds/avatar-ui}")"
player="$REPO_ROOT/Builds/Linux/Dogpatch.x86_64"
[[ -x "$player" ]] || bash "$REPO_ROOT/scripts/build-linux.sh"
mkdir -p "$out"
avatar="$(mktemp)"
curl -fsS "https://rapid-gnat-965.convex.site/api/avatar?playerId=${AVATAR_PLAYER:-live-kubanino}" > "$avatar"
python3 -I "$REPO_ROOT/scripts/fake-avatar-server.py" "$avatar" 8787 > "$out/fake-server.log" 2>&1 &
server=$!
trap 'kill $server 2>/dev/null; rm -f "$avatar"' EXIT
sleep 1
log="$out/player.log"
xvfb-run -a -s "-screen 0 720x1560x24" "$player" -screen-fullscreen 0 -screen-width 720 -screen-height 1560 \
  -avatarUiCapture "$out" -dogpatch-avatar-test-photo -dogpatch-avatar-url http://127.0.0.1:8787 -logFile "$log"
grep -E '\[AvatarUiCapture\]|\[Avatar\]' "$log"
