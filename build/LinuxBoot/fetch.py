#!/usr/bin/env python3
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Downloads the packages of packages.lock.json from the snapshot and refuses any that differ from the lock.
#
#   fetch.py packages.lock.json <folder>

import hashlib
import json
import os
import sys
import time
import urllib.request


def download(url, path):
    # snapshot.debian.org answers 5xx now and then.
    for attempt in range(5):
        try:
            with urllib.request.urlopen(url, timeout=120) as response, open(path, "wb") as target:
                while True:
                    block = response.read(1 << 20)
                    if not block:
                        return
                    target.write(block)
        except OSError as error:
            if attempt == 4:
                raise
            print("  again after: %s" % error, flush=True)
            time.sleep(5 * (attempt + 1))


def sha256_of(path):
    digest = hashlib.sha256()
    with open(path, "rb") as source:
        for block in iter(lambda: source.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


# Downloads one file the lock names and keeps it only when it has the lock's size and hash.
def download_locked(url, path, size, sha256):
    download(url, path)
    found = (os.path.getsize(path), sha256_of(path))
    if found != (size, sha256):
        os.remove(path)
        sys.exit("%s is not the file in the lock: %d bytes, sha256 %s" % (os.path.basename(path), found[0], found[1]))


def main():
    lock_path, folder = sys.argv[1], sys.argv[2]
    with open(lock_path, encoding="utf-8") as source:
        lock = json.load(source)
    os.makedirs(folder, exist_ok=True)
    for package in lock["packages"]:
        print("%s %s" % (package["name"], package["version"]), flush=True)
        url = "%s/%s/%s" % (lock["archive"], lock["snapshot"], package["path"])
        download_locked(url, os.path.join(folder, package["name"] + ".deb"), package["size"], package["sha256"])


if __name__ == "__main__":
    main()
