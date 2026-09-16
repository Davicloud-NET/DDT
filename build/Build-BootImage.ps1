#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
Builds the DDT Windows PE boot files and lays them out as the pxe role serves them.

.DESCRIPTION
Runs copype from the Windows ADK WinPE add-on, injects DDT.Agent and startnet.cmd into boot.wim,
writes a BCD that boots boot.wim from a RAM disk over TFTP, and publishes both Microsoft signed
boot managers. Nothing here is signed by DDT: Secure Boot sees only Microsoft's binaries.

Output layout, relative to -Destination, which is what DDT:Pxe:BootDirectory should contain:

  x64/bootmgfw.efi      boot manager signed by Microsoft Windows Production PCA 2011 (the default)
  x64/bootmgfw_ex.efi   boot manager signed by Windows UEFI CA 2023
  Boot/BCD
  Boot/boot.sdi
  Boot/boot.wim
  EFI/Microsoft/Boot/boot.stl     Secure Boot revocation list the boot manager checks
  EFI/Microsoft/Boot/Fonts/       fonts the boot manager draws its screens with

The two boot manager paths are stable. A site DHCP server that points option 67 at DDT chooses the
Secure Boot variant by naming one of them. Neither file is dual signed: a machine whose firmware db
holds only the 2011 certificate needs the first, one that has revoked it needs the second.

DISM and bcdedit both require elevation, even to read.

.PARAMETER AgentPath
DDT.Agent.exe to inject. Without it the image boots to a command prompt, which is enough to test
the netboot chain.

.PARAMETER TftpBlockSize
Written to the BCD as ramdisktftpblocksize, the block size bootmgr requests for boot.wim. DDT
never serves more than its own cap of 1380, which fits a WireGuard tunnel.

.PARAMETER TftpWindowSize
Written to the BCD as ramdisktftpwindowsize. Only 4 has Microsoft backing. DDT caps the window at
DDT:Pxe:TftpMaxWindowSize, so raise both together when measuring 8 or 16.

.EXAMPLE
.\build\Build-BootImage.ps1 -AgentPath .\artifacts\agent\DDT.Agent.exe
#>
[CmdletBinding()]
param(
    [string] $AgentPath,

    [string] $Destination = (Join-Path $PSScriptRoot '..\artifacts\boot'),

    [string] $WorkDirectory = (Join-Path $PSScriptRoot '..\artifacts\winpe'),

    [ValidateRange(512, 1380)]
    [int] $TftpBlockSize = 1380,

    [ValidateRange(1, 64)]
    [int] $TftpWindowSize = 4
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Resolved against the PowerShell location. [IO.Path]::GetFullPath uses the process directory, which
# Set-Location does not change, and the work directory is deleted recursively further down.
$Destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
$WorkDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkDirectory)
$Mount = Join-Path $WorkDirectory 'mount'

function Get-AdkPaths {
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
        WinPE   = Join-Path $adk 'Windows Preinstallation Environment'
        Copype  = Join-Path $adk 'Windows Preinstallation Environment\copype.cmd'
        Dism    = Join-Path $adk 'Deployment Tools\amd64\DISM'
        Oscdimg = Join-Path $adk 'Deployment Tools\amd64\Oscdimg'
    }

    if (-not (Test-Path -LiteralPath $paths.Copype)) {
        throw 'The Windows PE add-on for the ADK is not installed.'
    }

    return $paths
}

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [Parameter(ValueFromRemainingArguments)][string[]] $Arguments
    )

    # Standard error is left on the console: redirecting it in Windows PowerShell turns every line into
    # a terminating error under ErrorActionPreference Stop.
    $output = & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($Arguments -join ' ') failed with exit code $LASTEXITCODE.`n$($output -join "`n")"
    }

    return $output
}

