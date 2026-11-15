#!/usr/bin/env bash
# Shared paths for the Unity scripts. Override UNITY_PATH to point at another editor install.
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_VERSION="6000.3.28f1"
PROJECT_PATH="$REPO_ROOT/unity"
if [[ -z "${UNITY_PATH:-}" ]]; then
  if [[ "$(uname)" == "Darwin" ]]; then
    UNITY_PATH="/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity"
  else
    UNITY_PATH="$HOME/Unity/$UNITY_VERSION/Editor/Unity"
  fi
fi
export REPO_ROOT UNITY_VERSION PROJECT_PATH UNITY_PATH
