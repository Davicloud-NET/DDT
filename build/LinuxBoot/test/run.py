#!/usr/bin/env python3
# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Boots the DDT Linux boot environment in QEMU, with Secure Boot on and over TFTP as a PC would, and checks
# what the start scripts and the placeholder agent report on the serial port. build/Test-LinuxBoot.ps1 runs it.
#
#   run.py [--boot <folder>] [--out <folder>] [<check> ...]
#
# --boot is the output of Build-LinuxBoot.ps1, --out gets the serial logs. Without names, every check runs.

import argparse
import os
import re
import shutil
import subprocess
import sys
import time

from machine import AGENT_JSON, SERVER, WORK, Machine, archive

LIBRARIES = "/usr/lib/x86_64-linux-gnu"
READY = "DDT-PLACEHOLDER-AGENT ready"
DONE = "DDT-PLACEHOLDER-AGENT facts done"
SCREEN = ["-device", "VGA"]
KEYBOARDS = ["-device", "qemu-xhci", "-device", "usb-kbd", "-device", "virtio-keyboard-pci"]
# A hardware clock that stopped years ago, as with a flat battery.
OLD_CLOCK = ["-rtc", "base=2020-01-01T00:00:00"]
DISPLAY_DRIVERS = {"bochs", "virtio_gpu", "hyperv_drm", "simpledrm", "vmwgfx"}

results = []


def check(name, good, evidence):
    results.append((bool(good), name))
    print("%s %s: %s" % ("PASS" if good else "FAIL", name, evidence), flush=True)


# The firmware loads shim, shim loads GRUB, GRUB loads the kernel and three initramfs files.
def chain(boot, out):
    machine = Machine("chain", boot, out)
    machine.start(SCREEN)
    seconds = machine.wait(re.escape(READY), 120)
    machine.wait(re.escape(DONE), 30)
    machine.stop()
    if seconds is None:
        check("chain: the agent starts with Secure Boot on", False, machine.tail())
        return
    check("chain: the agent starts with Secure Boot on", True, "'%s' %.1f s after power-on" % (READY, seconds))
    check("chain: /ddt/agent.json from site.cpio", machine.fact("agent.json") == AGENT_JSON, machine.fact("agent.json"))
    check("chain: the SecureBoot variable is 1", machine.fact("SecureBoot") == "01", "SecureBoot=" + machine.fact("SecureBoot"))
    lockdown = machine.fact("lockdown") + " / " + machine.fact("lockdown-log")
    check("chain: the kernel is locked down", "[integrity]" in lockdown and "locked down" in lockdown, lockdown)
    stages = machine.lines("ddt: ")
    before_agent = stages[:stages.index("ddt: agent: starting") + 1] if "ddt: agent: starting" in stages else []
    check("chain: the start scripts report their stages", len(before_agent) >= 7, " | ".join(stages))
    logged = machine.fact("kernel-log").split("|")
    missing = [stage for stage in before_agent if stage[5:] not in logged]
    check("chain: every stage is in the kernel's log too", before_agent and not missing,
          "%d lines on the console, missing in dmesg: %s" % (len(before_agent), ", ".join(missing) or "none"))
    kept = machine.lines("ddt: clock: the hardware clock is")
    check("chain: a hardware clock near the server's time is kept", bool(kept), kept[0] if kept else machine.tail())
    marker = machine.fact("marker")
    check("chain: the marker file names this boot environment", "snapshot=" in marker and "kernel=" + machine.fact("kernel") in marker,
          "/etc/ddt/boot-environment: " + marker)
    terminal = "%s, controlling terminal %s, echo is %s, /tmp %s, printk %s" % (
        machine.fact("terminal"), machine.fact("controlling-terminal"), machine.fact("echo"), machine.fact("tmp"), machine.fact("printk"))
    check("chain: the agent has the first screen as its terminal", machine.fact("terminal") == "/dev/tty1 TERM=linux"
          and machine.fact("controlling-terminal") == "yes" and machine.fact("echo") == "-echo" and machine.fact("tmp") == "writable"
          and machine.fact("printk").startswith("1 "), terminal)
    check("chain: no shell without ddt.shell", machine.fact("shell") == "closed" and "ddt:~#" not in machine.text(),
          "shell is %s, no prompt on the serial port" % machine.fact("shell"))


