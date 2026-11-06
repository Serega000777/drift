#!/usr/bin/env bash
# Drift Career - one-time VPS setup (Ubuntu 22.04 / 24.04). Safe to run again.
#
# Run from the cloned repository as a normal user with sudo (not root):
#   sudo bash tools/setup/vps-setup.sh              # packages, firewall, auto-updates, auto-pull
#   sudo bash tools/setup/vps-setup.sh --harden-ssh # also: key-only SSH, no root login
#
# What it does:
#   1. installs git, git-lfs, .NET 8 SDK, ufw, fail2ban, unattended-upgrades
#   2. firewall: only SSH, 80, 443 open
#   3. automatic security updates, fail2ban for SSH
#   4. a systemd timer (every 5 min) that fast-forwards the checkout from GitHub
#      and runs the core tests when something changed (log: ~/drift-status.log)
# Security note: the timer runs code from the repository (tests) as the unprivileged owner
# of the checkout. Anyone who can push to the tracked branch can run code as that user.
set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OWNER="${SUDO_USER:-$(stat -c %U "$REPO_DIR")}"
HARDEN_SSH=0
for arg in "$@"; do
  case "$arg" in
    --harden-ssh) HARDEN_SSH=1 ;;
    *) echo "unknown option: $arg" >&2; exit 2 ;;
  esac
done

if [ "$(id -u)" -ne 0 ]; then echo "Run with sudo." >&2; exit 1; fi
if [ "$OWNER" = "root" ]; then
  echo "The checkout must belong to a normal user, not root. See docs/VPS_SETUP.md, step 2." >&2
  exit 1
fi
OWNER_HOME="$(getent passwd "$OWNER" | cut -d: -f6)"
as_owner() { sudo -u "$OWNER" -H "$@"; }

echo "== 1/5 packages"
apt-get update -q
if ! DEBIAN_FRONTEND=noninteractive apt-get install -y -q git git-lfs ufw fail2ban unattended-upgrades dotnet-sdk-8.0; then
  echo "Package install failed. On Debian or older Ubuntu add the Microsoft package feed for dotnet-sdk-8.0 first." >&2
  exit 1
fi

echo "== 2/5 firewall: SSH, 80, 443"
ufw allow OpenSSH >/dev/null
ufw allow 80,443/tcp >/dev/null
ufw --force enable >/dev/null

echo "== 3/5 security updates and fail2ban"
cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF
systemctl enable --now fail2ban >/dev/null

if [ "$HARDEN_SSH" -eq 1 ]; then
  if [ ! -s "$OWNER_HOME/.ssh/authorized_keys" ]; then
    echo "Refusing --harden-ssh: $OWNER has no SSH key in ~/.ssh/authorized_keys (you would be locked out)." >&2
    exit 1
  fi
  # 00- so it is read before cloud-init's 50-*.conf (sshd uses the first value it sees).
  printf 'PasswordAuthentication no\nKbdInteractiveAuthentication no\nPermitRootLogin no\n' > /etc/ssh/sshd_config.d/00-drift-hardening.conf
  sshd -t
  systemctl reload ssh 2>/dev/null || systemctl reload sshd
  echo "   SSH: key-only, root login disabled. Keep this session open and test a new login first."
fi

echo "== 4/5 repository"
as_owner git lfs install >/dev/null
as_owner git -C "$REPO_DIR" lfs pull || echo "   (git lfs pull failed - binary assets may be missing)"

echo "== 5/5 auto-update timer"
cat > /etc/systemd/system/drift-update.service <<EOF
[Unit]
Description=Drift Career: pull latest code from GitHub and run core tests
Wants=network-online.target
After=network-online.target

[Service]
Type=oneshot
User=$OWNER
WorkingDirectory=$REPO_DIR
Environment=DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
ExecStart=/usr/bin/env bash "$REPO_DIR/tools/setup/vps-update.sh"
Nice=10
EOF
cat > /etc/systemd/system/drift-update.timer <<'EOF'
[Unit]
Description=Drift Career: check GitHub for updates every 5 minutes

[Timer]
OnBootSec=2min
OnUnitActiveSec=5min

[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload
systemctl enable --now drift-update.timer >/dev/null

echo "== first test run"
as_owner env DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 bash "$REPO_DIR/tools/setup/vps-update.sh" --force || true
echo
echo "Done. Status log: $OWNER_HOME/drift-status.log"
echo "Timer: systemctl list-timers drift-update.timer    Logs: journalctl -u drift-update.service"
