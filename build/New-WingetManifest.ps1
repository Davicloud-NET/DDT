# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Writes the winget manifests for a release of DDT.msi.

.DESCRIPTION
Fills in the templates in build/winget with the version, the MSI's download URL, SHA-256 and ProductCode, in the folder
layout of the winget-pkgs repository. A pull request there makes winget install Davicloud.DDT work.

.PARAMETER Msi
The DDT.msi that the release publishes.

.PARAMETER Version
The release's version, as Year.Major.Build, without the v of the tag.

.PARAMETER InstallerUrl
Where winget downloads the MSI. Defaults to the GitHub release of the version.

.EXAMPLE
.\build\New-WingetManifest.ps1 -Msi artifacts\installer\DDT.msi -Version 26.1.412
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Msi,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d{1,3}\.\d{1,3}\.\d{1,5}$')]
    [string] $Version,

    [string] $InstallerUrl,

    [string] $Output
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Defaults are set here instead of in param(), because Windows PowerShell leaves $PSScriptRoot empty
# there when the script is started with powershell -File.
if (-not $InstallerUrl) { $InstallerUrl = "https://github.com/Davicloud-NET/DDT/releases/download/v$Version/DDT.msi" }
if (-not $Output) { $Output = Join-Path $PSScriptRoot '..\artifacts\winget' }

$Msi = (Resolve-Path -LiteralPath $Msi).Path
$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)

# Windows Installer's COM object has no type library, so every call goes through InvokeMember.
function Get-MsiProperty {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Name
    )

    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($Path, 0))
    $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @("SELECT Value FROM Property WHERE Property = '$Name'"))
    [void] $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)
    $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)

    try {
        if (-not $record) { throw "$Path has no $Name." }

        return [string] $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, @([int] 1))
    }
    finally {
        [void] $view.GetType().InvokeMember('Close', 'InvokeMethod', $null, $view, $null)
        [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($view)
        [void] [Runtime.InteropServices.Marshal]::ReleaseComObject($database)
    }
}

$values = @{
    Version         = $Version
    ReleaseDate     = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
    InstallerUrl    = $InstallerUrl
    InstallerSha256 = (Get-FileHash -LiteralPath $Msi -Algorithm SHA256).Hash
    ProductCode     = Get-MsiProperty -Path $Msi -Name 'ProductCode'
}

$folder = Join-Path $Output "manifests\d\Davicloud\DDT\$Version"
New-Item -ItemType Directory -Force -Path $folder | Out-Null

foreach ($template in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'winget') -Filter '*.yaml') {
    $text = [IO.File]::ReadAllText($template.FullName)

    foreach ($key in $values.Keys) {
        $text = $text.Replace("{{$key}}", $values[$key])
    }

    if ($text -match '\{\{\w+\}\}') {
        throw "$($template.Name) has a value this script doesn't fill in: $($Matches[0])."
    }

    # Without the template line, which isn't true of the result.
    $text = ($text -split "`n" | Where-Object { $_ -notmatch '^# Template:' }) -join "`n"
    [IO.File]::WriteAllText((Join-Path $folder $template.Name), $text, (New-Object Text.UTF8Encoding $false))
}

Write-Host "Wrote the winget manifests for $Version to $folder"