# The menu an agent that failed leaves takes its keys from the first screen: 1 starts the agent again, 4 powers off.
def menu(boot, out):
    machine = Machine("menu", boot, out)
    machine.start(SCREEN)
    ended = "ddt: agent: ended with code 3"
    first = machine.wait(ended, 120)
    if first is not None:
        time.sleep(1)
        machine.keys("1", "ret")
    again = machine.wait("(?s)%s.*%s" % (ended, ended), 30)
    check("menu: 1 starts the agent again", again is not None, "the agent ran %d times" % machine.count("ddt: agent: starting"))
    check("menu: a failed agent opens the shell on the second screen and the serial port",
          machine.facts("shell") == ["closed", "open"], "shell was %s" % ", then ".join(machine.facts("shell")))
    if again is not None:
        time.sleep(1)
        machine.keys("4", "ret")
    try:
        machine.process.wait(timeout=30)
        check("menu: 4 powers the machine off", True, "QEMU ended %.1f s after power-on" % machine.seconds())
    except subprocess.TimeoutExpired:
        check("menu: 4 powers the machine off", False, "the machine is still on")
    machine.stop()


# Secure Boot is not just switched on, it refuses what is not signed.
def tampered(boot, out):
    for file, refusal in [("vmlinuz", "bad shim signature"), ("grubx64.efi", "Security Violation|Verification failed")]:
        machine = Machine("tampered-" + file.split(".")[0], boot, out, tamper=file)
        machine.start(SCREEN)
        seconds = machine.wait(refusal + "|" + re.escape(READY), 90)
        # A kernel that starts anyway would show its marker soon after.
        machine.wait(re.escape(READY), 15)
        machine.stop()
        text = machine.text()
        found = re.search(refusal, text)
        good = seconds is not None and found is not None and READY not in text and "Linux version" not in text
        evidence = "'%s' after %.1f s, no kernel" % (found.group(0), seconds) if good else machine.tail()
        check("tampered: %s with one byte changed is refused" % file, good, evidence)


# Network, disk, screen and keyboards come up, with each display QEMU can stand in for.
def hardware(boot, out, hooks):
    second_card = ["-netdev", "user,id=second,net=10.0.3.0/24", "-device", "e1000e,netdev=second,romfile="]
    # virtio-gpu-pci has no frame buffer in the firmware, and base.img brings no driver for it.
    displays = [("VGA", "EFI VGA"), ("virtio-vga", "EFI VGA"), ("ramfb", "EFI VGA"), ("bochs-display", "EFI VGA"),
                ("virtio-gpu-pci", None)]
    for display, firmware in displays:
        machine = Machine("hardware-" + display, boot, out, "ddt.hook=hardware", [hooks])
        machine.start(KEYBOARDS + second_card + ["-device", display])
        machine.wait("DDT-PLACEHOLDER-AGENT hook", 150)
        machine.stop()
        screen = (machine.listed("fb0") or [machine.tail()])[0]
        check("hardware: /dev/fb0 with display %s is the firmware's screen" % display,
              "id=%s " % firmware in screen if firmware else screen == "fb0 absent", screen)
        loaded = set(machine.fact("modules").split())
        check("hardware: no display driver with display %s" % display,
              DONE in machine.text() and not machine.fact("drm-cards").strip() and not loaded & DISPLAY_DRIVERS,
              "DRM cards: %s; %d modules loaded, display drivers among them: %s" % (
                  machine.fact("drm-cards").strip() or "none", len(loaded), ", ".join(sorted(loaded & DISPLAY_DRIVERS)) or "none"))
        if display == "VGA":
            hardware_facts(machine)


