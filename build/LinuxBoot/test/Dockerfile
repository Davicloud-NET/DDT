# syntax=docker/dockerfile:1
# check=skip=InvalidDefaultArgInFrom
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# The image build/Test-LinuxBoot.ps1 boots the Linux boot environment in: QEMU, and OVMF with Secure Boot on and
# Microsoft's keys enrolled. The context is build/LinuxBoot.

ARG TOOLS_IMAGE

FROM ${TOOLS_IMAGE}
COPY packages.lock.json /src/
# From the lock's snapshot, as the build's tools. chattr of e2fsprogs goes into the test's initramfs.
RUN snapshot=$(sed -n 's/.*"snapshot": *"\([0-9TZ]*\)".*/\1/p' /src/packages.lock.json) \
    && test -n "$snapshot" \
    && printf '%s\n' 'Types: deb' "URIs: http://snapshot.debian.org/archive/debian/$snapshot" 'Suites: trixie' \
        'Components: main' 'Check-Valid-Until: no' 'Signed-By: /usr/share/keyrings/debian-archive-keyring.pgp' \
        > /etc/apt/sources.list.d/debian.sources \
    && apt-get update \
    && apt-get install -y --no-install-recommends e2fsprogs ovmf python3 qemu-system-x86 \
    && rm -rf /var/lib/apt/lists/*
COPY mkcpio.py test/ /test/
# The tests put it in the place of the agent that ddt.img holds.
COPY placeholder/ /test/placeholder/
ENTRYPOINT ["python3", "/test/run.py"]
