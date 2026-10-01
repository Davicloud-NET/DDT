# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# A release puts Build-BootImage.ps1 next to the server, with the agent and the console it came with. Run from there,
# a build takes those, names the server and its root as the installer printed them, and writes into the server's boot
# directory. Null when the folder holds no server: a clone of the repository keeps its defaults.
function Get-ReleaseDefault {
    param(
        [Parameter(Mandatory)][string] $Folder,
        [string] $ProgramData = $env:ProgramData
    )

    if (-not (Test-Path -LiteralPath (Join-Path $Folder 'DDT.Host.exe'))) { return $null }

    # ddt.ini, the bootstrap file the installer wrote
    $settings = @{}
    $section = ''
    $bootstrap = Join-Path $ProgramData 'DDT\ddt.ini'

    if (Test-Path -LiteralPath $bootstrap) {
        foreach ($line in [IO.File]::ReadAllLines($bootstrap)) {
            if ($line -match '^\s*\[(.+)\]\s*$') { $section = $Matches[1] }
            elseif ($line -match '^\s*([^=;]+?)\s*=\s*(.*)$') { $settings["${section}:$($Matches[1])"] = $Matches[2].Trim() }
        }
    }

    $store = if ($settings['DDT:StorePath']) { $settings['DDT:StorePath'].TrimEnd('\') } else { Join-Path $ProgramData 'DDT' }
    $port = if ($settings['Kestrel:Endpoints:Https:Url'] -match ':(\d+)/?$') { $Matches[1] } else { '8443' }

    # The DNS name, as DDT's certificate has it (ServerNames.DnsName)
    $name = [Net.Dns]::GetHostName()
    $domain = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().DomainName.Trim('.')
    if ($domain -and $name -notmatch '\.' -and [Uri]::CheckHostName("$name.$domain") -eq 'Dns') { $name = "$name.$domain" }

    $agent = Join-Path $Folder 'ddt-agent.exe'
    $console = Join-Path $Folder 'ddt-console.zip'
    $root = Join-Path $store 'certs\ddt-root.pem'

    [pscustomobject]@{
        AgentPath           = if (Test-Path -LiteralPath $agent) { $agent } else { $null }
        ConsolePackage      = if (Test-Path -LiteralPath $console) { $console } else { $null }
        ServerUrl           = "https://${name}:$port"
        RootCertificatePath = if (Test-Path -LiteralPath $root) { $root } else { $null }
        Destination         = Join-Path $store 'boot'
        WorkDirectory       = Join-Path $store 'winpe'
    }
}

# Names the build the server serves, in the file "current" of its boot directory. Written next to it and renamed, so
# the server never reads half a name; it may hold the file open for an instant.
function Set-CurrentBootBuild {
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'Part of a build that was asked for.')]
    param(
        [Parameter(Mandatory)][string] $BootDirectory,
        [Parameter(Mandatory)][ValidatePattern('^[0-9A-Za-z][0-9A-Za-z_-]{0,63}$')][string] $Build
    )

    $marker = Join-Path $BootDirectory 'current'
    $next = "$marker.next"
    [IO.File]::WriteAllText($next, $Build)

    for ($attempt = 1; ; $attempt++) {
        try {
            if (Test-Path -LiteralPath $marker) { [IO.File]::Replace($next, $marker, $null) } else { [IO.File]::Move($next, $marker) }
            return
        }
        catch {
            if ($attempt -ge 50) { throw }
            Start-Sleep -Milliseconds 20
        }
    }
}

# -ConsolePath takes the console's folder, and a release carries it as the zip the server offers. Unpacked beside the
# work directory, which the build empties.
function Expand-ReleaseConsole {
    param(
        [Parameter(Mandatory)][string] $Package,
        [Parameter(Mandatory)][string] $WorkDirectory
    )

    $folder = $WorkDirectory.TrimEnd('\') + '-console'
    if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $folder | Out-Null

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($Package, $folder)

    return $folder
}
