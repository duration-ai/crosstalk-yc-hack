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

# Builds apply PlayerSettings in memory and Unity writes them into ProjectSettings on exit.
# Call this before a build so the committed settings come back untouched afterwards.
preserve_project_settings() {
  local backup
  backup="$(mktemp -d)"
  cp -a "$PROJECT_PATH/ProjectSettings/." "$backup/"
  trap "rm -rf '$PROJECT_PATH/ProjectSettings' && mkdir -p '$PROJECT_PATH/ProjectSettings' && cp -a '$backup/.' '$PROJECT_PATH/ProjectSettings/' && rm -rf '$backup'" EXIT
}
