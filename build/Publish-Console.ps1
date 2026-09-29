# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Publishes DDT.MachineConsole as ddt-console.exe, the graphical console the agent starts in Windows PE.

.DESCRIPTION
Windows PE has no .NET runtime, so the console is compiled ahead of time into one native executable, like the agent.
This needs the Visual C++ build tools. Next to ddt-console.exe, the folder holds the two native libraries it draws
with, libSkiaSharp.dll and libHarfBuzzSharp.dll. The three files belong together. Pass the folder to
Build-BootImage.ps1 with -ConsolePath, which copies them to X:\DDT next to the agent.

It also zips the three files into -Package, at the root of the zip. Upload the zip on the server's settings page, or
name it with DDT:Agent:ConsolePath. Then it's the console that the agents of netbooting machines switch to, so a new
console doesn't need a new boot image.

.EXAMPLE
.\build\Publish-Console.ps1
#>
[CmdletBinding()]
param(
    [string] $Output,

    [string] $Package
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Defaults are set here instead of in param(), because Windows PowerShell leaves $PSScriptRoot empty
# there when the script is started with powershell -File.
if (-not $Output) { $Output = Join-Path $PSScriptRoot '..\artifacts\console' }
if (-not $Package) { $Package = Join-Path $PSScriptRoot '..\artifacts\ddt-console.zip' }

$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
$Package = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Package)
$project = Join-Path $PSScriptRoot '..\src\DDT.MachineConsole\DDT.MachineConsole.csproj'

# VsDevCmd.bat runs vswhere.exe by its bare name from the installer folder. A shell that sets
# NoDefaultCurrentDirectoryInExePath refuses that, and the NativeAOT link step then fails with exit code 123.
$installer = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer'
if (Test-Path -LiteralPath (Join-Path $installer 'vswhere.exe')) {
    $env:PATH = "$installer;$env:PATH"
}

# The folder is emptied first, so it holds only what this publish wrote and can be copied as a whole.
if (Test-Path -LiteralPath $Output) {
    Remove-Item -LiteralPath $Output -Recurse -Force
}

& dotnet publish $project --configuration Release --output $Output
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# Symbols stay out of the boot image.
Get-ChildItem -LiteralPath $Output -Filter '*.pdb' | Remove-Item -Force

foreach ($file in 'ddt-console.exe', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $Output $file))) {
        throw "The publish did not write $file."
    }
}

# ZipFile instead of Compress-Archive, because in Windows PowerShell 5.1 Compress-Archive writes backslashes into the
# entry names.
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $Package) {
    Remove-Item -LiteralPath $Package -Force
}
[System.IO.Compression.ZipFile]::CreateFromDirectory($Output, $Package, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$files = Get-ChildItem -LiteralPath $Output -File
$files | Select-Object Name, @{ Name = 'MB'; Expression = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
Write-Host ("Published {0} ({1:N1} MB in {2} files)" -f $Output, (($files | Measure-Object Length -Sum).Sum / 1MB), $files.Count)
Write-Host ("Zipped it to {0} ({1:N1} MB)" -f $Package, ((Get-Item -LiteralPath $Package).Length / 1MB))
