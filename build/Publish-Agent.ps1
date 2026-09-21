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
    [string] $Output
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Defaults are resolved here rather than in param(): Windows PowerShell leaves $PSScriptRoot empty
# there when the script is started with powershell -File.
if (-not $Output) { $Output = Join-Path $PSScriptRoot '..\artifacts\agent' }

$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$project = Join-Path $PSScriptRoot '..\src\DDT.Agent\DDT.Agent.csproj'

# Visual Studio's VsDevCmd.bat runs vswhere.exe by bare name from the installer folder, which fails in a
# shell that sets NoDefaultCurrentDirectoryInExePath. The error text then lands in the linker path the
# NativeAOT targets read back, and the link step fails with exit code 123.
$installer = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer'
if (Test-Path -LiteralPath (Join-Path $installer 'vswhere.exe')) {
    $env:PATH = "$installer;$env:PATH"
}

& dotnet publish $project --configuration Release --output $Output
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$agent = Join-Path $Output 'ddt-agent.exe'
Write-Host ("Published {0} ({1:N1} MB)" -f $agent, ((Get-Item -LiteralPath $agent).Length / 1MB))
