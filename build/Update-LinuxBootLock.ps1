# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Moves the lock of the DDT Linux boot environment to another snapshot of Debian's archive.

.DESCRIPTION
Writes build/LinuxBoot/packages.lock.json again for the snapshot: the same packages at the versions and hashes
snapshot.debian.org holds there, the kernel release that Debian's kernel meta package names, and the source
packages all of them were built from. Then it writes licenses/linux-boot again from the new packages: the
copyright file of every source package, the licence texts those point to, and SOURCES.md.

It believes Debian's package indexes only as far as Debian signed them: the InRelease file of each suite must carry
a signature of one of the archive keys in the tools image, and each index must be the one InRelease names. The lock
also gets the packages of the tools image as they are at the snapshot, which every build compares its tools with.

At the end it builds base.img from the lock and writes its SHA-256 into the lock, as the hash every build of that
lock has to give. Continuous integration and a release compare their base.img with it, so a base.img that came
out differently on another computer does not ship unnoticed.

After it, build with Build-LinuxBoot.ps1 and test with Test-LinuxBoot.ps1 and on a machine. Where a package got
another name, as the kernel's do with each release, THIRD-PARTY-NOTICES.md has to follow; LinuxBootNoticesTests
says where.

The tools image, which the lock names by its digest, stays. Change it by hand, then run this script for the
snapshot the lock is at, so the lock names the new image's packages.

Needs Docker with the Linux engine.

.PARAMETER Snapshot
The snapshot's time as snapshot.debian.org writes it, such as 20261006T203214Z. For a time between two snapshots
the archive answers with the earlier one. Without it, the newest snapshot is taken.

.PARAMETER BaseImageOnly
Leaves the packages where they are and writes only base.img's hash into the lock again. For a change to what
base.img is made of besides the packages: the start scripts in build/LinuxBoot/rootfs, the lists of modules and
keymaps, or the scripts that assemble it.

.EXAMPLE
.\build\Update-LinuxBootLock.ps1

.EXAMPLE
.\build\Update-LinuxBootLock.ps1 -Snapshot 20261006T203214Z

.EXAMPLE
.\build\Update-LinuxBootLock.ps1 -BaseImageOnly
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d{8}T\d{6}Z$')]
    [string] $Snapshot,

    [switch] $BaseImageOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Runs one stage of build/LinuxBoot/Dockerfile and puts its files into the folder, which is emptied first.
function Invoke-DockerStage {
    param(
        [Parameter(Mandatory)][string] $Context,
        [Parameter(Mandatory)][string] $ToolsImage,
        [Parameter(Mandatory)][string] $Target,
        [Parameter(Mandatory)][string] $Destination,
        [string[]] $BuildArgument = @()
    )

    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
    $arguments = @(
        'buildx', 'build'
        '--file', (Join-Path $Context 'Dockerfile')
        '--target', $Target
        '--platform', 'linux/amd64'
        '--build-arg', "TOOLS_IMAGE=$ToolsImage"
        '--output', "type=local,dest=$Destination"
        '--progress', 'plain'
    )
    foreach ($argument in $BuildArgument) { $arguments += @('--build-arg', $argument) }
    $arguments += $Context

    # docker reports its progress on stderr. Windows PowerShell makes errors of that when the caller redirects it,
    # and they would end the script here.
    $ErrorActionPreference = 'Continue'
    & docker @arguments
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne 0) {
        throw "docker buildx build of the stage $Target failed with exit code $LASTEXITCODE."
    }
}

# Puts the hash into the lock where it stands, so the rest of the file stays byte for byte as lock.py wrote it.
function Set-ExpectedBaseImage {
    param(
        [Parameter(Mandatory)][string] $LockPath,
        [Parameter(Mandatory)][string] $Sha256
    )

    $text = [IO.File]::ReadAllText($LockPath)
    $pattern = '("baseImageSha256":\s*")[0-9a-f]*(")'
    if (-not [regex]::IsMatch($text, $pattern)) {
        throw "$LockPath has no expected.baseImageSha256 to write base.img's hash into."
    }

    [IO.File]::WriteAllText($LockPath, [regex]::Replace($text, $pattern, "`${1}$Sha256`${2}"))
}

if ($BaseImageOnly -and $Snapshot) {
    throw '-BaseImageOnly keeps the snapshot the lock is at. Pass one of the two.'
}

$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$context = Join-Path $PSScriptRoot 'LinuxBoot'
$lockPath = Join-Path $context 'packages.lock.json'
$licenses = Join-Path $repository 'licenses\linux-boot'
$work = Join-Path $repository 'artifacts\linux-boot-lock'
if (-not $Snapshot) { $Snapshot = [DateTime]::UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'") }

$before = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json

if (-not $BaseImageOnly) {
    Invoke-DockerStage -Context $context -ToolsImage $before.toolsImage -Target 'lock' -Destination (Join-Path $work 'lock') `
        -BuildArgument "SNAPSHOT=$Snapshot"
    Copy-Item -LiteralPath (Join-Path $work 'lock\packages.lock.json') -Destination $lockPath -Force

    # The tools come from the snapshot too, so the lock names them as they are there.
    Invoke-DockerStage -Context $context -ToolsImage $before.toolsImage -Target 'tools-lock' -Destination (Join-Path $work 'tools')
    Copy-Item -LiteralPath (Join-Path $work 'tools\packages.lock.json') -Destination $lockPath -Force

    # From the lock as it is now, so the packages this downloads are the ones the next build takes from the cache.
    Invoke-DockerStage -Context $context -ToolsImage $before.toolsImage -Target 'notices' -Destination (Join-Path $work 'notices')
    if (Test-Path -LiteralPath $licenses) { Remove-Item -LiteralPath $licenses -Recurse -Force }
    Copy-Item -LiteralPath (Join-Path $work 'notices') -Destination $licenses -Recurse
}

# With the placeholder in ddt.img, which base.img does not depend on.
Invoke-DockerStage -Context $context -ToolsImage $before.toolsImage -Target 'export' -Destination (Join-Path $work 'build')
$hash = (Get-FileHash -LiteralPath (Join-Path $work 'build\base.img') -Algorithm SHA256).Hash.ToLowerInvariant()
Set-ExpectedBaseImage -LockPath $lockPath -Sha256 $hash
Remove-Item -LiteralPath $work -Recurse -Force

$after = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
if ($BaseImageOnly) {
    Write-Host "The lock stays at snapshot $($after.snapshot), kernel $($after.kernelRelease)."
}
else {
    Write-Host "The lock is at snapshot $($after.snapshot), kernel $($after.kernelRelease). $licenses is written again."
}

$versions = @{}
foreach ($package in $before.packages) { $versions[$package.name] = $package.version }
$changes = 0
foreach ($package in $after.packages) {
    if (-not $versions.ContainsKey($package.name)) {
        Write-Host "  $($package.name) $($package.version), a new name"
        $changes++
    }
    elseif ($versions[$package.name] -ne $package.version) {
        Write-Host "  $($package.name): $($versions[$package.name]) -> $($package.version)"
        $changes++
    }
}

if ($changes -eq 0) { Write-Host '  No package changed.' }
if ($before.expected.baseImageSha256 -eq $hash) {
    Write-Host "  base.img is as the lock expected it, SHA-256 $hash."
}
else {
    Write-Host "  base.img has SHA-256 $hash now, which the lock expects from here on. It was $($before.expected.baseImageSha256)."
}
