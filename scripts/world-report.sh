#!/usr/bin/env bash
# Builds the world headless and writes the coverage/fit report plus the reference renders.
#   scripts/world-report.sh [out-dir]   (default Builds/world)
# Needs an activated Unity (scripts/unity-activate.sh) and xvfb-run on Linux.
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"
out="$(mkdir -p "${1:-$REPO_ROOT/Builds/world}" && cd "${1:-$REPO_ROOT/Builds/world}" && pwd)"
preserve_project_settings
run=("$UNITY_PATH" -batchmode -quit -projectPath "$PROJECT_PATH" -executeMethod Dogpatch.EditorTools.WorldReport.Run -logFile "$out/unity.log")
[[ "$(uname)" == "Linux" ]] && run=(xvfb-run -a -s "-screen 0 1920x1080x24" "${run[@]}" -force-glcore)
WORLD_REPORT="$out/world-report.json" RENDER_DIR="$out" "${run[@]}"
echo "Wrote $out/world-report.json and renders to $out"
