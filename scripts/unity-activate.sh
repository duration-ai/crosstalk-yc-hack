#!/usr/bin/env bash
# Activates Unity for headless use from environment variables. Never writes secrets into the repo.
#   Pro/Plus:  UNITY_SERIAL + UNITY_EMAIL + UNITY_PASSWORD activate a seat.
#   Personal:  UNITY_EMAIL + UNITY_PASSWORD activate a free Personal seat on this machine through
#              Unity's licensing client. A Personal .ulf is bound to the machine that issued it, so
#              UNITY_LICENSE only helps when it was issued for this machine.
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"

client="$(dirname "$UNITY_PATH")/Data/Resources/Licensing/Client/Unity.Licensing.Client"
[[ "$(uname)" == "Darwin" ]] && client="$(dirname "$UNITY_PATH")/../Frameworks/UnityLicensingClient.app/Contents/MacOS/Unity.Licensing.Client"

if [[ -n "${UNITY_SERIAL:-}" && ${#UNITY_SERIAL} -gt 8 ]]; then
  "$UNITY_PATH" -batchmode -nographics -quit -logFile - \
    -serial "$UNITY_SERIAL" -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" | grep -i licens || true
  echo "Activated serial seat"
elif [[ -n "${UNITY_EMAIL:-}" && -n "${UNITY_PASSWORD:-}" ]]; then
  "$client" --activate-all --include-personal --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD"
elif [[ -n "${UNITY_LICENSE:-}" ]]; then
  dir="$HOME/.local/share/unity3d/Unity"
  mkdir -p "$dir"
  printf '%s' "$UNITY_LICENSE" > "$dir/Unity_lic.ulf"
  chmod 600 "$dir/Unity_lic.ulf"
  echo "Installed license file at $dir/Unity_lic.ulf"
else
  echo "Set UNITY_EMAIL and UNITY_PASSWORD (plus UNITY_SERIAL for Pro/Plus), or UNITY_LICENSE" >&2
  exit 1
fi
