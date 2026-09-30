# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Installs the DDT server on Windows from a release.

.DESCRIPTION
Downloads DDT.msi, checks it against the release's SHA256SUMS, installs it quietly and waits for the DDT service to
listen. Then it prints the address, the SHA-256 of DDT's root and where the first password is. Each release carries
this script as install.ps1.

In an elevated PowerShell:

  irm https://github.com/Davicloud-NET/DDT/releases/latest/download/install.ps1 | iex

With settings:

  & ([scriptblock]::Create((irm https://github.com/Davicloud-NET/DDT/releases/latest/download/install.ps1))) -Port 443

.PARAMETER Port
The HTTPS port, on a new install. 8443 by default.

.PARAMETER StoreFolder
Where the store goes, on a new install. %ProgramData%\DDT by default.

.PARAMETER NetbootInterface
The network card that answers netboot, by name, such as Ethernet. Unset, PXE stays off until someone picks one in DDT.

.PARAMETER Database
A PostgreSQL connection string. Without one, DDT keeps its database in SQLite in the store.

.PARAMETER Version
A release, as 0.1.0, instead of the latest one.

.PARAMETER Source
A folder or URL with DDT.msi and SHA256SUMS, instead of a release. For testing a build.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 65535)]
    [int] $Port = 8443,

    [string] $StoreFolder,

    [string] $NetbootInterface,

    [string] $Database,

    [ValidatePattern('^\d{1,3}\.\d{1,3}\.\d{1,5}$')]
    [string] $Version,

    [string] $Source
)

# A child scope: through irm | iex, strict mode and preferences would stay in the caller's shell.
& {
    param([int] $Port, [string] $StoreFolder, [string] $NetbootInterface, [string] $Database, [string] $Version, [string] $Source)

    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'

    $principal = New-Object Security.Principal.WindowsPrincipal ([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Installing DDT needs an elevated PowerShell. Open one with Run as administrator and try again.'
    }

    $build = [int] (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
    if ($build -lt 17763) {
        throw 'DDT needs Windows Server 2019 or later, or Windows 10 version 1809 or later.'
    }

    # GitHub only speaks TLS 1.2 and later, which Windows PowerShell doesn't always offer by default.
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    $release = if ($Source) {
        $Source.TrimEnd('/', '\')
    }
    elseif ($Version) {
        "https://github.com/Davicloud-NET/DDT/releases/download/v$Version"
    }
    else {
        'https://github.com/Davicloud-NET/DDT/releases/latest/download'
    }

    $work = Join-Path ([IO.Path]::GetTempPath()) ('ddt-install-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    $msi = Join-Path $work 'DDT.msi'
    $log = Join-Path $work 'DDT.msi.log'
    $sumsFile = Join-Path $work 'SHA256SUMS'

    Write-Host "Getting DDT.msi from $release"
    if ($release -match '^https?://') {
        Invoke-WebRequest -UseBasicParsing -Uri "$release/DDT.msi" -OutFile $msi
        Invoke-WebRequest -UseBasicParsing -Uri "$release/SHA256SUMS" -OutFile $sumsFile
    }
    else {
        Copy-Item -LiteralPath (Join-Path $release 'DDT.msi') -Destination $msi
        Copy-Item -LiteralPath (Join-Path $release 'SHA256SUMS') -Destination $sumsFile
    }

    $sums = [IO.File]::ReadAllText($sumsFile)
    $expected = ($sums -split "`n" | Where-Object { $_ -match '^\s*([0-9a-fA-F]{64})\s+\*?DDT\.msi\s*$' } | Select-Object -First 1)
    if (-not $expected) {
        throw "The release's SHA256SUMS names no DDT.msi."
    }

    if ((Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash -ne ($expected -split '\s+')[0].Trim().ToUpperInvariant()) {
        throw "DDT.msi doesn't match the release's SHA256SUMS. Nothing was installed."
    }

    $arguments = @('/i', "`"$msi`"", '/qn', '/norestart', '/l*v', "`"$log`"", "PORT=$Port")
    if ($StoreFolder) { $arguments += "STOREFOLDER=`"$($StoreFolder.TrimEnd('\'))`"" }
    if ($NetbootInterface) { $arguments += "NETBOOTINTERFACE=`"$NetbootInterface`"" }
    if ($Database) { $arguments += "CONNECTIONSTRING=`"$Database`"" }

    Write-Host 'Installing DDT'
    $started = Get-Date
    $installed = Start-Process -FilePath msiexec.exe -ArgumentList $arguments -Wait -PassThru
    if ($installed.ExitCode -notin 0, 3010) {
        throw "Windows Installer failed with exit code $($installed.ExitCode). Its log is $log."
    }

    # An upgrade keeps what ddt.ini already says, so the port and the store come from there.
    $settings = @{}
    $section = ''
    $bootstrap = Join-Path $env:ProgramData 'DDT\ddt.ini'
    foreach ($line in [IO.File]::ReadAllLines($bootstrap)) {
        if ($line -match '^\s*\[(.+)\]\s*$') { $section = $Matches[1] }
        elseif ($line -match '^\s*([^=;]+?)\s*=\s*(.*)$') { $settings["${section}:$($Matches[1])"] = $Matches[2].Trim() }
    }

    $listening = if ($settings['Kestrel:Endpoints:Https:Url'] -match ':(\d+)/?$') { [int] $Matches[1] } else { $Port }
    $store = if ($settings['DDT:StorePath']) { $settings['DDT:StorePath'] } else { Join-Path $env:ProgramData 'DDT' }

    Write-Host "Waiting for DDT to listen on port $listening"
    $deadline = (Get-Date).AddMinutes(2)
    while (-not (Get-NetTCPConnection -LocalPort $listening -State Listen -ErrorAction SilentlyContinue)) {
        # The service restarts after a crash, so only the event log shows one
        $crash = Get-WinEvent -FilterHashtable @{ LogName = 'Application'; ProviderName = '.NET Runtime'; Level = 2; StartTime = $started } -ErrorAction SilentlyContinue |
            Where-Object { $_.Message -match 'DDT\.Host\.exe' } |
            Select-Object -First 1
        if ($crash -and $crash.Message -match 'Exception Info: (.+)') {
            throw "DDT stopped while starting: $($Matches[1].Trim()) The Application event log has the rest."
        }

        if ((Get-Date) -gt $deadline) {
            throw "The DDT service isn't listening on port $listening after two minutes. See the Application event log."
        }

        Start-Sleep -Seconds 2
    }

    # The name the certificate has. Not the FQDN or the NetBIOS name.
    $name = [Net.Dns]::GetHostName()
    Write-Host ''
    Write-Host "DDT runs at https://${name}:$listening/"

    $root = Join-Path $store 'certs\ddt-root.pem'
    if (Test-Path -LiteralPath $root) {
        $base64 = ([IO.File]::ReadAllText($root) -replace '-----[^-]+-----', '' -replace '\s', '')
        $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2 (, [Convert]::FromBase64String($base64))
        $fingerprint = (Get-FileHash -InputStream (New-Object IO.MemoryStream (, $certificate.RawData)) -Algorithm SHA256).Hash
        Write-Host "Its root's SHA-256: $fingerprint"
    }

    $password = Join-Path $store 'first-admin.txt'
    if (Test-Path -LiteralPath $password) {
        Write-Host "Sign in as admin. The password is in $password."
    }

    Remove-Item -LiteralPath $work -Recurse -Force
} -Port $Port -StoreFolder $StoreFolder -NetbootInterface $NetbootInterface -Database $Database -Version $Version -Source $Source
