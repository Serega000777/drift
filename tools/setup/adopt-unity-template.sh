#!/usr/bin/env bash
# Copies Packages/ and ProjectSettings/ (and optionally Assets/Settings) from a fresh
# "Universal 3D" Unity template project into this repository's UnityProject/ folder.
# Usage: ./tools/setup/adopt-unity-template.sh /path/to/DriftTemplate
set -euo pipefail
TEMPLATE="${1:?usage: adopt-unity-template.sh /path/to/TemplateProject}"
REPO="$(cd "$(dirname "$0")/../.." && pwd)"
TARGET="$REPO/UnityProject"
for d in Packages ProjectSettings; do
  [ -d "$TEMPLATE/$d" ] || { echo "Not a Unity project (missing $d): $TEMPLATE" >&2; exit 1; }
  cp -R "$TEMPLATE/$d" "$TARGET/"
  echo "Copied $d"
done
if [ -d "$TEMPLATE/Assets/Settings" ]; then
  mkdir -p "$TARGET/Assets"
  cp -R "$TEMPLATE/Assets/Settings" "$TARGET/Assets/"
  echo "Copied Assets/Settings (URP assets)"
fi
echo "Done. Unity Hub -> Add project from disk -> $TARGET"
