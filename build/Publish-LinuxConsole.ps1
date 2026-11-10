# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Publishes DDT.MachineConsole.Linux, the console for Linux on x64.

.DESCRIPTION
The console is compiled ahead of time against glibc 2.28, so it starts in the Linux boot environment and on the
distributions DDT installs. NativeAOT does not compile across systems, so the build runs build/Dockerfile.console-linux
with Docker BuildKit and writes ddt-console, libSkiaSharp.so and libHarfBuzzSharp.so to artifacts/console-linux.

Needs Docker with the Linux engine and nothing else. It works without administrator rights.

.EXAMPLE
.\build\Publish-LinuxConsole.ps1

.EXAMPLE
.\build\Publish-LinuxConsole.ps1 -Version 26.1.412
#>
[CmdletBinding()]
param(
    [string] $Output,

    # As 26.1.412. Without it, the version is the one Get-Version.ps1 counts, because the build in the container has
    # no commits to count.
    [ValidatePattern('^(\d{1,3}\.\d{1,3}\.\d{1,5})?$')]
    [string] $Version,

    # Builds every stage again, which installs the compiler and the SDK again.
    [switch] $NoCache
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Defaults are set here instead of in param(), because Windows PowerShell leaves $PSScriptRoot empty
# there when the script is started with powershell -File.
if (-not $Output) { $Output = Join-Path $PSScriptRoot '../artifacts/console-linux' }
if (-not $Version) { $Version = & (Join-Path $PSScriptRoot 'Get-Version.ps1') -AllowShallow }

$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath

$arguments = @(
    'buildx', 'build'
    '--file', (Join-Path $PSScriptRoot 'Dockerfile.console-linux')
    '--target', 'export'
    '--platform', 'linux/amd64'
    '--build-arg', "DDT_VERSION=$Version"
    '--output', "type=local,dest=$Output"
    '--progress', 'plain'
)
if ($NoCache) { $arguments += '--no-cache' }
$arguments += $root

# docker reports its progress on stderr. Windows PowerShell makes errors of that when the caller redirects it,
# and they would end the script here.
$ErrorActionPreference = 'Continue'
& docker @arguments
$ErrorActionPreference = 'Stop'
if ($LASTEXITCODE -ne 0) {
    throw "docker buildx build failed with exit code $LASTEXITCODE."
}

foreach ($name in 'ddt-console', 'libSkiaSharp.so', 'libHarfBuzzSharp.so') {
    $file = Join-Path $Output $name
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ('Published {0} ({1:N1} MB, SHA-256 {2})' -f $file, ((Get-Item -LiteralPath $file).Length / 1MB), $hash)
}
Write-Host "Version $Version"
