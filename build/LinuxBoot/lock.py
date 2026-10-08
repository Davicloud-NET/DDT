#!/usr/bin/env python3
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Writes packages.lock.json again for another snapshot of Debian's archive: the lock's packages at the versions
# and hashes that snapshot has, and the source packages they were built from. Update-LinuxBootLock.ps1 runs it.
#
#   lock.py packages.lock.json <snapshot, as 20261006T203214Z> <new lock>
#
# What it writes is what every later build trusts, so it believes an index only as far as Debian signed it: each
# suite's InRelease must carry a signature of one of the archive keys in the tools image, and each index must have
# the size and SHA-256 that InRelease names for it.
#
#   lock.py --tools packages.lock.json <new lock>
#
# writes the lock again with the packages installed in the image it runs in, which the build's tools stage compares
# its own with.

import json
import lzma
import os
import re
import subprocess
import sys
import tempfile
import urllib.request

from fetch import download, sha256_of

# Debian's archive keys, as the tools image has them.
KEYRING = "/usr/share/keyrings/debian-archive-keyring.pgp"

# Their upstream tarball holds the vendors' firmware as it is, without source code. A release leaves it out.
BINARY_ONLY = "pool/non-free-firmware/"
UPSTREAM = re.compile(r"\.orig(-[a-z0-9-]+)?\.tar\.[a-z0-9]+$")


# The stanzas of a Packages or Sources index, each a dictionary of its fields.
def stanzas(path):
    entry, key = {}, None
    with lzma.open(path, "rt", encoding="utf-8") as lines:
        for line in lines:
            line = line.rstrip("\n")
            if not line:
                if entry:
                    yield entry
                entry, key = {}, None
            elif line[0] in " \t":
                entry[key] += "\n" + line.strip()
            else:
                key, _, value = line.partition(":")
                entry[key] = value.strip()
    if entry:
        yield entry


def newer(first, second):
    return subprocess.run(["dpkg", "--compare-versions", first, "gt", second], check=False).returncode == 0


# "name (= version), ..." as Built-Using has it.
def built_using(entry):
    listed = ", ".join(entry[field] for field in ["Built-Using", "Static-Built-Using"] if entry.get(field))
    found = []
    for part in filter(None, (part.strip() for part in listed.split(","))):
        name, version = re.match(r"^(\S+) \(= (.+)\)$", part).groups()
        found.append({"source": name, "version": version})
    return found


# The text of a file signed in the clear, once one of the keys in the keyring has signed it. sqv writes nothing
# of a text whose signature does not hold.
def signed_text(path, keyring=KEYRING):
    text = path + ".signed"
    if os.path.exists(text):
        os.remove(text)
    checked = subprocess.run(["sqv", "--keyring", keyring, "--cleartext", "--output", text, path],
                             check=False, capture_output=True, text=True)
    if checked.returncode != 0 or not os.path.exists(text):
        sys.exit("%s is not signed by one of Debian's archive keys, so nothing in it is believed: %s" % (
            path, (checked.stderr or checked.stdout).strip().replace("\n", " ")))
    with open(text, encoding="utf-8") as source:
        return source.read()


# The size and SHA-256 of each file a Release names, by its path below the suite.
def release_files(text):
    files, listed = {}, False
    for line in text.split("\n"):
        if not line.startswith(" "):
            listed = line.strip() == "SHA256:"
        elif listed:
            sha256, size, name = line.split()
            files[name] = (int(size), sha256)
    return files


class Snapshot:
    def __init__(self, archive, stamp, architecture, work):
        self.archive = archive
        self.architecture = architecture
        self.work = work
        self.releases = {}
        # The archive answers a time between two snapshots with the earlier one, and that one's name is kept.
        with urllib.request.urlopen("%s/%s/" % (archive, stamp), timeout=120) as response:
            self.stamp = re.search(r"/(\d{8}T\d{6}Z)/?$", response.geturl()).group(1)

    # What the suite's signed Release says of its indexes.
    def release(self, suite):
        if suite not in self.releases:
            path = os.path.join(self.work, "%s_InRelease" % suite)
            download("%s/%s/dists/%s/InRelease" % (self.archive, self.stamp, suite), path)
            self.releases[suite] = release_files(signed_text(path))
        return self.releases[suite]

    def index(self, suite, component, kind):
        name = "%s/%s.xz" % (component, kind)
        path = os.path.join(self.work, "%s_%s_%s.xz" % (suite, component, kind.replace("/", "_")))
        if not os.path.exists(path):
            download("%s/%s/dists/%s/%s" % (self.archive, self.stamp, suite, name), path)
            signed = self.release(suite).get(name)
            found = (os.path.getsize(path), sha256_of(path))
            if signed != found:
                os.remove(path)
                sys.exit("%s of %s is not the index Debian signed: %s, and the signed Release names %s" % (
                    name, suite, "%d bytes, sha256 %s" % found, "%d bytes, sha256 %s" % signed if signed else "no such file"))
        return stanzas(path)

    # The newest version of each named package in one suite and component.
    def packages(self, suite, component, names):
        found = {}
        for entry in self.index(suite, component, "binary-%s/Packages" % self.architecture):
            name = entry["Package"]
            if name in names and (name not in found or newer(entry["Version"], found[name]["Version"])):
                found[name] = entry
        return found

    def sources(self, suite, component, wanted):
        for entry in self.index(suite, component, "source/Sources"):
            if (entry["Package"], entry["Version"]) in wanted:
                yield entry


