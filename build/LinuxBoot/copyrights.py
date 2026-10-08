#!/usr/bin/env python3
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Writes what licenses/linux-boot holds for the lock: Debian's copyright file of every source package, as
# <source>.copyright, and the licence texts those files point to. Update-LinuxBootLock.ps1 runs it.
#
#   copyrights.py packages.lock.json <packages folder> <output folder>

import json
import os
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile

from fetch import download_locked

DOCUMENTS = "./usr/share/doc/"
# Where Debian keeps the licences many packages share, and where their copyright files point instead of quoting.
COMMON = "/usr/share/common-licenses/"
POINTER = re.compile(re.escape(COMMON) + r"([A-Za-z0-9.+-]*[A-Za-z0-9])")


# A package's copyright file, or the name of the package whose documents folder its own is a link to.
def in_package(folder, name):
    unpacking = subprocess.Popen(["dpkg-deb", "--fsys-tarfile", os.path.join(folder, name + ".deb")], stdout=subprocess.PIPE)
    try:
        with tarfile.open(fileobj=unpacking.stdout, mode="r|") as files:
            for entry in files:
                if entry.name == DOCUMENTS + name and entry.issym():
                    return None, entry.linkname
                if entry.name == DOCUMENTS + name + "/copyright":
                    return files.extractfile(entry).read(), None
    finally:
        unpacking.kill()
        unpacking.wait()
    sys.exit("%s has no copyright file" % name)


# debian/copyright out of a source package, for a source no package of the lock was built from directly.
def in_source(lock, source, work):
    packaging = [file for file in source["files"] if re.search(r"\.debian\.tar\.[a-z0-9]+$", file["name"])]
    file = (packaging or [file for file in source["files"] if ".tar." in file["name"]])[0]
    path = os.path.join(work, file["name"])
    url = "%s/%s/%s/%s" % (lock["archive"], lock["snapshot"], source["directory"], file["name"])
    download_locked(url, path, file["size"], file["sha256"])
    with tarfile.open(path) as files:
        for entry in files:
            if entry.name == "debian/copyright" or entry.name.endswith("/debian/copyright"):
                return files.extractfile(entry).read()
    sys.exit("%s has no debian/copyright" % file["name"])


def from_packages(lock, folder):
    texts = {}
    for package in lock["packages"]:
        text, link = in_package(folder, package["name"])
        if link:
            text, _ = in_package(folder, link)
        key = (package["source"], package["sourceVersion"])
        # One file stands for every package of a source, so they must agree.
        if texts.setdefault(key, text) != text:
            sys.exit("The packages built from %s differ in their copyright files" % package["source"])
    return texts


def main():
    lock_path, folder, output = sys.argv[1:4]
    with open(lock_path, encoding="utf-8") as source:
        lock = json.load(source)
    texts = from_packages(lock, folder)
    os.makedirs(os.path.join(output, "common-licenses"), exist_ok=True)
    pointers = set()
    written = {}
    with tempfile.TemporaryDirectory() as work:
        for source in lock["sources"]:
            text = texts.get((source["name"], source["version"])) or in_source(lock, source, work)
            # Two versions of one source share the file as long as its text is the same.
            if written.setdefault(source["name"], text) != text:
                sys.exit("The versions of %s in the lock differ in their copyright files" % source["name"])
            with open(os.path.join(output, source["name"] + ".copyright"), "wb") as target:
                target.write(text)
            pointers.update(POINTER.findall(text.decode("utf-8", errors="replace")))
    for name in sorted(pointers):
        shutil.copyfile(COMMON + name, os.path.join(output, "common-licenses", name))


if __name__ == "__main__":
    main()
