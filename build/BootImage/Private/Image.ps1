# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Mounts boot.wim, adds what the build asks for, and commits it. A failure discards every change.
function Update-BootWim {
    param(
        [Parameter(Mandatory)] $Workspace,
        [string[]] $Package = @(),
        $ServerDriver,
        [string] $DriverPath,
        $Agent,
        [string] $ExtraPath,
        [string] $TrimListPath
    )

    $dism = $Workspace.Dism
    $mount = $Workspace.MountDirectory

    Invoke-Native $dism /Mount-Image "/ImageFile:$($Workspace.ImageFile)" /Index:1 "/MountDir:$mount" | Out-Null
    $committed = $false
    try {
        Install-ImageComponent -Dism $dism -MountDirectory $mount -Package $Package
        Add-ImageDriver -Dism $dism -MountDirectory $mount -ServerDriver $ServerDriver -DriverPath $DriverPath
        if ($Agent) { Install-DdtAgent -MountDirectory $mount -Agent $Agent }
        if ($ExtraPath) { Copy-ExtraFile -MountDirectory $mount -ExtraPath $ExtraPath }
        Write-StartNet -MountDirectory $mount

        # The NativeAOT agent imports the universal C runtime. Stock WinPE has it, and the build fails if that changes.
        if (-not (Test-Path -LiteralPath (Join-Path $mount 'Windows\System32\ucrtbase.dll'))) {
            throw 'boot.wim has no ucrtbase.dll, which DDT.Agent needs.'
        }

        # Set in the image instead of with wpeutil SetKeyboardLayout in startnet.cmd. Field reports say that only
        # reaches consoles opened after it, and the agent runs in the first one.
        if ($Agent -and $Agent.KeyboardLayout) {
            Invoke-Native $dism "/Image:$mount" "/Set-InputLocale:$($Agent.KeyboardLayout)" | Out-Null
        }

        Invoke-Native $dism "/Image:$mount" /Set-ScratchSpace:512 | Out-Null

        # Makes the added packages permanent and removes the component versions they replaced. Microsoft documents
        # this step for a serviced Windows PE image.
        if ($Package.Count -gt 0) {
            $scratch = New-Item -ItemType Directory -Force -Path $Workspace.ScratchDirectory
            Invoke-Native $dism "/Image:$mount" /Cleanup-Image /StartComponentCleanup /ResetBase "/ScratchDir:$($scratch.FullName)" | Out-Null
        }

        # Runs last, because it removes the servicing stack that DISM used above.
        if ($TrimListPath) { Remove-TrimmedFile -MountDirectory $mount -ListPath $TrimListPath }

        Invoke-Native $dism /Unmount-Image "/MountDir:$mount" /Commit | Out-Null
        $committed = $true
    }
    finally {
        if (-not $committed) {
            & $dism /Unmount-Image "/MountDir:$mount" /Discard | Out-Null
        }
    }
}

function Install-ImageComponent {
    param(
        [Parameter(Mandatory)][string] $Dism,
        [Parameter(Mandatory)][string] $MountDirectory,
        [string[]] $Package = @()
    )

    foreach ($path in $Package) {
        Write-Host "Adding $(Split-Path -Leaf $path)"
        Invoke-Native $Dism "/Image:$MountDirectory" /Add-Package "/PackagePath:$path" | Out-Null
    }
}

# Adds the drivers of the packages downloaded from DDT, then those in the local folder.
function Add-ImageDriver {
    param(
        [Parameter(Mandatory)][string] $Dism,
        [Parameter(Mandatory)][string] $MountDirectory,
        $ServerDriver,
        [string] $DriverPath
    )

    # No /ForceUnsigned. Windows PE couldn't load an unsigned driver with Secure Boot on, so DISM refusing it here
    # fails earlier and more clearly.
    if ($ServerDriver) {
        foreach ($driver in $ServerDriver.Drivers) {
            Write-Host "Adding the drivers of $($driver.name)"
            $folder = Join-Path $ServerDriver.Folder ([string] $driver.packageId)
            Invoke-Native $Dism "/Image:$MountDirectory" /Add-Driver "/Driver:$folder" /Recurse | Out-Null
        }
    }

    if ($DriverPath) {
        Write-Host "Adding the drivers in $DriverPath"
        Invoke-Native $Dism "/Image:$MountDirectory" /Add-Driver "/Driver:$DriverPath" /Recurse | Out-Null
    }
}

