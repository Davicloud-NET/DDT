# syntax=docker/dockerfile:1@sha256:4edf897a3ffa55b89f906fc8cc78afdb3f1834cc9c7083565e611a8a7d5fe99e
# check=skip=InvalidDefaultArgInFrom
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Builds the DDT Linux boot environment from the Debian packages in packages.lock.json, with this folder as context.
# build/Build-LinuxBoot.ps1 takes the stage export, build/Update-LinuxBootLock.ps1 the stages lock, tools-lock and
# notices. The first line names the program that reads this file by its digest, so it is the same one every time.

# The image's tools shape base.img's bytes, so the lock names it by digest and there is no default.
# busybox runs here to list its applets, which needs --platform linux/amd64.
ARG TOOLS_IMAGE

# What becomes /ddt in ddt.img. --build-context agent=<folder> puts the real agent in the placeholder's place.
FROM scratch AS agent
COPY placeholder/ /

FROM ${TOOLS_IMAGE} AS installed
COPY packages.lock.json /src/
# The tools come from the lock's snapshot too. apt would refuse its Release files, which are past their date.
RUN snapshot=$(sed -n 's/.*"snapshot": *"\([0-9TZ]*\)".*/\1/p' /src/packages.lock.json) \
    && test -n "$snapshot" \
    && printf '%s\n' 'Types: deb' "URIs: http://snapshot.debian.org/archive/debian/$snapshot" 'Suites: trixie' \
        'Components: main' 'Check-Valid-Until: no' 'Signed-By: /usr/share/keyrings/debian-archive-keyring.pgp' \
        > /etc/apt/sources.list.d/debian.sources \
    && apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates jq kbd kmod openssl python3 xz-utils zstd \
    && rm -rf /var/lib/apt/lists/* \
    # Files in the archives carry the snapshot's time, so one lock gives one base.img.
    && date -u -d "$(echo "$snapshot" | sed 's/^\(....\)\(..\)\(..\)T\(..\)\(..\)\(..\)Z$/\1-\2-\3 \4:\5:\6/')" +%s > /src/mtime

# apt takes the tools over plain HTTP, where another state of the archive that Debian signed as well can be handed
# over. Every package in the image must be at the version the lock names, or base.img could differ unnoticed.
FROM installed AS tools
RUN dpkg-query --show --showformat '${Package} ${Version}\n' | sort > /tmp/installed \
    && jq --raw-output '.expected.toolsPackages | to_entries[] | "\(.key) \(.value)"' /src/packages.lock.json | sort > /tmp/named \
    && if ! diff /tmp/named /tmp/installed; then \
        echo "The tools image holds other packages than the lock names. Update-LinuxBootLock.ps1 writes the lock again." >&2; \
        exit 1; \
    fi

FROM tools AS packages
COPY fetch.py /src/
RUN python3 /src/fetch.py /src/packages.lock.json /packages

# base.img changes with the lock and with the files named here, never with the agent.
FROM packages AS base
COPY assemble.py mkcpio.py modules.txt kernel-config.txt keymaps.txt /src/
COPY rootfs/ /src/rootfs/
# One zstd thread: with more, the number of processors would shape the bytes.
RUN python3 /src/assemble.py /src /packages /tree /out \
    && python3 /src/mkcpio.py --mtime "$(cat /src/mtime)" /tree /tmp/base.cpio \
    && zstd -q -19 -T1 /tmp/base.cpio -o /out/base.img \
    && rm -rf /tmp/base.cpio /packages/unpacked

FROM base AS build
COPY manifest.py /src/
COPY --from=agent / /agent/ddt/
RUN python3 /src/mkcpio.py --mtime "$(cat /src/mtime)" /agent /tmp/ddt.cpio \
    && zstd -q -19 -T1 /tmp/ddt.cpio -o /out/ddt.img \
    && python3 /src/manifest.py /src/packages.lock.json /out /agent/ddt/ddt-agent

FROM scratch AS export
COPY --from=build /out/ /

# The lock again, for the snapshot of Debian's archive that SNAPSHOT names. Its tools are those of the lock before,
# and not compared: after a change of the tools image the lock that names them is the one to be written.
FROM installed AS relock
ARG SNAPSHOT
COPY fetch.py lock.py /src/
RUN python3 /src/lock.py /src/packages.lock.json "${SNAPSHOT:?}" /out/packages.lock.json

FROM scratch AS lock
COPY --from=relock /out/ /

# The lock again with the packages of the tools image as it is at the lock's snapshot.
FROM installed AS retool
COPY fetch.py lock.py /src/
RUN python3 /src/lock.py --tools /src/packages.lock.json /out/packages.lock.json

FROM scratch AS tools-lock
COPY --from=retool /out/ /

# What licenses/linux-boot holds: the copyright files of the lock's packages and the list of their sources.
FROM packages AS copyrights
COPY copyrights.py sources.sh /src/
RUN python3 /src/copyrights.py /src/packages.lock.json /packages /out \
    && sh /src/sources.sh --no-download /src/packages.lock.json /out

FROM scratch AS notices
COPY --from=copyrights /out/ /
