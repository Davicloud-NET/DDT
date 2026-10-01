# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Runs copype into a fresh work directory and returns the paths of what the build uses.
function New-WinPEWorkspace {
    param(
        [Parameter(Mandatory)] $Adk,
        [Parameter(Mandatory)][string] $WorkDirectory
    )

    $workspace = [pscustomobject]@{
        Directory        = $WorkDirectory
        Dism             = Join-Path $Adk.Dism 'dism.exe'
        ImageFile        = Join-Path $WorkDirectory 'media\sources\boot.wim'
        MountDirectory   = Join-Path $WorkDirectory 'mount'
        ScratchDirectory = Join-Path $WorkDirectory 'scratch'
        BootManager2011  = Join-Path $WorkDirectory 'bootbins\bootmgfw.efi'
        BootManager2023  = Join-Path $WorkDirectory 'bootbins\bootmgfw_EX.efi'
    }

    Clear-StaleMount -Dism $workspace.Dism -MountDirectory $workspace.MountDirectory
    Remove-BuildFolder -Path $WorkDirectory

    # copype refuses an existing directory. It reads these three variables instead of finding the ADK itself.
    $env:WinPERoot = $Adk.WinPE
    $env:DISMRoot = $Adk.Dism
    $env:OSCDImgRoot = $Adk.Oscdimg
    Write-Host 'Copying Windows PE from the ADK'
    Invoke-Native $Adk.Copype amd64 $WorkDirectory | Out-Null

    return $workspace
}

# Discards an image a failed build left mounted, which would keep the work directory from being deleted.
function Clear-StaleMount {
    param(
        [Parameter(Mandatory)][string] $Dism,
        [Parameter(Mandatory)][string] $MountDirectory
    )

    $mounted = Invoke-Native $Dism '/English' '/Get-MountedWimInfo'
    if (($mounted -join "`n") -match [regex]::Escape($MountDirectory)) {
        & $Dism /Unmount-Image "/MountDir:$MountDirectory" /Discard | Out-Null
        & $Dism /Cleanup-Mountpoints | Out-Null
    }
}

function Remove-BuildFolder {
    param([Parameter(Mandatory)][string] $Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}
