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
#
# ddt.img holds the real agent, which says nothing on the serial port and needs a server. So every check but
# "agent" hands the machine one more initramfs file, whose placeholder takes the agent's place.

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time

from machine import AGENT_JSON, SERVER, WORK, Machine, archive

LIBRARIES = "/usr/lib/x86_64-linux-gnu"
PLACEHOLDER = "/test/placeholder/ddt-agent"
READY = "DDT-PLACEHOLDER-AGENT ready"
# The first line the real agent prints, which additional term 1 in NOTICE asks for.
NOTICE = "DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026 Davicloud."
DONE = "DDT-PLACEHOLDER-AGENT facts done"
SCREEN = ["-device", "VGA"]
KEYBOARDS = ["-device", "qemu-xhci", "-device", "usb-kbd", "-device", "virtio-keyboard-pci"]
# A hardware clock that stopped years ago, as with a flat battery.
OLD_CLOCK = ["-rtc", "base=2020-01-01T00:00:00"]
DISPLAY_DRIVERS = {"bochs", "virtio_gpu", "hyperv_drm", "simpledrm", "vmwgfx"}

results = []
# The initramfs file with the placeholder and its hooks, which main() makes.
extra = []


def check(name, good, evidence):
    results.append((bool(good), name))
    print("%s %s: %s" % ("PASS" if good else "FAIL", name, evidence), flush=True)


# A machine whose agent is the placeholder. A file of a later initramfs archive replaces the one before.
def placeholder(name, boot, out, arguments="", **options):
    return Machine(name, boot, out, arguments, extra, **options)


# The same with the watcher, which copies the first screen to the serial port and runs before /init.
def watched_placeholder(name, boot, out, arguments="", more=(), **options):
    return Machine(name, boot, out, arguments, extra + [watch_archive()] + list(more), init="/ddt-test/init", **options)


# What the first screen showed, each line once, without the watcher's mark.
def screen_lines(machine):
    seen = []
    for line in machine.lines("screen: "):
        if line[8:] not in seen:
            seen.append(line[8:])
    return seen


def types(machine, text):
    machine.keys(*(list(text) + ["ret"]))


# The firmware loads shim, shim loads GRUB, GRUB loads the kernel and its initramfs files.
def chain(boot, out):
    machine = placeholder("chain", boot, out)
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


# The menu an agent that failed leaves takes its keys from the first screen: 1 starts the agent again, 3 restarts
# the machine, 2 opens a shell and 4 powers off.
def menu(boot, out):
    machine = placeholder("menu", boot, out)
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
        machine.keys("3", "ret")
    restarted = machine.wait("(?s)" + ".*".join([ended] * 3), 120) if again is not None else None
    # The machine has run for a shorter time when the agent starts after a restart than at its start before.
    running = [float(seconds) for seconds in machine.facts("uptime")]
    check("menu: 3 restarts the machine", restarted is not None and len(running) == 3 and running[2] < running[1],
          "the agent started %s s after the kernel did" % ", ".join("%.1f" % seconds for seconds in running))
    # Nothing but a shell knows the word, and the menu's own way to power off is another key.
    if restarted is not None:
        time.sleep(1)
        machine.keys("2", "ret")
        time.sleep(1.5)
        types(machine, "poweroff")
    try:
        machine.process.wait(timeout=30)
        check("menu: 2 opens a shell on the first screen", restarted is not None,
              "poweroff typed there, and QEMU ended %.1f s after power-on" % machine.seconds())
    except subprocess.TimeoutExpired:
        check("menu: 2 opens a shell on the first screen", False, "the machine is still on")
    machine.stop()


