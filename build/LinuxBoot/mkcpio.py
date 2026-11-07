#!/usr/bin/env python3
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Writes a folder as a cpio archive (newc) that is the same bytes wherever it is built.
#
#   mkcpio.py --mtime <unix seconds> <folder> <archive>
#
# Only names, contents and link targets come from the file system: paths are sorted by their bytes, owner and
# group are 0, every entry has the given time, inode numbers count up from 1 and nothing is a hard link.

import argparse
import os
import stat
import sys

FOLDER = stat.S_IFDIR | 0o755
LINK = stat.S_IFLNK | 0o777
PROGRAM = stat.S_IFREG | 0o755
DATA = stat.S_IFREG | 0o644


# The mode comes from the content, because a Windows checkout has no executable bit.
def is_program(name, start):
    if start[:2] == b"#!":
        return True
    # Kernel modules are ELF files too, and nothing runs them.
    return start[:4] == b"\x7fELF" and not name.endswith(".ko")


def entries_of(root):
    found = []
    for folder, folders, files in os.walk(root):
        relative = os.path.relpath(folder, root)
        prefix = "" if relative == "." else relative.replace(os.sep, "/") + "/"
        # os.walk lists a link to a folder among the folders and does not follow it.
        for name in folders + files:
            found.append(prefix + name)
    return sorted(found, key=lambda path: path.encode("utf-8"))


def header(inode, mode, links, mtime, size, name):
    fields = (inode, mode, 0, 0, links, mtime, size, 0, 0, 0, 0, len(name) + 1, 0)
    return b"070701" + b"".join(b"%08X" % field for field in fields)


def padded(block):
    return block + b"\0" * (-len(block) % 4)


def write_entry(target, inode, mode, links, mtime, name, data):
    encoded = name.encode("utf-8")
    target.write(padded(header(inode, mode, links, mtime, len(data), encoded) + encoded + b"\0"))
    target.write(padded(data))


def write_archive(root, target, mtime):
    paths = entries_of(root)
    folders = {path for path in paths if os.path.isdir(os.path.join(root, path)) and not os.path.islink(os.path.join(root, path))}
    for inode, path in enumerate(paths, start=1):
        full = os.path.join(root, path)
        if os.path.islink(full):
            write_entry(target, inode, LINK, 1, mtime, path, os.readlink(full).encode("utf-8"))
        elif path in folders:
            children = sum(1 for other in folders if other.rpartition("/")[0] == path)
            write_entry(target, inode, FOLDER, 2 + children, mtime, path, b"")
        elif os.path.isfile(full):
            with open(full, "rb") as source:
                data = source.read()
            mode = PROGRAM if is_program(path, data[:4]) else DATA
            write_entry(target, inode, mode, 1, mtime, path, data)
        else:
            sys.exit("%s is neither a file, a folder nor a link" % path)
    target.write(padded(header(0, 0, 1, 0, 0, b"TRAILER!!!") + b"TRAILER!!!\0"))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--mtime", type=int, required=True)
    parser.add_argument("folder")
    parser.add_argument("archive")
    arguments = parser.parse_args()
    with open(arguments.archive, "wb") as target:
        write_archive(arguments.folder, target, arguments.mtime)


if __name__ == "__main__":
    main()
