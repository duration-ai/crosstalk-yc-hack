#!/usr/bin/env bash
# Packs an exported iOS simulator Xcode project for a remote macOS build (Revyl's runner caps
# uploads at 100 MB). The export is ~440 MB raw; xz brings it to ~65 MB once the x86_64 IL2CPP
# toolchain, unused on an Apple-silicon runner, is left out.
# Output: Builds/ios-remote/{xcode.tar.xz,build.sh}. On the Mac: cd ios-remote && ./build.sh
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SRC="${1:-$REPO_ROOT/Builds/iOS-simulator}"
OUT="$REPO_ROOT/Builds/ios-remote"
rm -rf "$OUT" && mkdir -p "$OUT"
tar -C "$SRC" --exclude=Il2CppOutputProject/IL2CPP/build/deploy_x86_64 -cf - . | xz -T0 -9 > "$OUT/xcode.tar.xz"
cp "$REPO_ROOT/scripts/xcodebuild-simulator.sh" "$OUT/build.sh"
chmod +x "$OUT/build.sh"
ls -la "$OUT"
