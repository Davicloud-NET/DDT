# syntax=docker/dockerfile:1@sha256:4edf897a3ffa55b89f906fc8cc78afdb3f1834cc9c7083565e611a8a7d5fe99e
# check=skip=InvalidDefaultArgInFrom
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Writes the console's keyboard tables with make_layouts.py, with this folder as context. build/Update-KeyboardLayouts.ps1
# runs it with the tools image and snapshot of build/LinuxBoot/packages.lock.json, so the same lock gives the same tables.
ARG TOOLS_IMAGE

FROM ${TOOLS_IMAGE} AS tables
ARG SNAPSHOT
# apt would refuse the snapshot's Release files, which are past their date.
RUN test -n "$SNAPSHOT" \
    && printf '%s\n' 'Types: deb' "URIs: http://snapshot.debian.org/archive/debian/$SNAPSHOT" 'Suites: trixie' \
        'Components: main' 'Check-Valid-Until: no' 'Signed-By: /usr/share/keyrings/debian-archive-keyring.pgp' \
        > /etc/apt/sources.list.d/debian.sources \
    && apt-get update \
    && apt-get install -y --no-install-recommends python3=3.13.5-1 libxkbcommon0=1.7.0-2 xkb-data=2.42-1 \
    && rm -rf /var/lib/apt/lists/*
COPY make_layouts.py /src/
RUN mkdir /out \
    && python3 /src/make_layouts.py /out \
    && cp /usr/share/doc/xkb-data/copyright /out/copyright

FROM scratch AS export
COPY --from=tables /out/ /
