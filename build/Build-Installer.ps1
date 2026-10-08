# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Builds DDT.msi, the Windows installer of the DDT server.

.DESCRIPTION
Builds the web UI, publishes DDT.Host self-contained for win-x64, so the server needs no .NET runtime, and builds
build/Installer/DDT.Installer.wixproj around it with the WiX Toolset. Next to the server go the agent and the console
of the same version, which it offers until others are uploaded, and Build-BootImage.ps1 with its module.

The Linux boot environment and the agent for Linux go in too, as linux-boot\ and ddt-agent-linux-x64, when they are
built: Docker builds them, with Publish-LinuxAgent.ps1 and then Build-LinuxBoot.ps1 -AgentFolder, and this script
does not. Without them the script says so, and the server starts no machine in the Linux boot environment.

The MSI installs the server as the DDT service, running as NT SERVICE\DDT, adds its firewall rules, and writes
ddt.ini, the bootstrap file, to %ProgramData%\DDT. It takes these properties, as in msiexec /i DDT.msi PORT=443:

  PORT              The HTTPS port, 8443 by default.
  STOREFOLDER       The store, %ProgramData%\DDT\ by default. Only SYSTEM, Administrators and the service get in.
  CONNECTIONSTRING  A connection string for PostgreSQL or SQL Server. Without one, DDT keeps its database in SQLite
                    in the store.
  DATABASEPROVIDER  SqlServer when CONNECTIONSTRING names a SQL Server. PostgreSql is the default.
  FIREWALLPROFILES  The network profiles the firewall rules cover: 1 Domain, 2 Private, 4 Public, added up. 3 by
                    default; an upgrade keeps the last install's.
  FIREWALLPUBLIC=1  Adds Public to FIREWALLPROFILES, like the checkbox setup shows on a public network.
  IISHOSTNAME       With IISCERTIFICATE, a thumbprint from the machine store: an IIS site under this name forwards
                    to DDT. Needs IIS with URL Rewrite and ARR. Uninstall removes the site.
  INSTALLADK=1      Gets Microsoft's ADK and its Windows PE add-on once setup is done, in the background, by
                    DDT.Host setup adk. Accepts Microsoft's licence terms for them. The setup pages offer it.
  NETBOOTINTERFACE  The network card that answers netboot, by name, such as Ethernet. Unset, PXE stays off until
                    someone picks a card in DDT.
  REMOVESTORE=1     On uninstall, deletes the store and ddt.ini too, with the database and DDT's root key.

PORT, STOREFOLDER, CONNECTIONSTRING and DATABASEPROVIDER only seed ddt.ini on a new install. After that, ddt.ini is the administrator's.

.PARAMETER Version
The version of the MSI, the server, the agent and the console, as Year.Major.Build. Without it, the one Get-Version.ps1
counts for the commit that is checked out.

.PARAMETER PreRelease
Marks the server as a pre-release: the About page shows the version with -pre after it. The MSI's version stays the
three numbers, since Windows Installer takes nothing else.

.PARAMETER SkipWeb
Uses the web UI that's already in src/DDT.Host/wwwroot instead of building it again.

.PARAMETER SkipAgent
Leaves the agent and the console out, which need the Visual C++ build tools and minutes to compile. Such a server
offers no agent until one is uploaded. For checking that the installer builds, not for a release.

.PARAMETER StageOnly
Stops before the MSI is built, with all it will hold in artifacts\installer-work. A release signs the programs there
and goes on with -Staged.

.PARAMETER Staged
Builds the MSI from what -StageOnly left. It zips the console again, since its program may be signed by now.

.PARAMETER LinuxBoot
The folder Build-LinuxBoot.ps1 wrote the Linux boot environment to, artifacts\linux-boot by default. Either half of
a build in two halves takes it, since a release builds it on Linux while the programs are signed.

.PARAMETER LinuxAgent
The agent for Linux as Publish-LinuxAgent.ps1 wrote it, artifacts\agent-linux\ddt-agent by default. It has to be the
file in the boot environment's ddt.img and of this build's version: a build in two halves, as a release is, stops
when it is not, and any other build warns.

.EXAMPLE
.\build\Build-Installer.ps1
#>
[CmdletBinding()]
param(
    [ValidatePattern('^(\d{1,3}\.\d{1,3}\.\d{1,5})?$')]
    [string] $Version,

    [switch] $PreRelease,

    [string] $Output,

    [switch] $SkipWeb,

    [switch] $SkipAgent,

    [switch] $StageOnly,

    [switch] $Staged,

    [string] $LinuxBoot,

    [string] $LinuxAgent
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Defaults are set here instead of in param(), because Windows PowerShell leaves $PSScriptRoot empty
# there when the script is started with powershell -File.
if (-not $Output) { $Output = Join-Path $PSScriptRoot '..\artifacts\installer' }
if (-not $LinuxBoot) { $LinuxBoot = Join-Path $PSScriptRoot '..\artifacts\linux-boot' }
if (-not $LinuxAgent) { $LinuxAgent = Join-Path $PSScriptRoot '..\artifacts\agent-linux\ddt-agent' }

$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$LinuxBoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LinuxBoot)
$LinuxAgent = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($LinuxAgent)
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$work = Join-Path $root 'artifacts\installer-work'
$payload = Join-Path $work 'payload'
$agent = Join-Path $work 'agent'
$console = Join-Path $work 'console'

if ($StageOnly -and $Staged) {
    throw '-StageOnly and -Staged are the two halves of one build. Pass one of them.'
}

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