function Get-BootManagerIssuer {
    param([Parameter(Mandatory)][string] $Path)

    # Get-AuthenticodeSignature cannot be used: it prefers the OS catalog and reports the 2011 PCA for
    # both files. The embedded signature is read from the PE security directory instead.
    Add-Type -AssemblyName System.Security

    $stream = [IO.File]::OpenRead($Path)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        $stream.Position = 0x3C
        $peHeader = $reader.ReadInt32()
        $stream.Position = $peHeader + 24
        $magic = $reader.ReadUInt16()

        # Data directory entry 4 is the certificate table. Its address is a file offset, not an RVA.
        $directories = if ($magic -eq 0x20B) { $peHeader + 24 + 112 } else { $peHeader + 24 + 96 }
        $stream.Position = $directories + 32
        $offset = $reader.ReadUInt32()
        if ($offset -eq 0) { return $null }

        $stream.Position = $offset
        $length = $reader.ReadUInt32()
        $null = $reader.ReadUInt16()
        $null = $reader.ReadUInt16()

        $signedCms = New-Object Security.Cryptography.Pkcs.SignedCms
        $signedCms.Decode($reader.ReadBytes($length - 8))

        return $signedCms.SignerInfos[0].Certificate.Issuer
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Assert-Issuer {
    param([string] $Path, [string] $Expected)

    $issuer = Get-BootManagerIssuer -Path $Path
    if (-not $issuer -or $issuer -notlike "*$Expected*") {
        throw "$Path is signed by '$issuer', expected '$Expected'. The ADK layout may have changed."
    }
}

function Clear-StaleMount {
    $mounted = Invoke-Native (Join-Path $adk.Dism 'dism.exe') '/English' '/Get-MountedWimInfo'
    if (($mounted -join "`n") -match [regex]::Escape($Mount)) {
        & (Join-Path $adk.Dism 'dism.exe') /Unmount-Image "/MountDir:$Mount" /Discard | Out-Null
        & (Join-Path $adk.Dism 'dism.exe') /Cleanup-Mountpoints | Out-Null
    }
}

function New-Bcd {
    param([Parameter(Mandatory)][string] $Path)

    $bcdedit = Join-Path $env:SystemRoot 'System32\bcdedit.exe'

    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }

    Invoke-Native $bcdedit /createstore $Path | Out-Null

    Invoke-Native $bcdedit /store $Path /create '{ramdiskoptions}' /d 'DDT ramdisk options' | Out-Null
    Invoke-Native $bcdedit /store $Path /set '{ramdiskoptions}' ramdisksdidevice boot | Out-Null
    Invoke-Native $bcdedit /store $Path /set '{ramdiskoptions}' ramdisksdipath '\Boot\boot.sdi' | Out-Null
    Invoke-Native $bcdedit /store $Path /set '{ramdiskoptions}' ramdisktftpblocksize $TftpBlockSize | Out-Null
    Invoke-Native $bcdedit /store $Path /set '{ramdiskoptions}' ramdisktftpwindowsize $TftpWindowSize | Out-Null

    $created = Invoke-Native $bcdedit /store $Path /create /d 'DDT Windows PE' /application osloader
    $entry = [regex]::Match(($created -join ' '), '\{[0-9a-fA-F-]{36}\}').Value
    if (-not $entry) { throw "bcdedit did not report the new loader entry: $created" }

    Invoke-Native $bcdedit /store $Path /set $entry device 'ramdisk=[boot]\Boot\boot.wim,{ramdiskoptions}' | Out-Null
    Invoke-Native $bcdedit /store $Path /set $entry osdevice 'ramdisk=[boot]\Boot\boot.wim,{ramdiskoptions}' | Out-Null
    # winload.efi, not the winload.exe in Microsoft's PXE walkthrough, which is the BIOS loader.
    Invoke-Native $bcdedit /store $Path /set $entry path '\windows\system32\winload.efi' | Out-Null
    Invoke-Native $bcdedit /store $Path /set $entry systemroot '\windows' | Out-Null
    Invoke-Native $bcdedit /store $Path /set $entry detecthal yes | Out-Null
    Invoke-Native $bcdedit /store $Path /set $entry winpe yes | Out-Null

    Invoke-Native $bcdedit /store $Path /create '{bootmgr}' /d 'DDT boot manager' | Out-Null
    Invoke-Native $bcdedit /store $Path /set '{bootmgr}' timeout 0 | Out-Null
    Invoke-Native $bcdedit /store $Path /set '{bootmgr}' default $entry | Out-Null
    Invoke-Native $bcdedit /store $Path /displayorder $entry /addlast | Out-Null
}

$adk = Get-AdkPaths

if ($AgentPath -and -not (Test-Path -LiteralPath $AgentPath)) {
    throw "Agent not found at $AgentPath."
}

Clear-StaleMount

if (Test-Path -LiteralPath $WorkDirectory) {
    Remove-Item -LiteralPath $WorkDirectory -Recurse -Force
}

# copype refuses an existing directory and reads these three variables instead of finding the ADK.
$env:WinPERoot = $adk.WinPE
$env:DISMRoot = $adk.Dism
$env:OSCDImgRoot = $adk.Oscdimg
Invoke-Native $adk.Copype amd64 $WorkDirectory | Out-Null

