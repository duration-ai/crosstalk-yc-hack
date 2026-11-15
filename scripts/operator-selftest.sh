#!/usr/bin/env bash
# Headless checks for the voice Operator's pure pieces (Dogpatch.EditorTools.OperatorSelfTest).
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"
mkdir -p "$REPO_ROOT/Builds"
log="$REPO_ROOT/Builds/operator-selftest.log"
status=0
"$UNITY_PATH" -batchmode -nographics -projectPath "$PROJECT_PATH" \
  -executeMethod Dogpatch.EditorTools.OperatorSelfTest.Run -logFile "$log" || status=$?
grep -E '\[SelfTest\]|error CS' "$log" || true
exit $status