# A machine nobody sits at: the agent has ended, nobody chooses, and the agent starts again by itself, three times.
# Then the menu stays, and still takes a key. The test's wait is 5 s, a machine's is 60.
def unattended(boot, out):
    tree = os.path.join(WORK, "menu-wait-tree")
    shutil.rmtree(tree, ignore_errors=True)
    os.makedirs(os.path.join(tree, "etc", "ddt"))
    with open(os.path.join(tree, "etc", "ddt", "menu-wait"), "w", encoding="utf-8", newline="\n") as file:
        file.write("5\n")
    short = os.path.join(WORK, "menu-wait.cpio")
    archive(tree, short)
    machine = watched_placeholder("unattended", boot, out, more=[short])
    machine.start(SCREEN)
    started = "ddt: agent: starting"
    fourth = machine.wait("(?s)" + ".*".join([started] * 4), 180)
    said = [line for line in screen_lines(machine) if "If nobody chooses within" in line]
    check("unattended: the menu says what happens without a choice, and when", bool(said)
          and "within 5 seconds, the DDT agent starts again. That happens 3 more times, then this menu stays." in said[0],
          said[0] if said else machine.tail())
    stays = machine.wait("screen: The DDT agent has started again by itself as often as it does. This menu stays until somebody chooses.", 60)
    time.sleep(15)
    count = machine.count(started)
    check("unattended: without a choice the agent starts again three times, then the menu stays",
          fourth is not None and stays is not None and count == 4 and machine.process.poll() is None,
          "the agent ran %d times, the last start %.1f s after power-on, and not again in the 15 s after the menu said it stays; %s" % (
              count, fourth or 0, " | ".join(machine.lines("ddt: session:"))))
    machine.keys("4", "ret")
    try:
        machine.process.wait(timeout=30)
        off = True
    except subprocess.TimeoutExpired:
        off = False
    machine.stop()
    check("unattended: the menu that stays still takes a key", off, "4 powered the machine off" if off else machine.tail())

    machine = watched_placeholder("unattended-default", boot, out)
    machine.start(SCREEN)
    machine.wait("screen: If nobody chooses within", 150)
    machine.stop()
    said = [line for line in screen_lines(machine) if "If nobody chooses within" in line]
    check("unattended: a machine waits 60 seconds", bool(said) and "within 60 seconds" in said[0], said[0] if said else machine.tail())


# Secure Boot is not just switched on, it refuses what is not signed.
def tampered(boot, out):
    for file, refusal in [("vmlinuz", "bad shim signature"), ("grubx64.efi", "Security Violation|Verification failed")]:
        machine = placeholder("tampered-" + file.split(".")[0], boot, out, tamper=file)
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
def hardware(boot, out):
    second_card = ["-netdev", "user,id=second,net=10.0.3.0/24", "-device", "e1000e,netdev=second,romfile="]
    # virtio-gpu-pci has no frame buffer in the firmware, and base.img brings no driver for it.
    displays = [("VGA", "EFI VGA"), ("virtio-vga", "EFI VGA"), ("ramfb", "EFI VGA"), ("bochs-display", "EFI VGA"),
                ("virtio-gpu-pci", None)]
    for display, firmware in displays:
        machine = placeholder("hardware-" + display, boot, out, "ddt.hook=hardware")
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
def variables(boot, out):
    machine = placeholder("variables", boot, out, "ddt.hook=variables")
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
    machine = placeholder("clock", boot, out)
    machine.start(SCREEN + OLD_CLOCK)
    machine.wait(re.escape(DONE), 150)
    machine.stop()
    line = (machine.lines("ddt: clock:") or [machine.tail()])[0]
    date = int(machine.fact("date") or 0)
    check("clock: a hardware clock in 2020 is set from ddt.time", "set from ddt.time" in line and abs(date - time.time()) < 300,
          "%s; the agent's clock is %d s from this computer's" % (line, date - time.time()))

    machine = placeholder("clock-no-time", boot, out, server_time=False)
    machine.start(SCREEN + OLD_CLOCK)
    machine.wait(re.escape(DONE), 150)
    machine.stop()
    line = (machine.lines("ddt: clock:") or [machine.tail()])[0]
    year = time.gmtime(int(machine.fact("date") or 0)).tm_year
    check("clock: without ddt.time the clock stays and the start says so", "no ddt.time" in line and year == 2020,
          "%s; the agent's clock is in %d" % (line, year))


    # A machine that ran Windows keeps local time in its hardware clock: two hours ahead here.
    ahead = time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime(time.time() + 7200))
    machine = placeholder("clock-local-time", boot, out)
    machine.start(SCREEN + ["-rtc", "base=" + ahead])
    machine.wait(re.escape(DONE), 150)
    machine.stop()
    line = (machine.lines("ddt: clock:") or [machine.tail()])[0]
    date = int(machine.fact("date") or 0)
    check("clock: a hardware clock two hours ahead is set from ddt.time", re.search(r"was 1(19|20) minutes .* set from ddt.time", line) is not None
          and abs(date - time.time()) < 300, "%s; the agent's clock is %d s from this computer's" % (line, date - time.time()))


