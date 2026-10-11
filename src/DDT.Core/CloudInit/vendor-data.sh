#!/bin/sh
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.
# DDT: goes on with this machine's task sequence. Written by the DDT agent, removed when the run ends.
set -eu
say() { echo "DDT: $*"; }
if ! command -v systemctl >/dev/null || [ ! -d /run/systemd/system ]; then
    say "this image does not run systemd, so the run cannot go on in it." >&2
    exit 1
fi
seed=/run/ddt-seed
mkdir -p "$seed"
trap 'code=$?; umount "$seed" 2>/dev/null || true; rmdir "$seed" 2>/dev/null || true; [ "$code" -eq 0 ] || say "the agent could not be installed (exit code $code), so the run does not go on in this Linux." >&2' EXIT
# The seed the run was handed over on is the CIDATA volume that holds the run's token.
found=
for device in /dev/disk/by-label/CIDATA $(blkid -t LABEL=CIDATA -o device 2>/dev/null || true); do
    [ -b "$device" ] || continue
    mount -t vfat -o rw,umask=077 "$device" "$seed" 2>/dev/null || continue
    if [ -f "$seed/ddt/token" ]; then
        found=$device
        break
    fi
    umount "$seed"
done
if [ -z "$found" ]; then
    say "no seed holds a run to go on with, so nothing was installed."
    exit 0
fi
install -d -m 0755 /usr/local/lib/ddt
install -d -m 0700 /var/lib/ddt /var/lib/ddt/run
install -m 0755 "$seed/ddt/ddt-agent" /usr/local/lib/ddt/ddt-agent
install -m 0600 "$seed/ddt/agent.json" /var/lib/ddt/agent.json
install -m 0600 "$seed/ddt/state.json" /var/lib/ddt/run/state.json
install -m 0600 "$seed/ddt/token" /var/lib/ddt/run/token
cat > /etc/systemd/system/ddt-agent.service <<'UNIT'
[Unit]
Description=DDT agent, which goes on with this machine's task sequence and then removes itself
Wants=network-online.target
After=network-online.target
ConditionPathExists=/usr/local/lib/ddt/ddt-agent

[Service]
Type=simple
ExecStart=/usr/local/lib/ddt/ddt-agent --linux-phase
Environment=HOME=/root PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
Restart=on-failure
RestartSec=15
TimeoutStopSec=120

[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable ddt-agent.service
# The agent is on the disk for good before the token leaves the seed: a machine that loses power here starts the
# agent at its next start, and the agent removes a token the seed still holds.
sync
size=$(wc -c < "$seed/ddt/token")
if dd if=/dev/zero of="$seed/ddt/token" bs="$size" count=1 conv=notrunc,fsync 2>/dev/null && rm -f "$seed/ddt/token"; then
    say "the agent is installed from $found and the run's token is off the seed."
else
    say "the agent is installed from $found, but the run's token could not be removed from the seed. The agent tries again." >&2
fi
systemctl start --no-block ddt-agent.service
say "ddt-agent.service is started. It waits for cloud-init, goes on with the task sequence and removes itself at the end."
