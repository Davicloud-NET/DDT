#!/usr/bin/env python3
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Lays out the file tree of base.img from the packages fetch.py downloaded, and takes the three boot files out of them.
#
#   assemble.py <build/LinuxBoot> <packages folder> <tree> <output folder>
#
# The output folder also gets contents.json, which manifest.py works into ddt-linux-boot.json.

import json
import lzma
import os
import shutil
import subprocess
import sys

SIGNED = b"~Module signature appended~\n"
LIBRARIES = "usr/lib/x86_64-linux-gnu"
# libdl, libpthread and librt are empty since glibc 2.34, but programs linked against an older glibc name them.
GLIBC = ["ld-linux-x86-64.so.2", "libc.so.6", "libm.so.6", "libdl.so.2", "libpthread.so.0", "librt.so.1"]
TERMINALS = ["a/ansi", "d/dumb", "l/linux", "s/screen", "v/vt100", "v/vt102", "v/vt220", "x/xterm", "x/xterm-256color"]
FIRMWARE_PACKAGES = ["firmware-realtek", "firmware-bnx2", "firmware-bnx2x", "firmware-misc-nonfree"]
MODULE_INDEXES = ["modules.alias", "modules.builtin", "modules.dep", "modules.devname", "modules.order", "modules.softdep",
                  "modules.symbols"]
MARKER = "etc/ddt/boot-environment"


def run(*command, **options):
    return subprocess.run(command, check=True, **options)


def lines_of(path):
    with open(path, encoding="utf-8") as source:
        for line in source:
            line = line.split("#", 1)[0].strip()
            if line:
                yield line


def module_name(path):
    return os.path.basename(path).split(".ko")[0].replace("-", "_")


def tree_size(path):
    total = 0
    for folder, _, files in os.walk(path):
        total += sum(os.lstat(os.path.join(folder, name)).st_size for name in files)
    return total


