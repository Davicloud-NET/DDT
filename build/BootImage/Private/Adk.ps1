# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# The folders and tools of the installed Windows ADK and its Windows PE add-on.
function Get-AdkPath {
    # Read from the registry rather than running DandISetEnv.bat, which changes PATH and the current
    # directory of the calling shell.
    $kits = $null
    foreach ($key in 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots',
                     'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots') {
        $value = Get-ItemProperty -Path $key -Name KitsRoot10 -ErrorAction SilentlyContinue
        if ($value) { $kits = $value.KitsRoot10; break }
    }

    if (-not $kits) {
        throw 'The Windows ADK is not installed.'
    }

    $adk = Join-Path $kits 'Assessment and Deployment Kit'
    $paths = [pscustomobject]@{
        WinPE      = Join-Path $adk 'Windows Preinstallation Environment'
        Copype     = Join-Path $adk 'Windows Preinstallation Environment\copype.cmd'
        Components = Join-Path $adk 'Windows Preinstallation Environment\amd64\WinPE_OCs'
        Dism       = Join-Path $adk 'Deployment Tools\amd64\DISM'
        Oscdimg    = Join-Path $adk 'Deployment Tools\amd64\Oscdimg'
    }

    if (-not (Test-Path -LiteralPath $paths.Copype)) {
        throw 'The Windows PE add-on for the ADK is not installed.'
    }

    return $paths
}

# The version the ADK's installers register, which Microsoft uses to name its releases, such as 10.1.26100.2454. The
# Windows PE add-on's version wins, because boot.wim comes from it.
function Get-AdkVersion {
    # Strict mode requires checking the properties before reading them, because some uninstall entries lack them.
    $entries = @(Get-ItemProperty -Path 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
                                        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.PSObject.Properties['DisplayName'] -and $_.PSObject.Properties['DisplayVersion'] -and
                       $_.DisplayName -like 'Windows Assessment and Deployment Kit*' })

    $addOn = @($entries | Where-Object { $_.DisplayName -like '*Preinstallation Environment*' })
    $entry = @($addOn + $entries) | Select-Object -First 1

    if ($entry) { return [string] $entry.DisplayVersion }

    return $null
}

# The packages of the Windows PE optional components that PowerShell needs. A running Windows PE can't add them.
function Get-PowerShellComponentPackage {
    param([Parameter(Mandatory)][string] $ComponentDirectory)

    # WMI, NetFx, Scripting and PowerShell come first and in that order, because each of the other three needs them.
    # WinPE-SecureBootCmdlets has no language resources, so the ADK ships no en-us package for it.
    $components = @(
        [pscustomobject]@{ Name = 'WinPE-WMI'; HasLanguagePackage = $true }
        [pscustomobject]@{ Name = 'WinPE-NetFx'; HasLanguagePackage = $true }
        [pscustomobject]@{ Name = 'WinPE-Scripting'; HasLanguagePackage = $true }
        [pscustomobject]@{ Name = 'WinPE-PowerShell'; HasLanguagePackage = $true }
        [pscustomobject]@{ Name = 'WinPE-DismCmdlets'; HasLanguagePackage = $true }
        [pscustomobject]@{ Name = 'WinPE-StorageWMI'; HasLanguagePackage = $true }
        [pscustomobject]@{ Name = 'WinPE-SecureBootCmdlets'; HasLanguagePackage = $false }
    )

    # A language package has to match the image's language, and copype's image is en-us.
    $packages = foreach ($component in $components) {
        Join-Path $ComponentDirectory "$($component.Name).cab"
        if ($component.HasLanguagePackage) {
            Join-Path $ComponentDirectory "en-us\$($component.Name)_en-us.cab"
        }
    }

    foreach ($package in $packages) {
        if (-not (Test-Path -LiteralPath $package)) {
            throw "$package is missing from the Windows PE add-on. Repair the add-on, or build with -SkipPowerShell."
        }
    }

    return $packages
}