def hardware_facts(machine):
    cards = [card for card in machine.listed("net") if "driver=" in card and not card.startswith("net lo")]
    lease = machine.fact("lease")
    route = machine.fact("route")
    check("hardware: DHCP lease on the card it booted from", "10.0.2.15/24" in lease, lease)
    check("hardware: default route", route.startswith("default via " + SERVER), route)
    check("hardware: virtio and e1000e cards have drivers", any("virtio_net" in card for card in cards)
          and any("e1000e" in card for card in cards), " | ".join(cards))
    for start, name, proof in [("hardware: second adapter", "hardware: the e1000e card gets a lease too", "lease of"),
                               ("hardware: name lookup", "hardware: a name is looked up", r"PING .* \(\d"),
                               ("hardware: keycode 21", "hardware: the German keymap is loaded", "gives 7a0b"),
                               ("hardware: lsattr says", "hardware: a program linked against glibc runs", "says -")]:
        found = machine.lines(start)
        check(name, bool(found) and re.search(proof, found[0]) is not None, found[0] if found else "no such line")
    disks = [disk for disk in machine.listed("disk") if disk.startswith("disk nvme0n1")]
    check("hardware: the NVMe disk has 8 GB", bool(disks) and "sectors=16777216 " in disks[0], disks[0] if disks else "no nvme0n1")
    nodes = machine.fact("input-nodes")
    names = [device for device in machine.listed("input") if device.startswith("input N:")]
    check("hardware: /dev/input/event* for three keyboards", len(re.findall(r"event\d+", nodes)) >= 3
          and sum("eyboard" in name for name in names) >= 3, nodes + " | " + " | ".join(names))
    refusals = machine.fact("module-refusals")
    check("hardware: no module refused for its signature", refusals == "none" and machine.fact("tainted") == "0",
          "dmesg: %s, tainted=%s, %d modules loaded" % (refusals, machine.fact("tainted"), len(machine.fact("modules").split())))


# UEFI variables through efivarfs, over three starts of one machine. hooks/variables does the work.
def variables(boot, out, hooks):
    machine = Machine("variables", boot, out, "ddt.hook=variables", [hooks])
    machine.start(SCREEN, network_first=False)
    seconds = machine.wait("variables: finished|variables: FAILED unknown", 300)
    machine.stop()
    lines = machine.lines("variables: ")
    for line in lines:
        print("     " + line, flush=True)
    bad = [line for line in lines if "FAILED" in line]
    good = [line for line in lines if line.startswith("variables: ok")]
    check("variables: three starts, BootNext, a variable of our own, a boot entry of our own",
          seconds is not None and not bad and len(good) >= 16, "%d steps passed, %d failed" % (len(good), len(bad))
          + ("" if seconds is not None else ", never finished: " + machine.tail()))


# A hardware clock that is years off is set from ddt.time, and without ddt.time it stays wrong and the screen says so.
def clock(boot, out):
    machine = Machine("clock", boot, out)
    machine.start(SCREEN + OLD_CLOCK)
    machine.wait(re.escape(DONE), 150)
    machine.stop()
    line = (machine.lines("ddt: clock:") or [machine.tail()])[0]
    date = int(machine.fact("date") or 0)
    check("clock: a hardware clock in 2020 is set from ddt.time", "set from ddt.time" in line and abs(date - time.time()) < 300,
          "%s; the agent's clock is %d s from this computer's" % (line, date - time.time()))

    machine = Machine("clock-no-time", boot, out, server_time=False)
    machine.start(SCREEN + OLD_CLOCK)
    machine.wait(re.escape(DONE), 150)
    machine.stop()
    line = (machine.lines("ddt: clock:") or [machine.tail()])[0]
    year = time.gmtime(int(machine.fact("date") or 0)).tm_year
    check("clock: without ddt.time the clock stays and the start says so", "no ddt.time" in line and year == 2020,
          "%s; the agent's clock is in %d" % (line, year))


