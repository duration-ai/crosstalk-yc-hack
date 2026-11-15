#!/usr/bin/env bash
# Headless Linux desktop build, used to render the city on a Linux box (see scripts/screenshot-linux.sh).
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"
export BUILD_OUTPUT="${BUILD_OUTPUT:-$REPO_ROOT/Builds/Linux}"
mkdir -p "$REPO_ROOT/Builds"
log="$REPO_ROOT/Builds/unity-linux.log"
"$UNITY_PATH" -batchmode -nographics -quit -projectPath "$PROJECT_PATH" -buildTarget Linux64 \
  -executeMethod Dogpatch.EditorTools.BuildScript.BuildLinux -logFile "$log" || { tail -60 "$log"; exit 1; }
grep '\[Build\]' "$log"
