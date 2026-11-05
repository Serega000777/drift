#!/usr/bin/env bash
# Scans tracked (or staged with --staged) files for credentials before they reach git history.
# Exit code 1 if anything suspicious is found. No external dependencies (grep -E only).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
MODE="${1:-all}"
if [ "$MODE" = "--staged" ]; then
  FILES=$(git diff --cached --name-only --diff-filter=ACM)
else
  FILES=$(git ls-files)
fi
PATTERNS=(
  'AKIA[0-9A-Z]{16}'
  'sk-ant-[A-Za-z0-9_-]{20,}'
  'sk-[A-Za-z0-9]{32,}'
  'AIza[0-9A-Za-z_-]{35}'
  'ghp_[A-Za-z0-9]{36}'
  'xox[baprs]-[A-Za-z0-9-]{10,}'
  '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
  '(api[_-]?key|client[_-]?secret|password)[[:space:]]*[:=][[:space:]]*"[^"]{12,}"'
)
found=0
for f in $FILES; do
  [ -f "$f" ] || continue
  case "$f" in
    tools/security/*|*/Editor/ReleaseHardening.cs|*.png|*.jpg|*.fbx|*.glb|*.dll) continue ;;
  esac
  for p in "${PATTERNS[@]}"; do
    if grep -E -i -n -- "$p" "$f" >/dev/null 2>&1; then
      echo "possible secret: $f  (pattern: $p)"
      found=1
    fi
  done
  case "$f" in
    *.keystore|*.jks|*.p12|*.pfx|*.pem|*.env|.env*) echo "secret-like file type tracked: $f"; found=1 ;;
  esac
done
if [ $found -ne 0 ]; then
  echo "Secret scan failed. Remove the secret, rotate it, and use environment/CI secrets instead."
  exit 1
fi
echo "Secret scan: clean."
