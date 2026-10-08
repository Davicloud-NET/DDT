#!/usr/bin/env python3
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Writes ddt-linux-boot.json: the boot environment's files, what it was built from and who signed the loaders.
#
#   manifest.py packages.lock.json <output folder>
#
# The output folder holds the finished files and contents.json from assemble.py, which this replaces.

import json
import os
import re
import struct
import subprocess
import sys

from fetch import sha256_of

# The server reads this file. A change it could not read as before needs a new number here.
SCHEMA = 1
FILES = ["shimx64.efi", "grubx64.efi", "vmlinuz", "base.img", "ddt.img"]
SIGNED = ["shimx64.efi", "grubx64.efi", "vmlinuz"]
TOOLS = ["kmod", "zstd", "libzstd1", "kbd", "python3", "xz-utils", "dpkg"]
CERTIFICATE = re.compile(r"-----BEGIN CERTIFICATE-----.*?-----END CERTIFICATE-----", re.DOTALL)


# The Authenticode signatures of a PE file, each a PKCS #7 block in DER.
def signatures_of(path):
    with open(path, "rb") as source:
        data = source.read()
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    optional = pe + 24
    magic = struct.unpack_from("<H", data, optional)[0]
    # The certificate table is the fifth data directory; they start later in PE32+.
    directories = optional + (112 if magic == 0x20B else 96)
    offset, size = struct.unpack_from("<II", data, directories + 4 * 8)
    end = offset + size
    while offset + 8 <= end:
        length = struct.unpack_from("<I", data, offset)[0]
        if length < 8:
            break
        yield data[offset + 8:offset + length]
        offset += (length + 7) & ~7


def common_name(name):
    found = re.search(r"commonName\s*=\s*(.+)", name)
    return found.group(1).strip() if found else " ".join(name.split())


# The certificate that signed is the one no other certificate in the block was issued by.
def signer_of(signature):
    listed = subprocess.run(["openssl", "pkcs7", "-inform", "DER", "-print_certs"], input=signature, capture_output=True,
                            check=True).stdout.decode("utf-8")
    names = []
    for certificate in CERTIFICATE.findall(listed):
        text = subprocess.run(["openssl", "x509", "-noout", "-subject", "-issuer", "-nameopt", "multiline,utf8"],
                              input=certificate.encode("ascii"), capture_output=True, check=True).stdout.decode("utf-8")
        subject, _, issuer = text.partition("issuer=")
        names.append((common_name(subject), common_name(issuer)))
    issuers = {issuer for _, issuer in names}
    leaves = [(subject, issuer) for subject, issuer in names if subject not in issuers] or names
    return {"signer": leaves[0][0], "authority": leaves[0][1]}


def file_entry(folder, name):
    path = os.path.join(folder, name)
    entry = {"name": name, "size": os.path.getsize(path), "sha256": sha256_of(path)}
    if name in SIGNED:
        entry["signatures"] = [signer_of(block) for block in signatures_of(path)]
    return entry


def tool_versions():
    versions = {}
    for tool in TOOLS:
        listed = subprocess.run(["dpkg-query", "-W", "-f", "${Version}", tool], capture_output=True, text=True, check=True)
        versions[tool] = listed.stdout.strip()
    return versions


def main():
    lock_path, folder = sys.argv[1], sys.argv[2]
    with open(lock_path, encoding="utf-8") as source:
        lock = json.load(source)
    contents_path = os.path.join(folder, "contents.json")
    with open(contents_path, encoding="utf-8") as source:
        contents = json.load(source)
    files = [file_entry(folder, name) for name in FILES]
    manifest = {
        "schema": SCHEMA,
        "snapshot": lock["snapshot"],
        "kernelRelease": lock["kernelRelease"],
        # The certificate authorities a firmware must trust one of to start the shim.
        "shimSignedUnder": sorted({signature["authority"] for signature in files[0]["signatures"]}),
        "files": files,
        "packages": [{key: package[key] for key in ["name", "version", "suite", "source", "sourceVersion", "use"]}
                     for package in lock["packages"]],
        "tools": tool_versions(),
        "baseImage": contents,
    }
    with open(os.path.join(folder, "ddt-linux-boot.json"), "w", encoding="utf-8", newline="\n") as target:
        json.dump(manifest, target, indent=2)
        target.write("\n")
    os.remove(contents_path)


if __name__ == "__main__":
    main()
