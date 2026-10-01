# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Builds DDT's Windows PE boot image with the Windows ADK. Build-BootImage.ps1 is its command line.

# A module doesn't inherit these two from the script that imports it.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Private') -Filter '*.ps1') {
    . $file.FullName
}

<#
.SYNOPSIS
Builds the DDT Windows PE boot files into Destination. Build-BootImage.ps1's help describes the parameters.
#>
function New-DdtBootImage {
    [CmdletBinding()]
    param(
        [string] $AgentPath,
        [string] $ServerUrl,
        [string] $RootCertificatePath,
        [string] $KeyboardLayout,
        [Parameter(Mandatory)][string] $Destination,
        [Parameter(Mandatory)][string] $WorkDirectory,
        [ValidateRange(512, 1380)][int] $TftpBlockSize = 1380,
        [ValidateRange(1, 64)][int] $TftpWindowSize = 16,
        [string] $WimLibraryPath,
        [string] $ConsolePath,
        [string] $ExtraPath,
        [string] $DriverPath,
        [string] $ApiToken,
        [string] $ServerDriverPath,
        [switch] $SkipPowerShell,
        [string] $TrimListPath,
        [switch] $SkipTrim
    )

    # Resolved against the PowerShell location. [IO.Path]::GetFullPath uses the process directory, which Set-Location
    # doesn't change. That matters because the work directory is deleted recursively.
    $Destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
    $WorkDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkDirectory)

    $adk = Get-AdkPath
    $build = Resolve-BuildInput -AgentPath $AgentPath -ServerUrl $ServerUrl -RootCertificatePath $RootCertificatePath `
        -KeyboardLayout $KeyboardLayout -ApiToken $ApiToken -DriverPath $DriverPath -ConsolePath $ConsolePath `
        -ExtraPath $ExtraPath -TrimListPath $TrimListPath -SkipTrim:$SkipTrim -WimLibraryPath $WimLibraryPath

    # Checked before anything is built, so a missing package doesn't waste a copype run.
    $packages = @(if (-not $SkipPowerShell) { Get-PowerShellComponentPackage -ComponentDirectory $adk.Components })

    # Next to the work directory instead of in it, because copype refuses a directory that already exists. The drivers
    # are downloaded before copype runs too, so a refused token or an unreachable server doesn't waste a build.
    $downloads = "$WorkDirectory-drivers"
    Remove-BuildFolder -Path $downloads
    $serverDrivers = $null
    if ($ApiToken -and $ServerDriverPath) { throw '-ApiToken and -ServerDriverPath both bring the server''s drivers. Pass one of them.' }
    if ($ApiToken) {
        $serverDrivers = Save-ServerDriver -ServerUrl $ServerUrl -RootCertificate $build.RootCertificateBytes `
            -Token $ApiToken -Folder $downloads
    }
    elseif ($ServerDriverPath) {
        $serverDrivers = Read-ServerDriverFolder -Path $ServerDriverPath
    }

    $workspace = New-WinPEWorkspace -Adk $adk -WorkDirectory $WorkDirectory
    Assert-BootManager -Pca2011Path $workspace.BootManager2011 -Uefi2023Path $workspace.BootManager2023

    Update-BootWim -Workspace $workspace -Package $packages -ServerDriver $serverDrivers -DriverPath $build.DriverPath `
        -Agent $build.Agent -ExtraPath $build.ExtraPath -TrimListPath $build.TrimListPath
    Export-BootWim -Workspace $workspace

    Write-Host "Writing the boot files to $Destination"
    $bcd = Join-Path $WorkDirectory 'BCD'
    New-BootBcd -Path $bcd -TftpBlockSize $TftpBlockSize -TftpWindowSize $TftpWindowSize
    Publish-BootFile -Workspace $workspace -BcdPath $bcd -Destination $Destination
    Write-BootManifest -Path (Join-Path $Destination 'Boot\ddt-boot-image.json') -ServerDriver $serverDrivers `
        -BootManager $workspace.BootManager2011 -AgentPath $AgentPath -ServerUrl $ServerUrl `
        -RootCertificate $build.RootCertificateBytes -KeyboardLayout $(if ($build.Agent) { $build.Agent.KeyboardLayout }) `
        -PowerShell:(-not $SkipPowerShell)

    Remove-BuildFolder -Path $downloads
    Show-BootImageSummary -Destination $Destination -SkipPowerShell:$SkipPowerShell -SkipTrim:$SkipTrim
}

Export-ModuleMember -Function New-DdtBootImage -Verbose:$false