class Assembly:
    def __init__(self, source, packages, tree, output):
        self.source = source
        self.packages = packages
        self.tree = tree
        self.output = output
        with open(os.path.join(source, "packages.lock.json"), encoding="utf-8") as lock:
            self.lock = json.load(lock)
        self.kernel = self.lock["kernelRelease"]
        self.sizes = {}
        self.contents = {}

    def unpacked(self, package):
        return os.path.join(self.packages, "unpacked", package)

    def unpack(self):
        for package in self.lock["packages"]:
            target = self.unpacked(package["name"])
            os.makedirs(target)
            run("dpkg-deb", "-x", os.path.join(self.packages, package["name"] + ".deb"), target)

    def boot_files(self):
        os.makedirs(self.output, exist_ok=True)
        files = {
            "shimx64.efi": "shim-signed/usr/lib/shim/shimx64.efi.signed",
            # The network image: its prefix is /grub on the server GRUB was loaded from.
            "grubx64.efi": "grub-efi-amd64-signed/usr/lib/grub/x86_64-efi-signed/grubnetx64.efi.signed",
            "vmlinuz": "linux-binary-%s/usr/lib/modules/%s/vmlinuz" % (self.kernel, self.kernel),
        }
        for name, path in files.items():
            shutil.copyfile(os.path.join(self.packages, "unpacked", path), os.path.join(self.output, name))

    def check_kernel_config(self):
        path = os.path.join(self.unpacked("linux-base-" + self.kernel), "usr/lib/modules", self.kernel, "config")
        with open(path, encoding="utf-8") as source:
            config = {line.strip() for line in source}
        missing = [line for line in lines_of(os.path.join(self.source, "kernel-config.txt")) if line not in config]
        if missing:
            sys.exit("The kernel's config lacks: " + ", ".join(missing))

    # Indexes the whole module package, so that the choice below can follow what each module depends on.
    def index_all_modules(self):
        base = os.path.join(self.unpacked("linux-modules-" + self.kernel), "usr")
        folder = os.path.join(base, "lib/modules", self.kernel)
        described = os.path.join(self.unpacked("linux-base-" + self.kernel), "usr/lib/modules", self.kernel)
        for name in ["modules.builtin", "modules.builtin.modinfo", "modules.order"]:
            shutil.copyfile(os.path.join(described, name), os.path.join(folder, name))
        run("depmod", "-b", base, self.kernel)
        return folder

    def wanted_modules(self):
        group = None
        with open(os.path.join(self.source, "modules.txt"), encoding="utf-8") as source:
            for line in source:
                line = line.split("#", 1)[0].strip()
                if line.startswith("["):
                    group = line.strip("[]")
                elif line:
                    yield group, line.replace("-", "_")

    def choose_modules(self, folder):
        files = {}
        for directory, _, names in os.walk(os.path.join(folder, "kernel")):
            for name in names:
                files[module_name(name)] = os.path.relpath(os.path.join(directory, name), folder)
        built_in = {module_name(line) for line in lines_of(os.path.join(folder, "modules.builtin"))}
        needs = {}
        with open(os.path.join(folder, "modules.dep"), encoding="utf-8") as source:
            for line in source:
                module, _, others = line.partition(":")
                needs[module_name(module)] = [module_name(other) for other in others.split()]
        with open(os.path.join(folder, "modules.softdep"), encoding="utf-8") as source:
            for line in source:
                words = line.split()
                if words and words[0] == "softdep":
                    needs.setdefault(module_name(words[1]), []).extend(
                        module_name(word) for word in words[2:] if not word.endswith(":"))

        chosen = {}
        named_built_in = []
        unknown = []

        def add(name, group):
            if name in chosen or name not in files:
                return
            chosen[name] = group
            for other in needs.get(name, []):
                add(other, "dependencies")

        for group, name in self.wanted_modules():
            if name in files:
                # A display driver would take the screen from the firmware's, which every machine keeps.
                if files[name].startswith("kernel/drivers/gpu/"):
                    sys.exit("modules.txt names the display driver %s" % name)
                # A module named in the list keeps its group, even when another one pulled it in first.
                chosen.pop(name, None)
                add(name, group)
            elif name in built_in:
                named_built_in.append(name)
            else:
                unknown.append(name)
        if unknown:
            sys.exit("modules.txt names what this kernel does not have: " + ", ".join(unknown))
        self.contents["builtIn"] = sorted(named_built_in)
        return {name: (files[name], group) for name, group in chosen.items()}

    def copy_modules(self, folder, chosen):
        target_folder = os.path.join(self.tree, "usr/lib/modules", self.kernel)
        firmware = {}
        by_group = {}
        for name in sorted(chosen):
            path, group = chosen[name]
            source = os.path.join(folder, path)
            target = os.path.join(target_folder, path[:-len(".xz")])
            os.makedirs(os.path.dirname(target), exist_ok=True)
            with lzma.open(source) as packed:
                data = packed.read()
            # Without the signature at its end, a kernel under Secure Boot refuses the module.
            if not data.endswith(SIGNED):
                sys.exit("%s is not signed" % path)
            with open(target, "wb") as unpacked:
                unpacked.write(data)
            by_group.setdefault(group, []).append(name)
            self.sizes["modules." + group] = self.sizes.get("modules." + group, 0) + len(data)
            named = run("modinfo", "-F", "firmware", source, capture_output=True, text=True).stdout.split()
            if named:
                firmware[name] = named
        relative = {os.path.splitext(path)[0] for path, _ in chosen.values()}
        with open(os.path.join(folder, "modules.order"), encoding="utf-8") as order:
            kept = [line for line in order if line.strip() in relative]
        with open(os.path.join(target_folder, "modules.order"), "w", encoding="utf-8", newline="\n") as order:
            order.writelines(kept)
        for name in ["modules.builtin", "modules.builtin.modinfo"]:
            shutil.copyfile(os.path.join(folder, name), os.path.join(target_folder, name))
        run("depmod", "-b", os.path.join(self.tree, "usr"), self.kernel)
        # busybox reads the text indexes only.
        for name in os.listdir(target_folder):
            if name.startswith("modules.") and name not in MODULE_INDEXES:
                os.remove(os.path.join(target_folder, name))
        # busybox modprobe reads soft dependencies from its configuration only.
        os.makedirs(os.path.join(self.tree, "etc/modprobe.d"), exist_ok=True)
        with open(os.path.join(target_folder, "modules.softdep"), encoding="utf-8") as source:
            soft = [line for line in source if line.startswith("softdep ")]
        with open(os.path.join(self.tree, "etc/modprobe.d/softdep.conf"), "w", encoding="utf-8", newline="\n") as target:
            target.writelines(soft)
        self.contents["modules"] = {group: sorted(names) for group, names in sorted(by_group.items())}
        return firmware

    def copy_firmware(self, wanted):
        target_folder = os.path.join(self.tree, "usr/lib/firmware")
        included = {}
        missing = {}
        for module in sorted(wanted):
            for name in wanted[module]:
                for package in FIRMWARE_PACKAGES:
                    # realpath, because some firmware names are links to another file of the package.
                    source = os.path.realpath(os.path.join(self.unpacked(package), "usr/lib/firmware", name))
                    if os.path.isfile(source):
                        target = os.path.join(target_folder, name)
                        os.makedirs(os.path.dirname(target), exist_ok=True)
                        shutil.copyfile(source, target)
                        included.setdefault(package, []).append(name)
                        break
                else:
                    missing.setdefault(module, []).append(name)
        self.sizes["firmware"] = tree_size(target_folder)
        self.contents["firmware"] = {package: sorted(set(names)) for package, names in sorted(included.items())}
        self.contents["firmwareNotIncluded"] = missing

    def copy_busybox(self):
        folder = os.path.join(self.tree, "usr/bin")
        os.makedirs(folder, exist_ok=True)
        busybox = os.path.join(folder, "busybox")
        shutil.copyfile(os.path.join(self.unpacked("busybox-static"), "usr/bin/busybox"), busybox)
        os.chmod(busybox, 0o755)
        for applet in run(busybox, "--list", capture_output=True, text=True).stdout.split():
            if applet != "busybox":
                os.symlink("busybox", os.path.join(folder, applet))
        self.sizes["busybox"] = os.path.getsize(busybox)

    def copy_glibc(self):
        folder = os.path.join(self.tree, LIBRARIES)
        os.makedirs(folder, exist_ok=True)
        for name in GLIBC:
            shutil.copyfile(os.path.join(self.unpacked("libc6"), LIBRARIES, name), os.path.join(folder, name))
        os.makedirs(os.path.join(self.tree, "usr/lib64"), exist_ok=True)
        os.symlink("../lib/x86_64-linux-gnu/ld-linux-x86-64.so.2", os.path.join(self.tree, "usr/lib64/ld-linux-x86-64.so.2"))
        self.sizes["glibc"] = tree_size(folder)

    def copy_terminfo(self):
        folder = os.path.join(self.tree, "usr/share/terminfo")
        for name in TERMINALS:
            target = os.path.join(folder, name)
            os.makedirs(os.path.dirname(target), exist_ok=True)
            shutil.copyfile(os.path.join(self.unpacked("ncurses-base"), "usr/share/terminfo", name), target)
        self.sizes["terminfo"] = tree_size(folder)

    def make_keymaps(self):
        folder = os.path.join(self.tree, "usr/share/ddt/keymaps")
        os.makedirs(folder, exist_ok=True)
        maps = os.path.join(self.unpacked("console-data"), "usr/share/keymaps/i386")
        names = []
        for line in lines_of(os.path.join(self.source, "keymaps.txt")):
            name, keymap = line.split()
            with open(os.path.join(folder, name + ".bmap"), "wb") as target:
                run("loadkeys", "-b", os.path.join(maps, keymap + ".kmap.gz"), stdout=target)
            names.append(name)
        self.sizes["keymaps"] = tree_size(folder)
        self.contents["keymaps"] = names

    def copy_rootfs(self):
        source = os.path.join(self.source, "rootfs")
        total = 0
        for folder, _, files in os.walk(source):
            for name in files:
                path = os.path.join(folder, name)
                with open(path, "rb") as script:
                    data = script.read()
                # A script with Windows line ends does not start: the kernel looks for "sh\r".
                if b"\r" in data:
                    sys.exit("%s has Windows line ends" % path)
                target = os.path.join(self.tree, os.path.relpath(path, source))
                os.makedirs(os.path.dirname(target), exist_ok=True)
                with open(target, "wb") as copy:
                    copy.write(data)
                total += len(data)
        self.sizes["scripts"] = total

    # The file a program finds DDT's boot environment by, and which build of it this is.
    def write_marker(self):
        os.makedirs(os.path.join(self.tree, os.path.dirname(MARKER)), exist_ok=True)
        with open(os.path.join(self.tree, MARKER), "w", encoding="utf-8", newline="\n") as marker:
            marker.write("snapshot=%s\nkernel=%s\n" % (self.lock["snapshot"], self.kernel))

    def link_folders(self):
        # As Debian lays it out: everything is below /usr, and the old names are links.
        os.symlink("bin", os.path.join(self.tree, "usr/sbin"))
        for name in ["bin", "sbin", "lib", "lib64"]:
            os.symlink("usr/" + name, os.path.join(self.tree, name))

    def run(self):
        self.unpack()
        self.boot_files()
        self.check_kernel_config()
        folder = self.index_all_modules()
        firmware = self.copy_modules(folder, self.choose_modules(folder))
        self.copy_firmware(firmware)
        self.copy_busybox()
        self.copy_glibc()
        self.copy_terminfo()
        self.make_keymaps()
        self.copy_rootfs()
        self.write_marker()
        self.link_folders()
        self.contents["sizes"] = dict(sorted(self.sizes.items()))
        with open(os.path.join(self.output, "contents.json"), "w", encoding="utf-8", newline="\n") as target:
            json.dump(self.contents, target, indent=2)
            target.write("\n")


if __name__ == "__main__":
    Assembly(*sys.argv[1:5]).run()