$bootManager2011 = Join-Path $WorkDirectory 'bootbins\bootmgfw.efi'
$bootManager2023 = Join-Path $WorkDirectory 'bootbins\bootmgfw_EX.efi'

# Add-ons before 10.1.26100.2454 produce neither file.
foreach ($file in $bootManager2011, $bootManager2023) {
    if (-not (Test-Path -LiteralPath $file)) {
        throw "copype did not produce $file. Update the Windows PE add-on to 10.1.26100.2454 or later."
    }
}

Assert-Issuer -Path $bootManager2011 -Expected 'Microsoft Windows Production PCA 2011'
Assert-Issuer -Path $bootManager2023 -Expected 'Windows UEFI CA 2023'

$wim = Join-Path $WorkDirectory 'media\sources\boot.wim'
$dism = Join-Path $adk.Dism 'dism.exe'

Invoke-Native $dism /Mount-Image "/ImageFile:$wim" /Index:1 "/MountDir:$Mount" | Out-Null
$committed = $false
try {
    if ($AgentPath) {
        New-Item -ItemType Directory -Force -Path (Join-Path $Mount 'DDT') | Out-Null
        Copy-Item -LiteralPath $AgentPath -Destination (Join-Path $Mount 'DDT\DDT.Agent.exe')
    }

    # wpeinit brings up the network. WaitForNetwork is unverified on this WinPE build; if it is not
    # recognised, the agent's own retry has to cover the time DHCP takes.
    $startnet = @(
        '@echo off'
        'wpeinit'
        'wpeutil WaitForNetwork'
        'if exist X:\DDT\DDT.Agent.exe X:\DDT\DDT.Agent.exe'
    )
    Set-Content -LiteralPath (Join-Path $Mount 'Windows\System32\startnet.cmd') -Value $startnet -Encoding Ascii

    # The NativeAOT agent imports the universal C runtime. Stock WinPE carries it; fail if that changes.
    if (-not (Test-Path -LiteralPath (Join-Path $Mount 'Windows\System32\ucrtbase.dll'))) {
        throw 'boot.wim has no ucrtbase.dll, which DDT.Agent needs.'
    }

    Invoke-Native $dism "/Image:$Mount" /Set-ScratchSpace:512 | Out-Null
    Invoke-Native $dism /Unmount-Image "/MountDir:$Mount" /Commit | Out-Null
    $committed = $true
}
finally {
    if (-not $committed) {
        & $dism /Unmount-Image "/MountDir:$Mount" /Discard | Out-Null
    }
}

New-Bcd -Path (Join-Path $WorkDirectory 'BCD')

$efiBoot = Join-Path $Destination 'EFI\Microsoft\Boot'
New-Item -ItemType Directory -Force -Path (Join-Path $Destination 'x64'), (Join-Path $Destination 'Boot'), $efiBoot | Out-Null
Copy-Item -LiteralPath $bootManager2011 -Destination (Join-Path $Destination 'x64\bootmgfw.efi') -Force
Copy-Item -LiteralPath $bootManager2023 -Destination (Join-Path $Destination 'x64\bootmgfw_ex.efi') -Force
Copy-Item -LiteralPath (Join-Path $WorkDirectory 'BCD') -Destination (Join-Path $Destination 'Boot\BCD') -Force
Copy-Item -LiteralPath (Join-Path $WorkDirectory 'media\Boot\boot.sdi') -Destination (Join-Path $Destination 'Boot\boot.sdi') -Force
Copy-Item -LiteralPath $wim -Destination (Join-Path $Destination 'Boot\boot.wim') -Force

# The boot manager asks for these under EFI\Microsoft\Boot on every boot.
Copy-Item -LiteralPath (Join-Path $WorkDirectory 'media\EFI\Microsoft\Boot\boot.stl') -Destination $efiBoot -Force
$fonts = New-Item -ItemType Directory -Force -Path (Join-Path $efiBoot 'Fonts')
Copy-Item -Path (Join-Path $WorkDirectory 'media\EFI\Microsoft\Boot\Fonts\*') -Destination $fonts.FullName -Force

Get-ChildItem -LiteralPath $Destination -Recurse -File |
    Select-Object @{ Name = 'File'; Expression = { $_.FullName.Substring($Destination.Length + 1) } },
                  @{ Name = 'MB'; Expression = { [math]::Round($_.Length / 1MB, 1) } } |
    Format-Table -AutoSize
