#!/bin/sh
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Installs the DDT server on a Linux host that has Docker, or upgrades it. Each release carries this script:
#
#   curl -fsSL https://github.com/Davicloud-NET/DDT/releases/latest/download/install.sh | sudo sh
#
# With options:
#
#   curl -fsSL https://github.com/Davicloud-NET/DDT/releases/latest/download/install.sh | sudo sh -s -- --port 443
#
#   --version 26.1.412   That release instead of the latest one. A pre-release needs it: latest skips it.
#   --port 8443          The HTTPS port, on a new install.
#   --names a,b          More names and addresses for DDT's certificate, on a new install. The host's own name is in.
#   --firewall yes|no    Opens DDT's ports in ufw or firewalld without asking, or leaves them closed.
#   --directory /opt/ddt Where compose.yaml and .env go.
#   --source DIR|URL     compose.yaml and SHA256SUMS from there instead of a release. For testing a build.
#   --image REFERENCE    Another image than the release's. For testing a build.
#
# It installs nothing but DDT: without Docker Engine and its compose plugin it stops and says so. The store is the
# Docker volume ddt_ddt-store, which an upgrade and "docker compose down" both keep.

set -eu

version=
port=
names=
firewall=
directory=/opt/ddt
source=
image=

say() { printf '%s\n' "$*"; }
fail() { printf 'install.sh: %s\n' "$*" >&2; exit 1; }

while [ $# -gt 0 ]; do
    [ $# -ge 2 ] || fail "$1 needs a value."
    case "$1" in
        --version) version=$2 ;;
        --port) port=$2 ;;
        --names) names=$2 ;;
        --firewall) firewall=$2 ;;
        --directory) directory=$2 ;;
        --source) source=$2 ;;
        --image) image=$2 ;;
        *) fail "$1 is not an option. The top of this script lists them." ;;
    esac
    shift 2
done

case "$port" in
    '' | *[!0-9]*) [ -z "$port" ] || fail "--port takes a number." ;;
esac
case "$firewall" in
    '' | yes | no) ;;
    *) fail "--firewall takes yes or no." ;;
esac

[ "$(id -u)" -eq 0 ] || fail "Installing DDT needs root. Run it with sudo."
[ "$(uname -m)" = x86_64 ] || fail "DDT's image is built for x86-64, and this host is $(uname -m)."

command -v docker >/dev/null 2>&1 ||
    fail "DDT runs in Docker, which this host lacks. Install Docker Engine, https://docs.docker.com/engine/install/, and run this again."
docker compose version >/dev/null 2>&1 ||
    fail "Docker is here, but not its compose plugin. Install the package docker-compose-plugin and run this again."
docker info >/dev/null 2>&1 ||
    fail "Docker does not answer. Start it, for example with systemctl start docker, and run this again."

case "$(docker info --format '{{.OperatingSystem}}' 2>/dev/null)" in
    *'Docker Desktop'*) say "This is Docker Desktop, which delivers no broadcasts to containers: DDT will run, but machines cannot netboot from it." ;;
esac

# $1 is a URL or a file, $2 where it goes.
fetch() {
    case "$1" in
        http://* | https://*)
            if command -v curl >/dev/null 2>&1; then
                curl -fsSL "$1" -o "$2"
            elif command -v wget >/dev/null 2>&1; then
                wget -qO "$2" "$1"
            else
                fail "Neither curl nor wget is here to download with."
            fi
            ;;
        *) cp "$1" "$2" ;;
    esac
}

# Prints what the server answers at the path $1, whatever certificate it shows: its root is not trusted here yet.
# By address, because localhost may mean ::1 first, where DDT does not listen.
ask_server() {
    if command -v curl >/dev/null 2>&1; then
        curl -fsk "https://127.0.0.1:$port$1"
    else
        wget -qO- --no-check-certificate "https://127.0.0.1:$port$1"
    fi
}

compose() { docker compose --project-directory "$directory" "$@"; }

# The release this copy came with, which the release workflow fills in. So a copy installs its own release, a
# pre-release too, which latest skips.
released=
[ -n "$version" ] || version=$released

if [ -n "$source" ]; then
    release=${source%/}
elif [ -n "$version" ]; then
    release=https://github.com/Davicloud-NET/DDT/releases/download/v$version
else
    release=https://github.com/Davicloud-NET/DDT/releases/latest/download
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

say "Getting compose.yaml from $release"
fetch "$release/compose.yaml" "$work/compose.yaml"
fetch "$release/SHA256SUMS" "$work/SHA256SUMS"

