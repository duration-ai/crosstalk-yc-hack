#!/usr/bin/env bash
# Headless iOS export: Unity writes an Xcode project, no Mac needed for this step.
#   IOS_SDK=simulator (default) | device
#   IOS_BUNDLE_ID, APPLE_TEAM_ID (device signing), BUILD_OUTPUT (default Builds/iOS-<sdk>)
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"
export IOS_SDK="${IOS_SDK:-simulator}"
export BUILD_OUTPUT="${BUILD_OUTPUT:-$REPO_ROOT/Builds/iOS-$IOS_SDK}"
mkdir -p "$REPO_ROOT/Builds"
log="$REPO_ROOT/Builds/unity-ios-$IOS_SDK.log"
echo "Exporting iOS ($IOS_SDK) Xcode project to $BUILD_OUTPUT (log: $log)"
"$UNITY_PATH" -batchmode -nographics -quit -projectPath "$PROJECT_PATH" -buildTarget iOS \
  -executeMethod Dogpatch.EditorTools.BuildScript.BuildIOS -logFile "$log" || { tail -60 "$log"; exit 1; }
grep '\[Build\]' "$log"
