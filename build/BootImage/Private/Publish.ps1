# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Copies the boot files to Destination in the layout the pxe role serves.
function Publish-BootFile {
    param(
        [Parameter(Mandatory)] $Workspace,
        [Parameter(Mandatory)][string] $BcdPath,
        [Parameter(Mandatory)][string] $Destination
    )

    $media = Join-Path $Workspace.Directory 'media'
    $efiBoot = Join-Path $Destination 'EFI\Microsoft\Boot'
    New-Item -ItemType Directory -Force -Path (Join-Path $Destination 'x64'), (Join-Path $Destination 'Boot'), $efiBoot | Out-Null
    Copy-Item -LiteralPath $Workspace.BootManager2011 -Destination (Join-Path $Destination 'x64\bootmgfw.efi') -Force
    Copy-Item -LiteralPath $Workspace.BootManager2023 -Destination (Join-Path $Destination 'x64\bootmgfw_ex.efi') -Force
    Copy-Item -LiteralPath $BcdPath -Destination (Join-Path $Destination 'Boot\BCD') -Force
    Copy-Item -LiteralPath (Join-Path $media 'Boot\boot.sdi') -Destination (Join-Path $Destination 'Boot\boot.sdi') -Force
    Copy-Item -LiteralPath $Workspace.ImageFile -Destination (Join-Path $Destination 'Boot\boot.wim') -Force

    # The boot manager asks for these under EFI\Microsoft\Boot on every boot.
    Copy-Item -LiteralPath (Join-Path $media 'EFI\Microsoft\Boot\boot.stl') -Destination $efiBoot -Force
    $fonts = New-Item -ItemType Directory -Force -Path (Join-Path $efiBoot 'Fonts')
    Copy-Item -Path (Join-Path $media 'EFI\Microsoft\Boot\Fonts\*') -Destination $fonts.FullName -Force
}

# Writes ddt-boot-image.json, which tells DDT's boot image page what the build holds. It goes after boot.wim, so that a
# boot directory that has it has the image it describes.
function Write-BootManifest {
    param(
        [Parameter(Mandatory)][string] $Path,
        $ServerDriver,
        [Parameter(Mandatory)][string] $BootManager,
        [string] $AgentPath
    )

    # The time is a string, because Windows PowerShell writes a DateTime as \/Date()\/.
    $manifest = [ordered]@{
        builtUtc      = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        driverSetHash = if ($ServerDriver) { $ServerDriver.DriverSetHash } else { $null }
        drivers       = @(if ($ServerDriver) {
            foreach ($driver in $ServerDriver.Drivers) {
                [ordered]@{ packageId = [string] $driver.packageId; name = [string] $driver.name; sha256 = [string] $driver.sha256 }
            }
        })
        adkVersion    = Get-AdkVersion
        bootManager   = Get-FileVersion -Path $BootManager
        agentVersion  = if ($AgentPath) { Get-FileVersion -Path $AgentPath } else { $null }
    }

    # Without a byte order mark, which Windows PowerShell's Set-Content -Encoding UTF8 would add.
    [IO.File]::WriteAllText($Path, ($manifest | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
}

function Get-FileVersion {
    param([Parameter(Mandatory)][string] $Path)

    $version = (Get-Item -LiteralPath $Path).VersionInfo.ProductVersion

    if ([string]::IsNullOrWhiteSpace($version)) { return $null }

    return $version.Trim()
}

# Lists the boot files with their sizes and prints the size of boot.wim, which is most of what a netboot takes.
function Show-BootImageSummary {
    param(
        [Parameter(Mandatory)][string] $Destination,
        [switch] $SkipPowerShell,
        [switch] $SkipTrim
    )

    Get-ChildItem -LiteralPath $Destination -Recurse -File |
        Select-Object @{ Name = 'File'; Expression = { $_.FullName.Substring($Destination.Length + 1) } },
                      @{ Name = 'MB'; Expression = { [math]::Round($_.Length / 1MB, 1) } } |
        Format-Table -AutoSize

    $variant = if ($SkipPowerShell) { 'without PowerShell' } else { 'with PowerShell' }
    $variant += if ($SkipTrim) { ', untrimmed' } else { ', trimmed' }
    Write-Host ('boot.wim, {0}: {1:N1} MB' -f $variant, ((Get-Item -LiteralPath (Join-Path $Destination 'Boot\boot.wim')).Length / 1MB))
}