expected=$(awk '$2 == "compose.yaml" || $2 == "*compose.yaml" { print $1 }' "$work/SHA256SUMS")
[ -n "$expected" ] || fail "The release's SHA256SUMS names no compose.yaml."
actual=$(sha256sum "$work/compose.yaml" | awk '{ print $1 }')
[ "$expected" = "$actual" ] || fail "compose.yaml does not match the release's SHA256SUMS. Nothing was installed."

mkdir -p "$directory"
cp "$work/compose.yaml" "$directory/compose.yaml"

# An upgrade keeps what .env says, as the administrator may have changed it.
if [ -f "$directory/.env" ]; then
    [ -z "$port$names" ] || say "$directory/.env is there already, so --port and --names change nothing. Edit the file instead."
else
    host=$(hostname -f 2>/dev/null || hostname)
    {
        say "DDT_ROLES=web,pxe"
        say "DDT_HOSTNAMES=$host${names:+,$names}"
        say "DDT_PORT=${port:-8443}"
    } >"$directory/.env"
    chmod 600 "$directory/.env"
fi

if [ -n "$image" ]; then
    sed -i '/^DDT_IMAGE=/d' "$directory/.env"
    say "DDT_IMAGE=$image" >>"$directory/.env"
fi

port=$(sed -n 's/^DDT_PORT=//p' "$directory/.env")
port=${port:-8443}

# HTTPS for browsers and agents, HTTP boot, ProxyDHCP, TFTP and the PXE boot server. A TFTP transfer answers from a
# port of its own, which a firewall that tracks connections lets back in.
tcp_ports="$port 8080"
udp_ports="67 69 4011"

active=
if command -v ufw >/dev/null 2>&1 && ufw status 2>/dev/null | grep -q '^Status: active'; then
    active=ufw
elif command -v firewall-cmd >/dev/null 2>&1 && [ "$(firewall-cmd --state 2>/dev/null)" = running ]; then
    active=firewalld
fi

if [ -n "$active" ]; then
    # Piped into sh, the script itself is on standard input, so the question goes to the terminal.
    if [ -z "$firewall" ] && (exec </dev/tty) 2>/dev/null; then
        printf '%s is active. Open TCP %s and UDP %s for DDT? [y/N] ' "$active" "$tcp_ports" "$udp_ports" >/dev/tty
        read -r reply </dev/tty || reply=
        case "$reply" in
            [Yy]*) firewall=yes ;;
        esac
    fi

    if [ "$firewall" = yes ]; then
        for tcp in $tcp_ports; do
            if [ "$active" = ufw ]; then ufw allow "$tcp/tcp" comment DDT >/dev/null; else firewall-cmd --quiet --permanent --add-port="$tcp/tcp"; fi
        done
        for udp in $udp_ports; do
            if [ "$active" = ufw ]; then ufw allow "$udp/udp" comment DDT >/dev/null; else firewall-cmd --quiet --permanent --add-port="$udp/udp"; fi
        done
        [ "$active" = ufw ] || firewall-cmd --quiet --reload
        say "Opened TCP $tcp_ports and UDP $udp_ports in $active."
    else
        say "$active is active and stays as it is, so it blocks DDT. To open it later: TCP $tcp_ports and UDP $udp_ports, or run this again with --firewall yes."
    fi
fi

say "Starting DDT"
compose up --detach --remove-orphans

say "Waiting for DDT to answer on port $port"
attempt=0
until ask_server /api/about >/dev/null 2>&1; do
    attempt=$((attempt + 1))
    if [ "$attempt" -ge 60 ]; then
        compose logs --tail 30 ddt >&2 || true
        fail "DDT does not answer on port $port after two minutes. Its last lines are above."
    fi
    sleep 2
done

host=$(hostname -f 2>/dev/null || hostname)
say ""
say "DDT runs at https://$host:$port/"

# The root's SHA-256 is that of the certificate, not of the file around it.
if ask_server /api/about/root-certificate >"$work/root.pem" 2>/dev/null; then
    fingerprint=$(sed '/^-----/d' "$work/root.pem" | base64 -d 2>/dev/null | sha256sum | awk '{ print toupper($1) }')
    say "Its root's SHA-256: $fingerprint"
fi

if compose exec -T ddt test -f /var/lib/ddt/first-admin.txt 2>/dev/null; then
    say "Sign in as admin. This shows the password:"
    say "  sudo docker compose --project-directory $directory exec ddt cat /var/lib/ddt/first-admin.txt"
fi
