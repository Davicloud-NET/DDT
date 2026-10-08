#!/usr/bin/env python3
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# A virtual PC for run.py: QEMU with Secure Boot on, which netboots the boot environment from QEMU's own TFTP
# server and writes what its serial port says into a file.

import os
import re
import shutil
import socket
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mkcpio  # noqa: E402

FIRMWARE = "/usr/share/OVMF/OVMF_CODE_4M.ms.fd"
FIRMWARE_VARIABLES = "/usr/share/OVMF/OVMF_VARS_4M.ms.fd"
WORK = "/tmp/ddt-linux-boot"
# QEMU's own network: it is the DHCP and TFTP server at this address.
SERVER = "10.0.2.2"
ADDRESS = "52:54:00:dd:70:01"
# As the server writes it, without a root certificate: no server answers at this address.
AGENT_JSON = '{"serverUrl":"https://%s:8443"}' % SERVER
BOOT_FILES = ["shimx64.efi", "grubx64.efi", "vmlinuz", "base.img", "ddt.img"]
ESCAPES = re.compile(r"\x1b\[[0-9;?]*[A-Za-z]|\x1b[()][0-9A-B]|\r")


def archive(tree, path):
    with open(path, "wb") as target:
        mkcpio.write_archive(tree, target, int(time.time()))