# The kernel packages carry the kernel's release in their name, which the meta package of the same suite names.
def kernel_release(snapshot, lock):
    old = lock["kernelRelease"]
    suite, component = next((package["suite"], package["component"]) for package in lock["packages"] if package["name"].endswith(old))
    meta = "linux-image-" + lock["architecture"]
    depends = snapshot.packages(suite, component, {meta})[meta]["Depends"]
    return re.search(r"\blinux-image-(\S+)", depends).group(1)


def resolve_packages(snapshot, lock, release):
    wanted = {}
    for package in lock["packages"]:
        name = package["name"].replace(lock["kernelRelease"], release)
        wanted.setdefault((package["suite"], package["component"]), {})[name] = package
    resolved = {}
    for (suite, component), packages in wanted.items():
        found = snapshot.packages(suite, component, set(packages))
        missing = sorted(set(packages) - set(found))
        if missing:
            sys.exit("%s/%s at %s has no %s" % (suite, component, snapshot.stamp, ", ".join(missing)))
        for name, entry in found.items():
            source = re.match(r"^(\S+)(?: \((.+)\))?$", entry.get("Source", name))
            resolved[packages[name]["name"]] = {
                "name": name,
                "version": entry["Version"],
                "suite": suite,
                "component": component,
                "path": entry["Filename"],
                "sha256": entry["SHA256"],
                "size": int(entry["Size"]),
                "use": packages[name]["use"],
                "source": source.group(1),
                "sourceVersion": source.group(2) or entry["Version"],
                "builtUsing": built_using(entry),
            }
    return [resolved[package["name"]] for package in lock["packages"]]


def source_entry(entry):
    files = []
    for line in entry["Checksums-Sha256"].split("\n"):
        if line:
            sha256, size, name = line.split()
            files.append({"name": name, "size": int(size), "sha256": sha256})
    source = {"name": entry["Package"], "version": entry["Version"], "directory": entry["Directory"]}
    left_out = [file for file in files if entry["Directory"].startswith(BINARY_ONLY) and UPSTREAM.search(file["name"])]
    source["files"] = [file for file in files if file not in left_out]
    if left_out:
        source["leftOut"] = left_out
    return source


def resolve_sources(snapshot, packages):
    wanted = {(package["source"], package["sourceVersion"]) for package in packages}
    wanted |= {(used["source"], used["version"]) for package in packages for used in package["builtUsing"]}
    found = {}
    for suite, component in sorted({(package["suite"], package["component"]) for package in packages}):
        for entry in snapshot.sources(suite, component, wanted):
            found[(entry["Package"], entry["Version"])] = source_entry(entry)
    missing = sorted(wanted - set(found))
    if missing:
        sys.exit("The Sources indexes at %s lack: %s" % (snapshot.stamp, ", ".join("%s %s" % source for source in missing)))
    return [found[source] for source in sorted(found)]


# Every package installed in this image with its version, which is what the build's tools are.
def installed_packages():
    listed = subprocess.run(["dpkg-query", "--show", "--showformat", "${Package} ${Version}\\n"],
                            check=True, capture_output=True, text=True).stdout
    return dict(line.split() for line in sorted(listed.splitlines()) if line)


def write(lock, target):
    os.makedirs(os.path.dirname(os.path.abspath(target)), exist_ok=True)
    with open(target, "w", encoding="utf-8", newline="\n") as file:
        json.dump(lock, file, indent=2)
        file.write("\n")


def main():
    if sys.argv[1] == "--tools":
        with open(sys.argv[2], encoding="utf-8") as source:
            lock = json.load(source)
        lock["expected"]["toolsPackages"] = installed_packages()
        write(lock, sys.argv[3])
        return
    lock_path, stamp, target = sys.argv[1:4]
    with open(lock_path, encoding="utf-8") as source:
        lock = json.load(source)
    with tempfile.TemporaryDirectory() as work:
        snapshot = Snapshot(lock["archive"], stamp, lock["architecture"], work)
        release = kernel_release(snapshot, lock)
        packages = resolve_packages(snapshot, lock, release)
        sources = resolve_sources(snapshot, packages)
    lock.update(snapshot=snapshot.stamp, kernelRelease=release, packages=packages, sources=sources)
    write(lock, target)


if __name__ == "__main__":
    main()
