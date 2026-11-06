#!/usr/bin/env bash
# Drift Career - update the VPS checkout from GitHub (fast-forward only) and run core tests
# when something changed. Called by drift-update.timer; can be run by hand:
#   bash tools/setup/vps-update.sh          # pull, test only if new commits arrived
#   bash tools/setup/vps-update.sh --force  # pull and always test
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
LOG="${DRIFT_STATUS_LOG:-$HOME/drift-status.log}"

log() { echo "$(date -Is) $*" | tee -a "$LOG"; }

before="$(git rev-parse HEAD)"
git fetch --quiet origin
if ! git merge --ff-only --quiet '@{u}' 2>/dev/null; then
  log "update blocked: local changes or diverged history (fix by hand: git status)"
  exit 1
fi
git lfs pull >/dev/null 2>&1 || log "warning: git lfs pull failed"
after="$(git rev-parse HEAD)"

if [ "$before" = "$after" ] && [ "${1:-}" != "--force" ]; then exit 0; fi

if summary="$(dotnet test tools/CoreTests 2>&1 | tail -n 2)"; then status="OK"; else status="FAILED"; fi
log "$(git rev-parse --short HEAD) tests $status: $(echo "$summary" | tr -s '\n' ' ')"
[ "$status" = "OK" ]