# Copies the agent, and the libwim and console that go with it, to X:\DDT and writes its agent.json.
function Install-DdtAgent {
    param(
        [Parameter(Mandatory)][string] $MountDirectory,
        [Parameter(Mandatory)] $Agent
    )

    $folder = Join-Path $MountDirectory 'DDT'
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    Copy-Item -LiteralPath $Agent.Path -Destination (Join-Path $folder 'ddt-agent.exe')

    # The agent uses a libwim-15.dll it finds next to itself instead of writing out its own copy.
    if ($Agent.WimLibraryPath) {
        Copy-Item -LiteralPath $Agent.WimLibraryPath -Destination (Join-Path $folder 'libwim-15.dll')
    }

    # The agent starts ddt-console.exe when it finds it next to itself.
    foreach ($file in $Agent.ConsoleFiles) {
        Copy-Item -LiteralPath $file -Destination (Join-Path $folder (Split-Path -Leaf $file))
    }

    $configuration = [ordered]@{
        serverUrl       = $Agent.ServerUrl
        rootCertificate = $Agent.RootCertificate
        keyboardLayout  = $Agent.KeyboardLayoutName
    }

    # Written without a byte order mark, which Windows PowerShell's Set-Content -Encoding UTF8 adds.
    [IO.File]::WriteAllText(
        (Join-Path $folder 'agent.json'),
        ($configuration | ConvertTo-Json),
        (New-Object Text.UTF8Encoding $false))
}

# Copies a folder as it is, without .pdb files, to X:\Extra.
function Copy-ExtraFile {
    param(
        [Parameter(Mandatory)][string] $MountDirectory,
        [Parameter(Mandatory)][string] $ExtraPath
    )

    $extra = Join-Path $MountDirectory 'Extra'
    New-Item -ItemType Directory -Force -Path $extra | Out-Null
    Get-ChildItem -LiteralPath $ExtraPath -Recurse -File | Where-Object Extension -ne '.pdb' | ForEach-Object {
        $target = Join-Path $extra $_.FullName.Substring($ExtraPath.Length).TrimStart('\')
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target
    }
}

function Write-StartNet {
    param([Parameter(Mandatory)][string] $MountDirectory)

    # wpeinit brings up the network. WaitForNetwork isn't verified on this WinPE build. If it isn't recognised, the
    # agent's own retry covers the time DHCP takes. The PATH entry lets you run ddt-agent --licenses at the prompt
    # that's left after the agent stops, as the agent's legal notices say.
    $startnet = @(
        '@echo off'
        'wpeinit'
        'wpeutil WaitForNetwork'
        'set PATH=%PATH%;X:\DDT'
        'if exist X:\DDT\ddt-agent.exe X:\DDT\ddt-agent.exe'
    )
    Set-Content -LiteralPath (Join-Path $MountDirectory 'Windows\System32\startnet.cmd') -Value $startnet -Encoding Ascii
}

# Replaces boot.wim with an export of it. Committing a mounted image adds what changed but keeps what it replaced in
# the file. An export only copies what the image still uses. It stays bootable, like copype's boot.wim.
function Export-BootWim {
    param([Parameter(Mandatory)] $Workspace)

    $wim = $Workspace.ImageFile
    $exported = Join-Path $Workspace.Directory 'boot-exported.wim'
    Invoke-Native $Workspace.Dism /Export-Image "/SourceImageFile:$wim" /SourceIndex:1 "/DestinationImageFile:$exported" /Compress:max /Bootable | Out-Null
    Move-Item -LiteralPath $exported -Destination $wim -Force
}