# The server's name from the kernel command line is found without a name server, in lower case.
def server(boot, out, hooks):
    machine = Machine("server", boot, out, "ddt.hostname=DDT-Server.Example.Test ddt.hostip=%s ddt.hook=server" % SERVER, [hooks])
    machine.start(SCREEN)
    machine.wait("DDT-PLACEHOLDER-AGENT hook", 150)
    machine.stop()
    entry = "%s ddt-server.example.test" % SERVER
    check("server: /etc/hosts has the server's name in lower case", entry in machine.fact("hosts").split(";"),
          "/etc/hosts: " + machine.fact("hosts"))
    lookup = (machine.lines("server: lookup") or [machine.tail()])[0]
    check("server: the name is looked up there", "PING ddt-server.example.test (%s)" % SERVER in lookup, lookup)


# Ctrl+C ends the agent and leaves the menu, and the session itself stays through it.
def session(boot, out, hooks):
    machine = Machine("session", boot, out, "ddt.hook=wait", [hooks])
    machine.start(SCREEN)
    waiting = machine.wait("wait: waiting", 150)
    if waiting is None:
        machine.stop()
        check("session: Ctrl+C ends the agent and leaves the menu", False, machine.tail())
        return
    logged = (machine.lines("wait: the kernel's log has") or [""])[0]
    time.sleep(1)
    check("session: the kernel's messages stay off the agent's screen",
          " 1 error line" in logged and "ddt-test: an error line of the kernel" not in machine.text(),
          "%s, and the kernel's consoles did not show it" % logged)
    machine.keys("ctrl-c")
    ended = machine.wait("ddt: agent: ended with code 130", 15)
    time.sleep(3)
    once = machine.count("ddt: agent: starting")
    machine.keys("ctrl-c")
    time.sleep(2)
    still = machine.count("ddt: agent: starting")
    check("session: Ctrl+C ends the agent and leaves the menu", ended is not None and once == 1 and still == 1
          and machine.process.poll() is None, "the agent ended with code 130 and was not started again: %d start, %d after "
          "a second Ctrl+C at the menu" % (once, still))
    machine.keys("1", "ret")
    again = machine.wait("(?s)wait: waiting.*wait: waiting", 30)
    machine.stop()
    check("session: the menu still takes its keys after Ctrl+C", again is not None,
          "the agent ran %d times" % machine.count("ddt: agent: starting"))


# An agent that ends with the code for a restart and leaves the machine running: the session restarts it.
def restart(boot, out, hooks):
    machine = Machine("restart", boot, out, "ddt.hook=restart", [hooks])
    machine.start(SCREEN)
    ended = machine.wait("ddt: agent: ended with code 5", 150)
    back = machine.wait("(?s)%s.*%s" % (re.escape(READY), re.escape(READY)), 90) if ended is not None else None
    machine.stop()
    net = machine.lines("ddt: session:")
    check("restart: after the agent's exit code 5 the session restarts the machine", back is not None and bool(net),
          "the agent ended %.1f s after power-on and ran again at %.1f s; %s" % (ended, back, net[0]) if back is not None and net
          else machine.tail())


# Keys pressed while the machine starts are neither shown nor left for the agent to read.
def keys(boot, out):
    # The kernel prints the start scripts' lines on the serial port only at this log level.
    machine = Machine("keys", boot, out, "loglevel=7", screen=True)
    machine.start(SCREEN)
    # Without a cable the start waits half a minute for DHCP, which is the time the keys are pressed in.
    if machine.wait("Linux version", 120) is not None:
        machine.unplug_network()
    asking = machine.wait("ddt: network: asking DHCP", 60)
    if asking is not None:
        machine.keys("a", "b", "c")
    machine.wait(re.escape(DONE), 90)
    machine.stop()
    check("keys: what was typed during the start is gone when the agent starts",
          asking is not None and DONE in machine.text() and machine.fact("waiting-keys") == "" and machine.fact("echo") == "-echo",
          "a, b and c pressed %.1f s after power-on, while the start waited for DHCP; waiting for the agent: '%s', echo is %s" % (
              asking or 0, machine.fact("waiting-keys"), machine.fact("echo")))
    waited = [line for line in machine.text().splitlines() if "ddt: network: no answer from DHCP" in line]
    check("keys: a start without an answer from DHCP goes on to the agent", bool(waited) and DONE in machine.text(),
          waited[0].strip() if waited else machine.tail())