# What a kernel command line written by hand can hold must not stop the start: a time the shell would read as octal,
# a keymap that leads out of its folder, a server name with a command in it.
def hostile(boot, out):
    words = 'ddt.time=08 ddt.keymap=../../../etc/passwd "ddt.hostname=a;b" ddt.hostip=%s' % SERVER
    machine = placeholder("hostile", boot, out, words, server_time=False, keymap=None)
    machine.start(SCREEN)
    seconds = machine.wait(re.escape(DONE), 150)
    machine.stop()
    stages = machine.lines("ddt: ")
    check("hostile: the start reaches the agent with words on the command line that are not what they should be",
          seconds is not None and "ddt: start: done, the agent is next" in stages and "ddt.hostname=a;b" in machine.fact("cmdline"),
          "the agent started %.1f s after power-on; command line: %s" % (seconds or 0, machine.fact("cmdline")))
    clock_line = (machine.lines("ddt: clock:") or [machine.tail()])[0]
    check("hostile: ddt.time=08 is no time and changes no clock", "is no time" in clock_line and abs(int(machine.fact("date") or 0) - time.time()) < 300,
          clock_line)
    check("hostile: a keymap outside its folder is not loaded", not machine.lines("ddt: keyboard:"),
          "no keyboard line among: " + " | ".join(stages))
    check("hostile: a server name that is no name is not written", machine.fact("hosts").count(";") == 2 and not machine.lines("ddt: server:"),
          "/etc/hosts: " + machine.fact("hosts"))

    machine = placeholder("hostile-zeros", boot, out, "ddt.time=000%d" % int(time.time()), server_time=False)
    machine.start(SCREEN + OLD_CLOCK)
    machine.wait(re.escape(DONE), 150)
    machine.stop()
    line = (machine.lines("ddt: clock:") or [machine.tail()])[0]
    date = int(machine.fact("date") or 0)
    check("hostile: a time with leading zeros is read as the time it is", "set from ddt.time" in line and abs(date - time.time()) < 300,
          "%s; the agent's clock is %d s from this computer's" % (line, date - time.time()))


# A controller that shows its disk late: the disk goes away when the drivers are loaded and comes back two seconds
# later. The start waits until the list of disks has stayed the same for three seconds, so the agent finds it. A
# disk that takes longer than those three seconds after the last change is missed, as before.
def disks(boot, out):
    machine = watched_placeholder("disks", boot, out, "ddt.test=disk")
    machine.start(SCREEN)
    machine.wait(re.escape(DONE), 150)
    machine.stop()
    moved = machine.lines("disk: ")
    settled = (machine.lines("ddt: disks:") or [machine.tail()])[0]
    found = [disk for disk in machine.listed("disk") if disk.startswith("disk nvme0n1")]
    check("disks: the start waits for a disk that comes late", len(moved) == 2 and bool(found) and "1 listed, and the list stayed so for" in settled,
          "%s; %s; the agent found: %s" % (" and ".join(moved), settled, found[0] if found else "no nvme0n1"))

    machine = placeholder("disks-there", boot, out)
    machine.start(SCREEN)
    machine.wait(re.escape(DONE), 150)
    machine.stop()
    stages = machine.lines("ddt: ")
    settled = (machine.lines("ddt: disks:") or [machine.tail()])[0]
    check("disks: a machine whose disks are there waits no longer than three seconds for them",
          re.search(r"1 listed, and the list stayed so for [3-6] s", settled) is not None, "%s; %s" % (settled, " | ".join(stages[-4:])))


