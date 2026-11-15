#!/usr/bin/env bash
# Downloads the Linux Unity editor and the iOS build module into ~/Unity/<version>.
set -euo pipefail
source "$(dirname "$0")/unity-env.sh"
REV="8f082d2fe476"
dest="$HOME/Unity/$UNITY_VERSION"
if [[ -x "$dest/Editor/Unity" && -d "$dest/Editor/Data/PlaybackEngines/iOSSupport" ]]; then
  echo "Unity $UNITY_VERSION already installed at $dest"
  exit 0
fi
mkdir -p "$dest"
curl -fL "https://download.unity3d.com/download_unity/$REV/LinuxEditorInstaller/Unity-$UNITY_VERSION.tar.xz" | tar -xJ -C "$dest"
curl -fL "https://download.unity3d.com/download_unity/$REV/LinuxEditorTargetInstaller/UnitySetup-iOS-Support-for-Editor-$UNITY_VERSION.tar.xz" | tar -xJ -C "$dest"
"$dest/Editor/Unity" -version
