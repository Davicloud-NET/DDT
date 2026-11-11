# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Writes the gzipped MSI fixture for the installer tests with Windows Installer's own automation object.

.DESCRIPTION
The MSI holds only a Property table and the summary information, in code page 1252, with a product name that isn't
ASCII. It installs nothing. Windows only. Prints the SHA-256 of the uncompressed MSI.

.PARAMETER Path
Where the gzipped MSI goes.

.EXAMPLE
.\tests\DDT.Server.Tests\Fixtures\Installers\New-MsiFixture.ps1 -Path .\tests\DDT.Server.Tests\Fixtures\Installers\properties.msi.gz
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$createMode = 3
$summaryCodePage = 1

# Late-bound calls, since the database objects carry no type information PowerShell could bind to.
function Invoke-ComMethod {
    param([object]$Target, [string]$Name, [object[]]$Arguments = @())

    return [System.__ComObject].InvokeMember($Name, [System.Reflection.BindingFlags]::InvokeMethod, $null, $Target, $Arguments)
}

function Set-ComProperty {
    param([object]$Target, [string]$Name, [object[]]$Arguments)

    [void][System.__ComObject].InvokeMember($Name, [System.Reflection.BindingFlags]::SetProperty, $null, $Target, $Arguments)
}

function Invoke-Sql {
    param([object]$Database, [string]$Query, [object]$Record = $null)

    $view = Invoke-ComMethod -Target $Database -Name 'OpenView' -Arguments @($Query)
    $values = if ($null -eq $Record) { @() } else { @(, $Record) }
    [void](Invoke-ComMethod -Target $view -Name 'Execute' -Arguments $values)
    [void](Invoke-ComMethod -Target $view -Name 'Close')
}

function New-PropertyDatabase {
    param([string]$MsiPath)

    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = Invoke-ComMethod -Target $installer -Name 'OpenDatabase' -Arguments @($MsiPath, $createMode)

    # The code page is set by importing the _ForceCodepage pseudo table.
    $idt = [System.IO.Path]::GetDirectoryName($MsiPath)
    [System.IO.File]::WriteAllText([System.IO.Path]::Combine($idt, 'codepage.idt'), "`r`n`r`n1252`t_ForceCodepage`r`n", [System.Text.Encoding]::ASCII)
    [void](Invoke-ComMethod -Target $database -Name 'Import' -Arguments @($idt, 'codepage.idt'))

    Invoke-Sql -Database $database -Query 'CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` LONGCHAR NOT NULL LOCALIZABLE PRIMARY KEY `Property`)'

    $properties = [ordered]@{
        ProductCode     = '{6F1C2E5A-3B7D-4C9E-8A21-0D4F5B6C7E81}'
        # "DDT Prüfpaket Größe", escaped since Windows PowerShell reads a script without a BOM as ANSI.
        ProductName     = "DDT Pr$([char]0xFC)fpaket Gr$([char]0xF6)$([char]0xDF)e"
        ProductVersion  = '1.2.3'
        Manufacturer    = 'Davicloud'
        UpgradeCode     = '{0A9B8C7D-6E5F-4A3B-9C2D-1E0F2A3B4C5D}'
        ProductLanguage = '1033'
        ALLUSERS        = '1'
    }

    foreach ($name in $properties.Keys) {
        $record = Invoke-ComMethod -Target $installer -Name 'CreateRecord' -Arguments @(2)
        Set-ComProperty -Target $record -Name 'StringData' -Arguments @(1, $name)
        Set-ComProperty -Target $record -Name 'StringData' -Arguments @(2, $properties[$name])
        Invoke-Sql -Database $database -Query 'INSERT INTO `Property` (`Property`, `Value`) VALUES (?, ?)' -Record $record
    }

    $summary = [System.__ComObject].InvokeMember('SummaryInformation', [System.Reflection.BindingFlags]::GetProperty, $null, $database, @(6))
    Set-ComProperty -Target $summary -Name 'Property' -Arguments @($summaryCodePage, 1252)
    Set-ComProperty -Target $summary -Name 'Property' -Arguments @(2, 'DDT installer fixture')
    Set-ComProperty -Target $summary -Name 'Property' -Arguments @(7, 'x64;1033')
    Set-ComProperty -Target $summary -Name 'Property' -Arguments @(9, '{3C5E7A9B-1D2F-4E6A-8B0C-9D1E2F3A4B5C}')
    Set-ComProperty -Target $summary -Name 'Property' -Arguments @(14, 500)
    Set-ComProperty -Target $summary -Name 'Property' -Arguments @(15, 2)
    [void](Invoke-ComMethod -Target $summary -Name 'Persist')
    [void](Invoke-ComMethod -Target $database -Name 'Commit')

    # msi.dll keeps the file open until the last reference goes.
    foreach ($handle in @($summary, $database, $installer)) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($handle)
    }

    [System.GC]::Collect()
    [System.GC]::WaitForPendingFinalizers()
}

function Write-Gzip {
    param([string]$Source, [string]$Destination)

    $output = [System.IO.File]::Create($Destination)

    try {
        $gzip = New-Object System.IO.Compression.GZipStream($output, [System.IO.Compression.CompressionMode]::Compress)
        $bytes = [System.IO.File]::ReadAllBytes($Source)
        $gzip.Write($bytes, 0, $bytes.Length)
        $gzip.Dispose()
    }
    finally {
        $output.Dispose()
    }
}

$work = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), [System.IO.Path]::GetRandomFileName())
New-Item -ItemType Directory -Path $work | Out-Null

try {
    $msi = [System.IO.Path]::Combine($work, 'properties.msi')
    New-PropertyDatabase -MsiPath $msi
    Write-Gzip -Source $msi -Destination ([System.IO.Path]::GetFullPath($Path))
    (Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant()
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force
}
