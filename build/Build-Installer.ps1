# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Builds DDT.msi, the Windows installer of the DDT server.

.DESCRIPTION
Builds the web UI, publishes DDT.Host self-contained for win-x64, so the server needs no .NET runtime, and builds
build/Installer/DDT.Installer.wixproj around it with the WiX Toolset.

The MSI installs the server as the DDT service, running as NT SERVICE\DDT, adds its firewall rules, and writes
ddt.ini, the bootstrap file, to %ProgramData%\DDT. It takes these properties, as in msiexec /i DDT.msi PORT=443:

  PORT              The HTTPS port, 8443 by default.
  STOREFOLDER       The store, %ProgramData%\DDT\ by default. Only SYSTEM, Administrators and the service get in.
  CONNECTIONSTRING  A PostgreSQL connection string. Without one, DDT keeps its database in SQLite in the store.
  FIREWALLPROFILES  The network profiles the firewall rules cover: 1 Domain, 2 Private, 4 Public, added up. 3 by
                    default; an upgrade keeps the last install's.
  FIREWALLPUBLIC=1  Adds Public to FIREWALLPROFILES, like the checkbox setup shows on a public network.
  IISHOSTNAME       With IISCERTIFICATE, a thumbprint from the machine store: an IIS site under this name forwards
                    to DDT. Needs IIS with URL Rewrite and ARR. Uninstall removes the site.
  NETBOOTINTERFACE  The network card that answers netboot, by name, such as Ethernet. Unset, PXE stays off until
                    someone picks a card in DDT.
  REMOVESTORE=1     On uninstall, deletes the store and ddt.ini too, with the database and DDT's root key.

PORT, STOREFOLDER and CONNECTIONSTRING only seed ddt.ini on a new install. After that, ddt.ini is the administrator's.

.PARAMETER Version
The version of the MSI and the server, as major.minor.patch. Windows Installer ignores anything after it.

.PARAMETER SkipWeb
Uses the web UI that's already in src/DDT.Host/wwwroot instead of building it again.

.EXAMPLE
.\build\Build-Installer.ps1 -Version 0.1.0
#>
[CmdletBinding()]
param(
    [ValidatePattern('^\d{1,3}\.\d{1,3}\.\d{1,5}$')]
    [string] $Version = '0.0.1',

    [string] $Output,

    [switch] $SkipWeb
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Defaults are set here instead of in param(), because Windows PowerShell leaves $PSScriptRoot empty
# there when the script is started with powershell -File.
if (-not $Output) { $Output = Join-Path $PSScriptRoot '..\artifacts\installer' }

$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$work = Join-Path $root 'artifacts\installer-work'
$payload = Join-Path $work 'payload'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [string[]] $ArgumentList = @()
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($ArgumentList -join ' ') failed with exit code $LASTEXITCODE."
    }
}

# NOTICE for the licence page, one line per paragraph so the dialog wraps it.
function ConvertTo-Rtf {
    param([Parameter(Mandatory)] [string] $Text)

    $paragraphs = ($Text -replace "`r`n", "`n").Trim() -split "`n\s*`n" | ForEach-Object {
        $line = (($_ -split "`n") | ForEach-Object { $_.Trim() }) -join ' '
        $escaped = New-Object System.Text.StringBuilder
        foreach ($character in $line.Replace('\', '\\').Replace('{', '\{').Replace('}', '\}').ToCharArray()) {
            if ([int] $character -gt 127) {
                [void] $escaped.Append("\u$([int] $character)?")
            }
            else {
                [void] $escaped.Append($character)
            }
        }
        $escaped.ToString()
    }

    "{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs18`r`n" + ($paragraphs -join "\par\par`r`n") + "\par}`r`n"
}

$major, $minor, $build = $Version.Split('.') | ForEach-Object { [int] $_ }
if ($major -gt 255 -or $minor -gt 255 -or $build -gt 65535) {
    throw "Windows Installer takes versions up to 255.255.65535, not $Version."
}

if (-not $SkipWeb) {
    $web = Join-Path $root 'src\DDT.Web'
    Invoke-Checked 'npm' @('ci', '--prefix', $web)
    Invoke-Checked 'npm' @('run', 'build', '--prefix', $web)
}

# Leftovers would end up in the MSI.
if (Test-Path -LiteralPath $payload) {
    Remove-Item -LiteralPath $payload -Recurse -Force
}

Invoke-Checked 'dotnet' @(
    'publish', (Join-Path $root 'src\DDT.Host\DDT.Host.csproj'),
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained',
    "-p:Version=$Version",
    '--output', $payload)

$licence = Join-Path $work 'License.rtf'
$notice = [IO.File]::ReadAllText((Join-Path $root 'NOTICE'))
[IO.File]::WriteAllText($licence, (ConvertTo-Rtf $notice), [Text.Encoding]::ASCII)

Invoke-Checked 'dotnet' @(
    'build', (Join-Path $root 'build\Installer\DDT.Installer.wixproj'),
    '--configuration', 'Release',
    "-p:Version=$Version",
    "-p:PayloadDir=$payload",
    "-p:LicenseRtf=$licence")

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$built = Join-Path $root 'build\Installer\bin\Release\DDT.msi'
$msi = Join-Path $Output 'DDT.msi'

# Defender can hold the last build's copy for minutes. It allows a rename over it, not a write into it.
Copy-Item -LiteralPath $built -Destination "$msi.new" -Force

for ($attempt = 1; ; $attempt++) {
    try {
        Move-Item -LiteralPath "$msi.new" -Destination $msi -Force
        break
    }
    catch {
        if ($attempt -ge 40) { throw }
        if ($attempt -eq 1) { Write-Host 'Waiting for the last DDT.msi to be free, up to two minutes.' }
        Start-Sleep -Seconds 3
    }
}

# For Install-Ddt.ps1 -Source, which checks the MSI against it like against a release's.
$hash = (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $Output 'SHA256SUMS'), "$hash  DDT.msi`n")

Write-Host ("Built {0} ({1:N1} MB)" -f $msi, ((Get-Item -LiteralPath $msi).Length / 1MB))
