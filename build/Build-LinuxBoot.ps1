# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Builds the DDT Linux boot environment, which machines netboot to run Linux task sequences.

.DESCRIPTION
Runs build/LinuxBoot/Dockerfile with Docker BuildKit and writes its result to artifacts/linux-boot:

  shimx64.efi           Debian's shim, signed by Microsoft. The firmware loads it first.
  grubx64.efi           Debian's network GRUB, signed by Debian. It reads grub/grub.cfg from the server.
  vmlinuz               Debian's kernel, signed by Debian.
  base.img              The initramfs: busybox, kernel modules, firmware, glibc, keymaps and DDT's start scripts.
  ddt.img               A second initramfs with /ddt, the agent's folder.
  ddt-linux-boot.json   The files with their sizes and hashes, the packages, and who signed the loaders.

DDT signs nothing here, so Secure Boot only sees Microsoft's and Debian's signatures. Every package is named in
build/LinuxBoot/packages.lock.json by version and SHA-256, and the build takes them from snapshot.debian.org.
The same lock gives the same base.img, byte for byte, and the lock names its hash: the script warns when it built
another one. Update-LinuxBootLock.ps1 moves the lock on.

Needs Docker with the Linux engine and nothing else. It works without administrator rights.

.EXAMPLE
.\build\Build-LinuxBoot.ps1

.EXAMPLE
.\build\Build-LinuxBoot.ps1 -AgentFolder .\artifacts\agent-linux -NoCache
#>
[CmdletBinding()]
param(
    [string] $Output,

    # The folder that becomes /ddt in ddt.img. It must hold the agent as ddt-agent, as Publish-LinuxAgent.ps1 writes
    # it to artifacts/agent-linux. Without it, ddt.img gets a placeholder that prints what it finds on the machine,
    # and no machine can be deployed from the result.
    [string] $AgentFolder,

    # Builds every stage again, which downloads the packages again.
    [switch] $NoCache
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Defaults are set here instead of in param(), because Windows PowerShell leaves $PSScriptRoot empty
# there when the script is started with powershell -File.
if (-not $Output) { $Output = Join-Path $PSScriptRoot '../artifacts/linux-boot' }

$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$context = Join-Path $PSScriptRoot 'LinuxBoot'
$lock = Get-Content -LiteralPath (Join-Path $context 'packages.lock.json') -Raw | ConvertFrom-Json

$arguments = @(
    'buildx', 'build'
    '--file', (Join-Path $context 'Dockerfile')
    '--target', 'export'
    '--platform', 'linux/amd64'
    '--build-arg', "TOOLS_IMAGE=$($lock.toolsImage)"
    '--output', "type=local,dest=$Output"
    '--progress', 'plain'
)
if ($AgentFolder) {
    $AgentFolder = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($AgentFolder)
    if (-not (Test-Path -LiteralPath (Join-Path $AgentFolder 'ddt-agent'))) {
        throw "There is no ddt-agent in $AgentFolder."
    }

    $arguments += @('--build-context', "agent=$AgentFolder")
}

if ($NoCache) { $arguments += '--no-cache' }
$arguments += $context

# docker reports its progress on stderr. Windows PowerShell makes errors of that when the caller redirects it,
# and they would end the script here.
$ErrorActionPreference = 'Continue'
& docker @arguments
$ErrorActionPreference = 'Stop'
if ($LASTEXITCODE -ne 0) {
    throw "docker buildx build failed with exit code $LASTEXITCODE."
}

$manifest = Get-Content -LiteralPath (Join-Path $Output 'ddt-linux-boot.json') -Raw | ConvertFrom-Json
Write-Host "Built the Linux boot environment in $Output, kernel $($manifest.kernelRelease):"
foreach ($file in $manifest.files) {
    Write-Host ('  {0,-12} {1,7:N1} MB  {2}' -f $file.name, ($file.size / 1MB), $file.sha256)
}

if ($manifest.PSObject.Properties['agent']) {
    Write-Host "ddt.img holds the agent $($manifest.agent.version), SHA-256 $($manifest.agent.sha256)."
}
else {
    Write-Host 'ddt.img holds the placeholder, not an agent. Pass -AgentFolder for a boot environment that deploys machines.'
}

Write-Host "The shim is signed under: $($manifest.shimSignedUnder -join ', ')"

# The lock names the base.img every build of it has to give. Another one means the build is not the same on every
# computer, or that what base.img is made of changed and the lock has to follow.
$built = ($manifest.files | Where-Object { $_.name -eq 'base.img' }).sha256
if ($built -ne $lock.expected.baseImageSha256) {
    Write-Warning ("base.img is not the one the lock expects, SHA-256 $($lock.expected.baseImageSha256). If its start scripts or " +
        'lists changed, Update-LinuxBootLock.ps1 -BaseImageOnly writes the new hash into the lock.')
}
