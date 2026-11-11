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

It also zips the three files into -Package, at the root of the zip. Upload the zip on the server's settings page, or
name it with DDT:Agent:LinuxConsolePath. Then it's the console the agents for Linux switch to.

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

    [string] $Package,

    # Builds every stage again, which installs the compiler and the SDK again.
    [switch] $NoCache
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Defaults are set here instead of in param(), because Windows PowerShell leaves $PSScriptRoot empty
# there when the script is started with powershell -File.
if (-not $Output) { $Output = Join-Path $PSScriptRoot '../artifacts/console-linux' }
if (-not $Version) { $Version = & (Join-Path $PSScriptRoot 'Get-Version.ps1') -AllowShallow }
if (-not $Package) { $Package = Join-Path $PSScriptRoot '../artifacts/ddt-console-linux-x64.zip' }

$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$Package = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Package)
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

$names = 'ddt-console', 'libSkiaSharp.so', 'libHarfBuzzSharp.so'
foreach ($name in $names) {
    $file = Join-Path $Output $name
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ('Published {0} ({1:N1} MB, SHA-256 {2})' -f $file, ((Get-Item -LiteralPath $file).Length / 1MB), $hash)
}
Write-Host "Version $Version"

# The three files alone, whatever else the folder holds. ZipArchive instead of Compress-Archive, because in Windows
# PowerShell 5.1 Compress-Archive writes backslashes into the entry names.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $Package) {
    Remove-Item -LiteralPath $Package -Force
}
$zip = [System.IO.Compression.ZipFile]::Open($Package, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $names) {
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $Output $name), $name, [System.IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $zip.Dispose()
}
Write-Host ('Zipped it to {0} ({1:N1} MB)' -f $Package, ((Get-Item -LiteralPath $Package).Length / 1MB))
