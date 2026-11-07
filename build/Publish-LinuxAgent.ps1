# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Publishes DDT.Agent.Linux as one static executable for Linux on x64.

.DESCRIPTION
The Linux boot environment has no .NET runtime and no library to lend, so the agent is compiled ahead of time into
one file that needs nothing of the system it runs on. NativeAOT does not compile across systems, so the build runs
build/Dockerfile.agent-linux with Docker BuildKit and writes ddt-agent to artifacts/agent-linux. Pass that folder to
Build-LinuxBoot.ps1 with -AgentFolder.

Needs Docker with the Linux engine and nothing else. It works without administrator rights.

.EXAMPLE
.\build\Publish-LinuxAgent.ps1

.EXAMPLE
.\build\Publish-LinuxAgent.ps1 -Version 26.1.412
#>
[CmdletBinding()]
param(
    [string] $Output,

    # As 26.1.412. Without it, the version is the one Get-Version.ps1 counts, because the build in the container has
    # no commits to count. A release gives the agent the server's version, so the server can tell which is newer.
    [ValidatePattern('^(\d{1,3}\.\d{1,3}\.\d{1,5})?$')]
    [string] $Version,

    # Builds every stage again, which installs the compiler's packages again.
    [switch] $NoCache
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Defaults are set here instead of in param(), because Windows PowerShell leaves $PSScriptRoot empty
# there when the script is started with powershell -File.
if (-not $Output) { $Output = Join-Path $PSScriptRoot '../artifacts/agent-linux' }
if (-not $Version) { $Version = & (Join-Path $PSScriptRoot 'Get-Version.ps1') -AllowShallow }

$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath

$arguments = @(
    'buildx', 'build'
    '--file', (Join-Path $PSScriptRoot 'Dockerfile.agent-linux')
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

$agent = Join-Path $Output 'ddt-agent'
$hash = (Get-FileHash -LiteralPath $agent -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ('Published {0}, version {1} ({2:N1} MB, SHA-256 {3})' -f $agent, $Version, ((Get-Item -LiteralPath $agent).Length / 1MB), $hash)
