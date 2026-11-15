#!/usr/bin/env bash
# Runs on macOS next to xcode.tar.xz: unpacks the Unity export and builds an arm64 iOS Simulator
# .app at build/Build/Products/Release-iphonesimulator/Dogpatch.app (no signing needed).
set -euo pipefail
mkdir -p xcode
tar -xJf xcode.tar.xz -C xcode
cd xcode
rm -rf ../build/Build/Products/Release-iphonesimulator/Dogpatch.app
xcodebuild -project Unity-iPhone.xcodeproj -scheme Unity-iPhone -configuration Release \
  -sdk iphonesimulator -destination 'generic/platform=iOS Simulator' \
  -derivedDataPath ../build ARCHS=arm64 ONLY_ACTIVE_ARCH=NO CODE_SIGNING_ALLOWED=NO \
  COMPILER_INDEX_STORE_ENABLE=NO build | { grep -E 'error|warning: unable|BUILD|Compiling .*Bulk' || true; }
test -d ../build/Build/Products/Release-iphonesimulator/Dogpatch.app
echo "Built ../build/Build/Products/Release-iphonesimulator/Dogpatch.app"
