# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Publishes DDT.Agent as a NativeAOT win-x64 executable for Windows PE.

.DESCRIPTION
Windows PE has no .NET runtime, so the agent is compiled ahead of time into one native executable.
This needs the Visual C++ build tools. Pass the result to Build-BootImage.ps1 with -AgentPath.

.EXAMPLE
.\build\Publish-Agent.ps1
#>
[CmdletBinding()]
param(
    [string] $Output,

    # As 0.4.0. A release gives the agent and console the server's version, so the server can tell which is newer.
    [ValidatePattern('^(\d{1,3}\.\d{1,3}\.\d{1,5})?$')]
    [string] $Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Defaults are set here instead of in param(), because Windows PowerShell leaves $PSScriptRoot empty
# there when the script is started with powershell -File.
if (-not $Output) { $Output = Join-Path $PSScriptRoot '..\artifacts\agent' }

$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$project = Join-Path $PSScriptRoot '..\src\DDT.Agent\DDT.Agent.csproj'

# VsDevCmd.bat runs vswhere.exe by its bare name from the installer folder. A shell that sets
# NoDefaultCurrentDirectoryInExePath refuses that, and the NativeAOT link step then fails with exit code 123.
$installer = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer'
if (Test-Path -LiteralPath (Join-Path $installer 'vswhere.exe')) {
    $env:PATH = "$installer;$env:PATH"
}

$arguments = @('publish', $project, '--configuration', 'Release', '--output', $Output)
if ($Version) { $arguments += "-p:Version=$Version" }

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$agent = Join-Path $Output 'ddt-agent.exe'
Write-Host ("Published {0} ({1:N1} MB)" -f $agent, ((Get-Item -LiteralPath $agent).Length / 1MB))