# An SD card in a reader of the PC's own. Windows calls its media removable. The kernel does not, which is why the
# agent tells such a card by its kind and leaves it out. Should a kernel ever say otherwise, this says so first.
def card(boot, out):
    machine = placeholder("card", boot, out, "ddt.hook=card")
    image = os.path.join(machine.folder, "card.img")
    with open(image, "wb") as file:
        file.truncate(32 << 30)
    machine.start(SCREEN + ["-device", "sdhci-pci", "-drive", "if=none,id=card,format=raw,file=" + image, "-device", "sd-card,drive=card"])
    machine.wait("card: listed", 150)
    machine.stop()
    cards = machine.lines("card: mmcblk")
    check("card: the kernel shows an SD card in a built-in reader as a disk whose media is not removable",
          len(cards) == 1 and " removable=0 " in cards[0] and " type=SD " in cards[0] and "size=%d " % ((32 << 30) // 512) in cards[0],
          cards[0] if cards else machine.tail())


# The server's name from the kernel command line is found without a name server, in lower case.
def server(boot, out):
    machine = placeholder("server", boot, out, "ddt.hostname=DDT-Server.Example.Test ddt.hostip=%s ddt.hook=server" % SERVER)
    machine.start(SCREEN)
    machine.wait("DDT-PLACEHOLDER-AGENT hook", 150)
    machine.stop()
    entry = "%s ddt-server.example.test" % SERVER
    check("server: /etc/hosts has the server's name in lower case", entry in machine.fact("hosts").split(";"),
          "/etc/hosts: " + machine.fact("hosts"))
    lookup = (machine.lines("server: lookup") or [machine.tail()])[0]
    check("server: the name is looked up there", "PING ddt-server.example.test (%s)" % SERVER in lookup, lookup)


# Ctrl+C ends the agent and leaves the menu, and the session itself stays through it.
def session(boot, out):
    machine = placeholder("session", boot, out, "ddt.hook=wait")
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


# An agent that ends with a code for a restart and leaves the machine running: the session restarts it. Code 5 is
# a restart in the middle of a run, code 4 the one after a run that is done.
def restart(boot, out):
    for code, hook in [(5, "restart"), (4, "deployed")]:
        machine = placeholder("restart-%d" % code, boot, out, "ddt.hook=" + hook)
        machine.start(SCREEN)
        ended = machine.wait("ddt: agent: ended with code %d" % code, 150)
        back = machine.wait("(?s)%s.*%s" % (re.escape(READY), re.escape(READY)), 90) if ended is not None else None
        machine.stop()
        net = machine.lines("ddt: session:")
        check("restart: after the agent's exit code %d the session restarts the machine" % code, back is not None and bool(net),
              "the agent ended %.1f s after power-on and ran again at %.1f s; %s" % (ended, back, net[0]) if back is not None and net
              else machine.tail())


# Keys pressed while the machine starts are neither shown nor left for the agent to read.
def keys(boot, out):
    # The kernel prints the start scripts' lines on the serial port only at this log level.
    machine = placeholder("keys", boot, out, "loglevel=7", screen=True)
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
def shell(boot, out):
    machine = placeholder("shell", boot, out, "ddt.shell")
    machine.start(SCREEN)
    seconds = machine.wait("ddt:~# ", 120)
    machine.stop()
    check("shell: ddt.shell opens a root shell on the serial port", seconds is not None,
          "the prompt 'ddt:~# ' %.1f s after power-on" % seconds if seconds is not None else machine.tail())

    machine = placeholder("shell-after-failure", boot, out, "ddt.hook=wait")
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


# The real agent from ddt.img, started by the session as on any machine. No server answers here, so the agent
# gets as far as its notice and its first question to the server, which shows that it runs and read agent.json.
def agent(boot, out):
    with open(os.path.join(boot, "ddt-linux-boot.json"), encoding="utf-8") as source:
        manifest = json.load(source)
    bundled = manifest.get("agent")
    if not bundled:
        check("agent: ddt.img holds the agent", False,
              "it holds the placeholder. Build the boot environment with Build-LinuxBoot.ps1 -AgentFolder")
        return
    check("agent: ddt.img holds the agent", True, "version %s, SHA-256 %s" % (bundled["version"], bundled["sha256"]))
    machine = Machine("agent", boot, out, initrds=[watch_archive()], init="/ddt-test/init")
    machine.start(SCREEN)
    started = machine.wait("ddt: agent: starting", 120)
    noticed = machine.wait("screen: " + re.escape(NOTICE), 60) if started is not None else None
    asked = machine.wait("screen: .*Cannot reach the server to ask for the current agent", 90) if noticed is not None else None
    # An agent that gave up would end within moments of its first question.
    ended = machine.wait("ddt: agent: ended", 5)
    held = hold_the_screen(machine) if asked is not None else None
    machine.stop()
    screen = [line[8:] for line in machine.lines("screen: ")]
    check("agent: the agent from ddt.img starts on the first screen", noticed is not None,
          "'%s' %.1f s after power-on" % (NOTICE, noticed) if noticed is not None else machine.tail())
    question = [line for line in screen if "Cannot reach the server" in line]
    check("agent: it read agent.json and asks the server it names", asked is not None and SERVER + ":8443" in " ".join(screen),
          question[0] if question else machine.tail())
    check("agent: it stays and asks again", asked is not None and ended is None and machine.count("ddt: agent: starting") == 1,
          "started %d time, not ended %.1f s after power-on" % (machine.count("ddt: agent: starting"), machine.seconds()))
    check("agent: Scroll Lock and Ctrl+S do not hold its screen", held is not None and held[1] > held[0],
          "%d lines of the agent before the keys, %d within a minute after them" % held if held is not None else "the agent never asked")
    if manifest.get("console"):
        connected = [line for line in screen if "The graphical console" in line and "is connected" in line]
        check("agent: the console from ddt.img connects", connected, connected[0] if connected else machine.tail())


# Both keys stop what a terminal shows until another key lets it go on, and a program that writes to it waits.
# The agent asks the server again and again, so its screen gets new lines as long as nothing holds it.
def hold_the_screen(machine):
    def asked():
        return len([line for line in screen_lines(machine) if "Cannot reach the server" in line])

    before = asked()
    machine.keys("scroll_lock", "ctrl-s")
    end = time.monotonic() + 60
    while time.monotonic() < end and asked() < before + 2:
        time.sleep(1)
    return before, asked()


# A machine whose network card Linux has no driver for, although the firmware started it from that card: the agent
# says so in plain words and waits. An adapter on USB that is plugged in then gets an address, and the agent goes on.
def adapter(boot, out):
    late = ["-device", "qemu-xhci,id=usb", "-netdev", "user,id=late,net=10.0.5.0/24"]
    # ignore_loglevel shows on the serial port what the start scripts say after the start, which only the kernel's
    # log has then.
    # ddttext=1 keeps the graphical console from drawing over the screen the watcher reads.
    machine = Machine("adapter", boot, out, "module_blacklist=virtio_net ignore_loglevel ddttext=1", initrds=[watch_archive()],
                      init="/ddt-test/init")
    machine.start(SCREEN + late)
    said = machine.wait("screen: .*Linux shows no network adapter on this machine", 150)
    screen = screen_lines(machine)
    trace = [line for line in screen if "Unhandled exception" in line or "   at " in line]
    start = machine.lines("ddt: network:")
    check("adapter: without a network adapter the agent says what is wrong and what to do", said is not None and not trace
          and any("Connect a USB network adapter or a dock" in line for line in screen),
          " ".join(line for line in screen if "WARN" in line or "adapter" in line)[:400] if said is not None else machine.tail())
    # A kernel message can cut the start's line in two on the serial port.
    check("adapter: the start says that Linux shows no adapter and goes on looking",
          any("ddt: network: Linux shows no network adapter, still looking" in line for line in start),
          " | ".join(start) or machine.tail())
    if said is not None:
        machine.monitor("device_add usb-net,netdev=late,id=latecard,bus=usb.0")
    found = machine.wait("screen: .*Linux shows a network adapter now", 60) if said is not None else None
    lease = machine.wait("ddt: network: lease 10[.]0[.]5[.]", 60) if found is not None else None
    asked = machine.wait("screen: .*Cannot reach the server to ask for the current agent", 120) if found is not None else None
    ended = machine.count("ddt: agent: ended")
    machine.stop()
    screen = screen_lines(machine)
    now = [line for line in screen if "Linux shows a network adapter now" in line]
    question = [line for line in screen if "Cannot reach the server" in line]
    check("adapter: when an adapter is plugged in the agent goes on and asks the server", found is not None and asked is not None and ended == 0,
          "%s | %s" % (now[0], question[0]) if now and question else machine.tail())
    leased = [line.strip() for line in machine.text().splitlines() if "ddt: network: lease" in line]
    check("adapter: the adapter that came late gets its address", lease is not None, leased[0] if leased else machine.tail())


# One more initramfs file for the agent check: the watcher that copies the first screen to the serial port.
def watch_archive():
    tree = os.path.join(WORK, "watch-tree")
    shutil.rmtree(tree, ignore_errors=True)
    shutil.copytree(os.path.join(os.path.dirname(os.path.abspath(__file__)), "watch"), os.path.join(tree, "ddt-test"))
    path = os.path.join(WORK, "watch.cpio")
    archive(tree, path)
    return path


# One more initramfs file for the other checks: the placeholder in the agent's place, its hooks, and chattr with
# the libraries it needs.
def placeholder_archive():
    tree = os.path.join(WORK, "placeholder-tree")
    shutil.rmtree(tree, ignore_errors=True)
    shutil.copytree(os.path.join(os.path.dirname(os.path.abspath(__file__)), "hooks"), os.path.join(tree, "ddt", "hooks"))
    shutil.copyfile(PLACEHOLDER, os.path.join(tree, "ddt", "ddt-agent"))
    os.makedirs(os.path.join(tree, "usr", "bin"))
    os.makedirs(os.path.join(tree, LIBRARIES[1:]))
    for tool in ["chattr", "lsattr"]:
        shutil.copyfile(shutil.which(tool), os.path.join(tree, "usr", "bin", tool))
    for library in ["libe2p.so.2", "libcom_err.so.2"]:
        shutil.copyfile(os.path.join(LIBRARIES, library), os.path.join(tree, LIBRARIES[1:], library))
    path = os.path.join(WORK, "placeholder.cpio")
    archive(tree, path)
    return path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--boot", default="/boot-files")
    parser.add_argument("--out", default="/out")
    parser.add_argument("checks", nargs="*")
    arguments = parser.parse_args()
    os.makedirs(WORK, exist_ok=True)
    extra.append(placeholder_archive())
    boot, out = arguments.boot, arguments.out
    known = {
        "chain": lambda: chain(boot, out),
        "menu": lambda: menu(boot, out),
        "unattended": lambda: unattended(boot, out),
        "tampered": lambda: tampered(boot, out),
        "hardware": lambda: hardware(boot, out),
        "variables": lambda: variables(boot, out),
        "clock": lambda: clock(boot, out),
        "hostile": lambda: hostile(boot, out),
        "disks": lambda: disks(boot, out),
        "card": lambda: card(boot, out),
        "server": lambda: server(boot, out),
        "session": lambda: session(boot, out),
        "restart": lambda: restart(boot, out),
        "keys": lambda: keys(boot, out),
        "shell": lambda: shell(boot, out),
        "agent": lambda: agent(boot, out),
        "adapter": lambda: adapter(boot, out),
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
