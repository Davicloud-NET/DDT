# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Gets the Aspire CLI and its bundle before a build, for a computer that has neither.

.DESCRIPTION
DDT.AppHost builds against Aspire's CLI bundle. Where it is missing, the build fetches the CLI through dnx and unpacks
the bundle by itself, once, and fails when that does not work the first time. CI starts without the bundle every time,
so it runs this first: the same two calls, each tried again when it fails. The build then finds both in place.

The version is the Aspire.AppHost.Sdk's in global.json. A computer where Aspire is installed needs none of this.

.EXAMPLE
.\build\Get-AspireBundle.ps1
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 10)]
    [int] $Attempts = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$version = (Get-Content -LiteralPath (Join-Path $root 'global.json') -Raw | ConvertFrom-Json).'msbuild-sdks'.'Aspire.AppHost.Sdk'
if (-not $version) {
    throw 'global.json names no Aspire.AppHost.Sdk.'
}

# Where the build looks, too
$aspireHome = if ($env:ASPIRE_HOME) { $env:ASPIRE_HOME } else { Join-Path $HOME '.aspire' }

function Invoke-Aspire {
    param(
        [Parameter(Mandatory)] [string] $What,
        [Parameter(Mandatory)] [int] $Tries,
        [Parameter(Mandatory)] [string[]] $ArgumentList
    )

    for ($attempt = 1; ; $attempt++) {
        & dotnet dnx --yes "aspire.cli@$version" -- @ArgumentList
        if ($LASTEXITCODE -eq 0) {
            return
        }

        if ($attempt -ge $Tries) {
            throw "$What failed $Tries times, last with exit code $LASTEXITCODE."
        }

        Write-Host "$What failed with exit code $LASTEXITCODE. Trying again in 20 seconds."
        Start-Sleep -Seconds 20
    }
}

# The build asks for the version first, which is what makes dnx fetch the CLI.
Invoke-Aspire -What 'Fetching the Aspire CLI' -Tries $Attempts -ArgumentList '--version'
Invoke-Aspire -What 'Unpacking the Aspire bundle' -Tries $Attempts -ArgumentList 'setup', '--install-path', $aspireHome

Write-Host "Aspire $version is in $aspireHome."