class Machine:
    # server_time=False leaves ddt.time out. screen=True makes the screen /dev/console, as on a PC without a
    # serial console; otherwise it is the serial port, where the start scripts' lines can then be read. init is
    # the first program the kernel starts.
    def __init__(self, name, boot, out, arguments="", initrds=(), tamper=None, server_time=True, screen=False, init="/init"):
        self.name = name
        self.out = out
        self.folder = os.path.join(WORK, name)
        self.tftp = os.path.join(self.folder, "tftp")
        self.serial = os.path.join(self.folder, "serial.log")
        self.process = None
        self.command = []
        self.started = 0.0
        shutil.rmtree(self.folder, ignore_errors=True)
        os.makedirs(os.path.join(self.tftp, "linux"))
        os.makedirs(os.path.join(self.tftp, "grub"))
        for file in BOOT_FILES:
            shutil.copyfile(os.path.join(boot, file), os.path.join(self.tftp, "linux", file))
        if tamper:
            self.change_one_byte(os.path.join(self.tftp, "linux", tamper))
        self.write_site_archive()
        names = ["base.img", "ddt.img", "site.cpio"]
        for path in initrds:
            shutil.copyfile(path, os.path.join(self.tftp, "linux", os.path.basename(path)))
            names.append(os.path.basename(path))
        self.write_grub_config(arguments, names, server_time, screen, init)
        shutil.copyfile(FIRMWARE_VARIABLES, os.path.join(self.folder, "variables.fd"))
        # 8 GB, of which the file system keeps only what is written.
        with open(os.path.join(self.folder, "disk.img"), "wb") as disk:
            disk.truncate(8 << 30)

    @staticmethod
    def change_one_byte(path):
        with open(path, "r+b") as file:
            file.seek(os.path.getsize(path) // 2)
            byte = file.read(1)
            file.seek(-1, os.SEEK_CUR)
            file.write(bytes([byte[0] ^ 0x01]))

    # What the server will make for each machine: its address and root certificate as /ddt/agent.json.
    def write_site_archive(self):
        tree = os.path.join(self.folder, "site")
        os.makedirs(os.path.join(tree, "ddt"))
        with open(os.path.join(tree, "ddt", "agent.json"), "w", encoding="utf-8", newline="\n") as file:
            file.write(AGENT_JSON + "\n")
        archive(tree, os.path.join(self.tftp, "linux", "site.cpio"))

    # As the server will generate it, but over TFTP only. Debian's network GRUB asks the server it came from
    # for /grub/grub.cfg.
    def write_grub_config(self, arguments, initrds, server_time, screen, init):
        kernel = ["rdinit=" + init, "loglevel=4", "ddt.bootif=${net_default_mac}", "ddt.keymap=de"]
        if server_time:
            kernel.append("ddt.time=%d" % int(time.time()))
        # The last console named is /dev/console.
        kernel += ["console=ttyS0,115200", "console=tty0"] if screen else ["console=tty0", "console=ttyS0,115200"]
        lines = [
            "set timeout=0",
            "linux (tftp,%s)/linux/vmlinuz %s %s" % (SERVER, " ".join(kernel), arguments),
            "initrd " + " ".join("(tftp,%s)/linux/%s" % (SERVER, name) for name in initrds),
            "boot",
        ]
        with open(os.path.join(self.tftp, "grub", "grub.cfg"), "w", encoding="utf-8", newline="\n") as file:
            file.write("\n".join(lines) + "\n")

    # network_first puts the network card first for this start only; without it the firmware's own order decides.
    def start(self, devices, network_first=True):
        kvm = os.path.exists("/dev/kvm")
        command = [
            "qemu-system-x86_64", "-nodefaults", "-machine", "q35,smm=on", "-m", "2048", "-smp", "2",
            "-accel", "kvm" if kvm else "tcg", "-cpu", "host" if kvm else "max",
            # Secure Boot needs the variables in flash only the firmware's own code can write.
            "-global", "driver=cfi.pflash01,property=secure,value=on",
            "-drive", "if=pflash,format=raw,unit=0,readonly=on,file=" + FIRMWARE,
            "-drive", "if=pflash,format=raw,unit=1,file=" + os.path.join(self.folder, "variables.fd"),
            "-netdev", "user,id=boot,tftp=%s,bootfile=linux/shimx64.efi" % self.tftp,
            # No option ROM: the firmware has its own virtio network driver, and would refuse an unsigned ROM anyway.
            "-device", "virtio-net-pci,netdev=boot,mac=%s,romfile=%s" % (ADDRESS, ",bootindex=1" if network_first else ""),
            "-display", "none", "-serial", "file:" + self.serial,
            "-monitor", "unix:%s,server=on,wait=off" % os.path.join(self.folder, "monitor"),
            "-drive", "file=%s,if=none,id=disk,format=raw" % os.path.join(self.folder, "disk.img"),
            "-device", "nvme,drive=disk,serial=DDT0001",
        ] + list(devices)
        self.command = command
        self.started = time.monotonic()
        self.process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)

    # Gives QEMU's monitor one command after the other.
    def monitor(self, *commands):
        with socket.socket(socket.AF_UNIX) as monitor:
            monitor.connect(os.path.join(self.folder, "monitor"))
            for command in commands:
                monitor.sendall((command + "\n").encode("ascii"))
                time.sleep(0.2)

    # Presses keys on the machine's keyboard, named as QEMU's sendkey names them.
    def keys(self, *names):
        self.monitor(*("sendkey " + name for name in names))

    def unplug_network(self):
        self.monitor("set_link boot off")

    def text(self):
        try:
            with open(self.serial, "rb") as file:
                return ESCAPES.sub("", file.read().decode("utf-8", errors="replace"))
        except FileNotFoundError:
            return ""

    def count(self, text):
        return self.text().count(text)

    def seconds(self):
        return time.monotonic() - self.started

    # Seconds from the start until the serial log has the pattern, or None when that many seconds from now have
    # passed or QEMU has ended.
    def wait(self, pattern, seconds):
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            if re.search(pattern, self.text()):
                return self.seconds()
            if self.process.poll() is not None:
                return None
            time.sleep(0.1)
        return None

    def stop(self):
        if self.process.poll() is None:
            self.process.kill()
        output = self.process.communicate()[0].decode("utf-8", errors="replace").strip()
        os.makedirs(self.out, exist_ok=True)
        with open(os.path.join(self.out, self.name + ".serial.log"), "w", encoding="utf-8", newline="\n") as file:
            file.write(self.text())
        with open(os.path.join(self.out, self.name + ".qemu.txt"), "w", encoding="utf-8", newline="\n") as file:
            file.write(" ".join(self.command) + "\n" + output + "\n")

    # Every value the placeholder agent reported under the name, one for each time it ran.
    def facts(self, name):
        start = "fact %s=" % name
        return [line[len(start):] for line in self.text().splitlines() if line.startswith(start)]

    def fact(self, name):
        return (self.facts(name) or [""])[0]

    # The facts that list several things under one word, such as "fact net eth0 ...".
    def listed(self, word):
        start = "fact %s " % word
        return [line[5:] for line in self.text().splitlines() if line.startswith(start)]

    def lines(self, start):
        return [line for line in self.text().splitlines() if line.startswith(start)]

    def tail(self):
        return " | ".join(line for line in self.text().splitlines()[-6:] if line.strip())
