# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Boots the DDT Linux boot environment in QEMU with Secure Boot on and checks that it comes up.

.DESCRIPTION
Builds an image with QEMU and OVMF from build/LinuxBoot/test/Dockerfile and runs build/LinuxBoot/test/run.py in
it, against the output of Build-LinuxBoot.ps1. The virtual machine starts as a PC does: the firmware has
Microsoft's keys and loads shimx64.efi over TFTP, shim loads GRUB, GRUB loads the kernel and the initramfs files.
The start scripts then report on the serial port, and so does a placeholder that every check but the last puts in
the agent's place with one more initramfs file. So the boot environment under test is one that is shipped, with the
real agent in ddt.img.

The checks:

  chain       The agent starts on the first screen, /ddt/agent.json is there, SecureBoot reads 1, the kernel is
              locked down, and every stage of the start is on the console and in the kernel's log.
  menu        The menu a failed agent leaves takes keys: it starts the agent again, restarts the machine and
              opens a shell.
  unattended  Without a choice at that menu the agent starts again by itself, three times, and then the menu stays.
  tampered    A kernel and a GRUB with one byte changed are refused.
  hardware    DHCP, an NVMe disk, three keyboards, and the firmware's screen as /dev/fb0 with each display device
              QEMU has, with no display driver loaded.
  variables   BootNext, a variable and a boot entry of our own, over three starts of one machine.
  clock       A hardware clock years or hours off is set from ddt.time, and stays without it.
  hostile     Words on the kernel command line that are not what they should be do not stop the start.
  disks       The start waits for a disk that a controller shows late.
  card        The kernel shows an SD card in a reader of the PC's own as a disk whose media is not removable, which
              is why the agent leaves such a card out by its kind.
  server      The server's name from the kernel command line is in /etc/hosts in lower case.
  session     Ctrl+C ends the agent and leaves the menu, and the kernel's messages stay off the screen.
  restart     After each of the agent's two exit codes for a restart, the machine restarts.
  keys        Keys pressed during the start are gone when the agent starts, and a start that gets no answer from
              DHCP goes on to the agent.
  shell       ddt.shell opens a root shell on the serial port. The second screen has one only once the agent has
              failed.
  agent       The real agent from ddt.img starts on the first screen, reads agent.json and asks the server it names
              for a newer agent. No server answers here, so that is as far as it gets. Scroll Lock and Ctrl+S do
              not hold its screen.
  adapter     On a machine whose network card Linux has no driver for, the real agent says so and waits, and goes
              on when an adapter on USB is plugged in.

The agent and adapter checks need a boot environment built with -AgentFolder, and fail on one with the placeholder
in ddt.img.

Needs Docker with the Linux engine. The container runs privileged, because QEMU needs /dev/kvm. Without KVM
QEMU emulates the processor, which takes many times longer.

.EXAMPLE
.\build\Test-LinuxBoot.ps1

.EXAMPLE
.\build\Test-LinuxBoot.ps1 -Check chain, tampered, agent
#>
[CmdletBinding()]
param(
    # The output of Build-LinuxBoot.ps1.
    [string] $BootFiles,

    # Gets the serial log and the QEMU command line of every start.
    [string] $LogFolder,

    [ValidateSet('chain', 'menu', 'unattended', 'tampered', 'hardware', 'variables', 'clock', 'hostile', 'disks', 'card', 'server',
        'session', 'restart', 'keys', 'shell', 'agent', 'adapter')]
    [string[]] $Check = @(),

    # The image is built under this name, and the container runs under it.
    [ValidatePattern('^[a-z0-9][a-z0-9_.-]*$')]
    [string] $ImageName = 'ddt-linux-boot-test'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Defaults are set here instead of in param(), because Windows PowerShell leaves $PSScriptRoot empty
# there when the script is started with powershell -File.
if (-not $BootFiles) { $BootFiles = Join-Path $PSScriptRoot '../artifacts/linux-boot' }
if (-not $LogFolder) { $LogFolder = Join-Path $PSScriptRoot '../artifacts/linux-boot-test' }

$BootFiles = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($BootFiles)
$LogFolder = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LogFolder)
if (-not (Test-Path -LiteralPath (Join-Path $BootFiles 'base.img'))) {
    throw "There is no boot environment in $BootFiles. Run Build-LinuxBoot.ps1 first."
}

$null = New-Item -ItemType Directory -Path $LogFolder -Force
$context = Join-Path $PSScriptRoot 'LinuxBoot'
$lock = Get-Content -LiteralPath (Join-Path $context 'packages.lock.json') -Raw | ConvertFrom-Json

# docker reports its progress on stderr. Windows PowerShell makes errors of that when the caller redirects it,
# and they would end the script here.
$ErrorActionPreference = 'Continue'
& docker buildx build --file (Join-Path $context 'test/Dockerfile') --platform linux/amd64 `
    --build-arg "TOOLS_IMAGE=$($lock.toolsImage)" --tag $ImageName --quiet $context
$built = $LASTEXITCODE
if ($built -eq 0) {
    & docker run --rm --name $ImageName --privileged `
        --volume "${BootFiles}:/boot-files:ro" --volume "${LogFolder}:/out" $ImageName @Check
}

$ErrorActionPreference = 'Stop'
if ($built -ne 0) {
    throw "docker buildx build failed with exit code $built."
}

if ($LASTEXITCODE -ne 0) {
    throw "The Linux boot environment failed its checks. The serial logs are in $LogFolder."
}