# What is wrong with the Linux files in the payload, or nothing. A machine takes the server's agent when it is not
# the one its boot environment brought, so from one build the two are one file, of the server's version.
function Get-LinuxFileProblem {
    param(
        [Parameter(Mandatory)] [string] $BootFolder,
        [Parameter(Mandatory)] [string] $AgentPath,
        [Parameter(Mandatory)] [string] $Version
    )

    $manifest = Join-Path $BootFolder 'ddt-linux-boot.json'
    if (-not (Test-Path -LiteralPath $manifest) -or -not (Test-Path -LiteralPath $AgentPath)) {
        return
    }

    $inside = (Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json).PSObject.Properties['agent']
    if (-not $inside) {
        return 'The Linux boot environment holds a placeholder instead of the agent. Build it with Build-LinuxBoot.ps1 -AgentFolder.'
    }

    $hash = (Get-FileHash -LiteralPath $AgentPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($inside.Value.sha256 -ne $hash) {
        return ('The agent for Linux is not the one in the Linux boot environment, so every machine would download it at each ' +
            'start. Build both in one go: Publish-LinuxAgent.ps1, then Build-LinuxBoot.ps1 -AgentFolder.')
    }

    if ($inside.Value.version -ne $Version) {
        return "The agent for Linux is version $($inside.Value.version), and this build is $Version. Publish it again with -Version $Version."
    }
}

if (-not $Version) { $Version = & (Join-Path $PSScriptRoot 'Get-Version.ps1') -AllowShallow }

$major, $minor, $build = $Version.Split('.') | ForEach-Object { [int] $_ }
if ($major -gt 255 -or $minor -gt 255 -or $build -gt 65535) {
    throw "Windows Installer takes versions up to 255.255.65535, not $Version."
}

if ($Staged) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload 'DDT.Host.exe'))) {
        throw "Nothing is staged in $payload. Run with -StageOnly first."
    }

    # The zip that Publish-Console.ps1 wrote holds the console as it was before it was signed.
    if (Test-Path -LiteralPath $console) {
        $package = Join-Path $payload 'ddt-console.zip'
        if (Test-Path -LiteralPath $package) {
            Remove-Item -LiteralPath $package -Force
        }

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::CreateFromDirectory($console, $package, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    }
}
else {
    if (-not $SkipWeb) {
        $web = Join-Path $root 'src\DDT.Web'
        Invoke-Checked 'npm' @('ci', '--prefix', $web)
        Invoke-Checked 'npm' @('run', 'build', '--prefix', $web)
    }

    # Leftovers would end up in the MSI.
    foreach ($folder in $payload, $agent, $console) {
        if (Test-Path -LiteralPath $folder) {
            Remove-Item -LiteralPath $folder -Recurse -Force
        }
    }

    Invoke-Checked 'dotnet' @(
        'publish', (Join-Path $root 'src\DDT.Host\DDT.Host.csproj'),
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained',
        "-p:Version=$(if ($PreRelease) { "$Version-pre" } else { $Version })",
        '--output', $payload)

    # What the server offers machines until others are uploaded, and what builds a boot image on the server itself.
    if (-not $SkipAgent) {
        & (Join-Path $PSScriptRoot 'Publish-Agent.ps1') -Output $agent -Version $Version
        & (Join-Path $PSScriptRoot 'Publish-Console.ps1') -Output $console -Package (Join-Path $payload 'ddt-console.zip') -Version $Version
        Copy-Item -LiteralPath (Join-Path $agent 'ddt-agent.exe') -Destination $payload
    }

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Build-BootImage.ps1'), (Join-Path $PSScriptRoot 'boot-image-trim.txt') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'BootImage') -Destination $payload -Recurse
}

# The Linux boot environment and the agent for Linux, which Docker builds and this script does not. A release builds
# them on Linux while the programs are signed, so either half takes them when they are there.
$stagedLinuxBoot = Join-Path $payload 'linux-boot'
$stagedLinuxAgent = Join-Path $payload 'ddt-agent-linux-x64'
$how = if ($StageOnly) { '-Staged takes it when it is there by then.' } else { 'Publish-LinuxAgent.ps1 and Build-LinuxBoot.ps1 -AgentFolder build them.' }

if (Test-Path -LiteralPath (Join-Path $LinuxBoot 'ddt-linux-boot.json')) {
    if (Test-Path -LiteralPath $stagedLinuxBoot) {
        Remove-Item -LiteralPath $stagedLinuxBoot -Recurse -Force
    }

    Copy-Item -LiteralPath $LinuxBoot -Destination $stagedLinuxBoot -Recurse
}
elseif (-not (Test-Path -LiteralPath $stagedLinuxBoot)) {
    Write-Host "There is no Linux boot environment in $LinuxBoot, so the server would start no machine in one. $how"
}

if (Test-Path -LiteralPath $LinuxAgent -PathType Leaf) {
    Copy-Item -LiteralPath $LinuxAgent -Destination $stagedLinuxAgent -Force
}
elseif (-not (Test-Path -LiteralPath $stagedLinuxAgent)) {
    Write-Host "There is no agent for Linux at $LinuxAgent, so the server would offer none. $how"
}

$problem = Get-LinuxFileProblem -BootFolder $stagedLinuxBoot -AgentPath $stagedLinuxAgent -Version $Version
if ($problem -and ($StageOnly -or $Staged)) {
    throw $problem
}
elseif ($problem) {
    Write-Warning $problem
}

if ($StageOnly) {
    Write-Host "Staged version $Version in $work. -Staged builds the MSI from it."
    return
}

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

Write-Host ("Built {0}, version {1} ({2:N1} MB)" -f $msi, $Version, ((Get-Item -LiteralPath $msi).Length / 1MB))
