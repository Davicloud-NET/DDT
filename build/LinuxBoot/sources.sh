#!/bin/sh
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Collects the source code a release publishes beside the DDT Linux boot environment: the Debian source packages
# that packages.lock.json lists under "sources", and SOURCES.md, which says what they are.
#
#   sources.sh packages.lock.json <folder>
#   sources.sh --no-download packages.lock.json <folder>     Writes SOURCES.md alone.
#
# Each file is checked against the lock's SHA-256, and one that is already there and right is kept.
# Needs curl, jq and sha256sum.

set -eu

download=yes
if [ "${1:-}" = --no-download ]; then
    download=
    shift
fi
if [ $# -ne 2 ]; then
    echo "Usage: sources.sh [--no-download] packages.lock.json <folder>" >&2
    exit 2
fi
lock=$1
folder=$2

archive=$(jq -r '"\(.archive)/\(.snapshot)"' "$lock")
mkdir -p "$folder"

has_hash() {
    echo "$1  $2" | sha256sum -c --status - 2> /dev/null
}

if [ -n "$download" ]; then
    list=$(mktemp)
    trap 'rm -f "$list"' EXIT
    jq -r '.sources[] | .directory as $directory | .files[] | "\(.sha256) \($directory)/\(.name)"' "$lock" > "$list"
    while read -r sha256 path; do
        file=$folder/${path##*/}
        echo "${path##*/}"
        if ! has_hash "$sha256" "$file"; then
            curl -fsSL --retry 5 -o "$file" "$archive/$path"
            has_hash "$sha256" "$file" || {
                echo "${path##*/} is not the file in the lock." >&2
                exit 1
            }
        fi
    done < "$list"
fi

# Under leftOut the lock has the firmware's upstream tarball: the vendors' binaries again, without source code.
jq -r '
    def files: map("| `\(.name)` | \(.size) | `\(.sha256)` |") | join("\n");
    def names: map("`\(.name)` \(.version)") | join(", ");
    . as $lock
    | "# Source code of the DDT Linux boot environment",
      "",
      "DDT puts its Linux boot environment together from files of Debian packages and compiles none of them",
      "itself. These are the source packages of those packages, and of what Debian built them with, as they",
      "were in the Debian archive at its snapshot \(.snapshot). Every DDT release publishes these",
      "files as `ddt-linux-boot-sources.tar`, beside its other files. Each is also at the address given with it.",
      (.sources[]
        | . as $source
        | ($lock.packages | map(select(.source == $source.name and .sourceVersion == $source.version))) as $built
        | ($lock.packages | map(select(any(.builtUsing[]; .source == $source.name and .version == $source.version)))) as $used
        | "",
          "## \(.name) \(.version)",
          "",
          (if ($built | length) > 0 then "The source of \($built | names)." else empty end),
          (if ($used | length) > 0 then "Built into \($used | names)." else empty end),
          "From \($lock.archive)/\($lock.snapshot)/\(.directory)/",
          "",
          "| File | Bytes | SHA-256 |",
          "|---|---|---|",
          (.files | files),
          (if .leftOut then
            "",
            "DDT does not publish the next file, which is at the same address: it is the firmware as its vendors",
            "pass it on, without source code. The licence texts are in `\(.name).copyright`.",
            "",
            "| File | Bytes | SHA-256 |",
            "|---|---|---|",
            (.leftOut | files)
          else empty end))
' "$lock" > "$folder/SOURCES.md"

jq -r '"\(.sources | length) source packages, \([.sources[].files[].size] | add) bytes"' "$lock"
