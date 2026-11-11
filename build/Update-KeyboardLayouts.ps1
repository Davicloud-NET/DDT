# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Writes the console's keyboard tables again from xkeyboard-config.

.DESCRIPTION
Runs src/DDT.MachineConsole/Keyboard/Layouts/Dockerfile, which presses every key of each layout through libxkbcommon
from the snapshot of build/LinuxBoot/packages.lock.json, and puts what it writes into the repository:

  src/DDT.MachineConsole/Keyboard/Layouts/*.keymap  What each key types and what its dead keys make, one file per layout.
  licenses/xkeyboard-config/copyright              The licences of the data the tables come from.

With -Check it changes nothing and fails when the files differ from what the build gives, as CI runs it.

Needs Docker with the Linux engine and nothing else.

.EXAMPLE
.\build\Update-KeyboardLayouts.ps1

.EXAMPLE
.\build\Update-KeyboardLayouts.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch] $Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$context = Join-Path $root 'src/DDT.MachineConsole/Keyboard/Layouts'
$licence = Join-Path $root 'licenses/xkeyboard-config/copyright'
$lock = Get-Content -LiteralPath (Join-Path $root 'build/LinuxBoot/packages.lock.json') -Raw | ConvertFrom-Json
$output = Join-Path ([System.IO.Path]::GetTempPath()) "ddt-keyboard-$([guid]::NewGuid().ToString('N'))"

$arguments = @(
    'buildx', 'build'
    '--file', (Join-Path $context 'Dockerfile')
    '--target', 'export'
    '--platform', 'linux/amd64'
    '--build-arg', "TOOLS_IMAGE=$($lock.toolsImage)"
    '--build-arg', "SNAPSHOT=$($lock.snapshot)"
    '--output', "type=local,dest=$output"
    '--progress', 'plain'
    $context
)

# docker reports its progress on stderr, which Windows PowerShell turns into errors when the caller redirects it.
$ErrorActionPreference = 'Continue'
& docker @arguments
$ErrorActionPreference = 'Stop'
if ($LASTEXITCODE -ne 0) {
    throw "docker buildx build failed with exit code $LASTEXITCODE."
}

try {
    $pairs = @(Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Name -ne 'copyright' } | ForEach-Object {
            [pscustomobject]@{ Built = $_.FullName; Kept = Join-Path $context $_.Name }
        })
    $pairs += [pscustomobject]@{ Built = Join-Path $output 'copyright'; Kept = $licence }
    $stale = @(Get-ChildItem -LiteralPath $context -File | Where-Object {
            $_.Extension -in '.keymap', '.txt' -and -not (Test-Path -LiteralPath (Join-Path $output $_.Name))
        })

    if ($Check) {
        $differ = @($pairs | Where-Object {
                -not (Test-Path -LiteralPath $_.Kept) -or
                (Get-FileHash -LiteralPath $_.Built).Hash -ne (Get-FileHash -LiteralPath $_.Kept).Hash
            })
        if ($differ.Count -gt 0 -or $stale.Count -gt 0) {
            $names = @($differ | ForEach-Object { $_.Kept }) + @($stale | ForEach-Object { $_.FullName })
            throw "The keyboard tables are not what the build writes: $($names -join ', '). Run build/Update-KeyboardLayouts.ps1."
        }

        Write-Host "The $($pairs.Count - 1) keyboard files are what the build writes."
        return
    }

    $stale | Remove-Item
    New-Item -ItemType Directory -Force (Split-Path -Parent $licence) | Out-Null
    foreach ($pair in $pairs) {
        Copy-Item -LiteralPath $pair.Built -Destination $pair.Kept -Force
    }

    Write-Host "Wrote $($pairs.Count - 1) keyboard files to $context and the licences to $licence."
}
finally {
    Remove-Item -LiteralPath $output -Recurse -Force -ErrorAction SilentlyContinue
}