# Goes to the second screen, asks for its shell with Enter and types poweroff: the machine goes off if a shell is there.
def power_off_on_second_screen(machine):
    machine.keys("alt-f2", "ret")
    time.sleep(1.5)
    machine.keys("p", "o", "w", "e", "r", "o", "f", "f", "ret")


# A root shell opens with ddt.shell on the kernel command line, or once the agent has failed, and not before.
def shell(boot, out, hooks):
    machine = Machine("shell", boot, out, "ddt.shell")
    machine.start(SCREEN)
    seconds = machine.wait("ddt:~# ", 120)
    machine.stop()
    check("shell: ddt.shell opens a root shell on the serial port", seconds is not None,
          "the prompt 'ddt:~# ' %.1f s after power-on" % seconds if seconds is not None else machine.tail())

    machine = Machine("shell-after-failure", boot, out, "ddt.hook=wait", [hooks])
    machine.start(SCREEN)
    if machine.wait("wait: waiting", 150) is None:
        machine.stop()
        check("shell: the second screen has no shell while the agent runs", False, machine.tail())
        return
    power_off_on_second_screen(machine)
    time.sleep(5)
    check("shell: the second screen has no shell while the agent runs", machine.process.poll() is None,
          "poweroff typed there %.1f s after power-on, and the machine stayed on" % machine.seconds())
    machine.keys("alt-f1", "ctrl-c")
    ended = machine.wait("ddt: agent: ended with code 130", 15)
    time.sleep(1)
    power_off_on_second_screen(machine)
    try:
        machine.process.wait(timeout=30)
        off = True
    except subprocess.TimeoutExpired:
        off = False
    machine.stop()
    check("shell: once the agent has failed, the second screen has a shell", ended is not None and off,
          "poweroff typed there after the agent ended with code 130, and the machine went off" if off else machine.tail())


# One more initramfs file for the tests: the placeholder's hooks, and chattr with the libraries it needs.
def hooks_archive():
    tree = os.path.join(WORK, "hooks-tree")
    shutil.rmtree(tree, ignore_errors=True)
    shutil.copytree(os.path.join(os.path.dirname(os.path.abspath(__file__)), "hooks"), os.path.join(tree, "ddt", "hooks"))
    os.makedirs(os.path.join(tree, "usr", "bin"))
    os.makedirs(os.path.join(tree, LIBRARIES[1:]))
    for tool in ["chattr", "lsattr"]:
        shutil.copyfile(shutil.which(tool), os.path.join(tree, "usr", "bin", tool))
    for library in ["libe2p.so.2", "libcom_err.so.2"]:
        shutil.copyfile(os.path.join(LIBRARIES, library), os.path.join(tree, LIBRARIES[1:], library))
    path = os.path.join(WORK, "hooks.cpio")
    archive(tree, path)
    return path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--boot", default="/boot-files")
    parser.add_argument("--out", default="/out")
    parser.add_argument("checks", nargs="*")
    arguments = parser.parse_args()
    os.makedirs(WORK, exist_ok=True)
    hooks = hooks_archive()
    boot, out = arguments.boot, arguments.out
    known = {
        "chain": lambda: chain(boot, out),
        "menu": lambda: menu(boot, out),
        "tampered": lambda: tampered(boot, out),
        "hardware": lambda: hardware(boot, out, hooks),
        "variables": lambda: variables(boot, out, hooks),
        "clock": lambda: clock(boot, out),
        "server": lambda: server(boot, out, hooks),
        "session": lambda: session(boot, out, hooks),
        "restart": lambda: restart(boot, out, hooks),
        "keys": lambda: keys(boot, out),
        "shell": lambda: shell(boot, out, hooks),
    }
    for name in arguments.checks or list(known):
        if name not in known:
            sys.exit("There is no check named %s. There are: %s" % (name, ", ".join(known)))
        known[name]()
    bad = [name for good, name in results if not good]
    print("%d checks passed, %d failed" % (len(results) - len(bad), len(bad)), flush=True)
    sys.exit(1 if bad else 0)


if __name__ == "__main__":
    main()
