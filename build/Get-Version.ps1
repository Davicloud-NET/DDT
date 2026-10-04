# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Prints DDT's version for the commit that is checked out, as 26.1.412.

.DESCRIPTION
The version is Year.Major.Build. Year and Major are DdtVersionYear and DdtVersionMajor in Directory.Build.props, which
someone raises by hand. Build is the number of commits up to the one checked out, so it rises by itself and is the same
on every computer. A plain dotnet build counts the same way, in Directory.Build.targets.

Windows Installer compares the three numbers and takes at most 255.255.65535, which is why Year has two digits.

A release is cut from master only. A branch that is squashed into master has more commits than master gets from it, so
a release from the branch would carry a higher version than the next one from master.

.PARAMETER AllowShallow
Counts the commits a shallow clone has, where the script otherwise stops. For a build that is no release, such as
continuous integration's.

.EXAMPLE
.\build\Get-Version.ps1
#>
[CmdletBinding()]
[OutputType([string])]
param(
    [switch] $AllowShallow
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
$properties = ([xml] [IO.File]::ReadAllText((Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup

$year = [int] ($properties | ForEach-Object { $_.SelectSingleNode('DdtVersionYear') } | Where-Object { $_ }).InnerText
$major = [int] ($properties | ForEach-Object { $_.SelectSingleNode('DdtVersionMajor') } | Where-Object { $_ }).InnerText

$shallow = & git -C $root rev-parse --is-shallow-repository
if ($LASTEXITCODE -ne 0) { throw "git has no repository in $root, so there are no commits to count." }

if ($shallow -eq 'true' -and -not $AllowShallow) {
    throw 'This is a shallow clone, which has only some of the commits. Fetch the rest with git fetch --unshallow.'
}

$build = [int] (& git -C $root rev-list --count HEAD)
if ($LASTEXITCODE -ne 0) { throw 'git could not count the commits.' }

if ($year -lt 1 -or $year -gt 255 -or $major -lt 1 -or $major -gt 255 -or $build -gt 65535) {
    throw "Windows Installer takes versions up to 255.255.65535, not $year.$major.$build."
}

"$year.$major.$build"
