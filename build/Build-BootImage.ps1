# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
Builds the DDT Windows PE boot files and lays them out as the pxe role serves them.

.DESCRIPTION
Runs copype from the Windows ADK WinPE add-on, injects DDT.Agent and startnet.cmd into boot.wim,
writes a BCD that boots boot.wim from a RAM disk over TFTP, and publishes both Microsoft signed
boot managers. Nothing here is signed by DDT: Secure Boot sees only Microsoft's binaries.

It also adds the Windows PE optional components PowerShell needs, WinPE-WMI, WinPE-NetFx,
WinPE-Scripting, WinPE-PowerShell, WinPE-DismCmdlets, WinPE-StorageWMI and WinPE-SecureBootCmdlets,
with their en-us language packages, so task sequences can run PowerShell scripts in Windows PE.
Components cannot be added to a running Windows PE, so they have to be in the image. They make
boot.wim, which a PXE netboot fetches over TFTP, about 140 MB larger: 473 MB against 332 MB on the
test machine. -SkipPowerShell leaves them out. Either way boot.wim is exported at the end, which
drops what servicing left behind in it, and its size is printed, in megabytes of 1,048,576 bytes.

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
The published agent, ddt-agent.exe from Publish-Agent.ps1. Without it the image boots to a command
prompt, which is enough to test the netboot chain.

.PARAMETER ServerUrl
The https URL the agent registers with. Every name in it must be in DDT's TLS certificate.

.PARAMETER RootCertificatePath
The PEM root the agent trusts for the server. For DDT's own certificate this is ddt-root.pem, next to
the server certificate: /var/lib/ddt/certs/ddt-root.pem in the container. The boot image pins the
root, so it keeps working when DDT renews its certificate or adds a name. A boot image built with the
self-signed ddt.pem of a DDT from before it had a root needs this rebuild once. For a certificate of
your own, pass the root of its CA. Required with -AgentPath, even for a certificate from a public CA:
Windows PE carries only a handful of Microsoft roots, not the public web ones, and the agent also
fetches its own updates over this connection. The server must send its full chain, because the agent
does not download intermediates.

.PARAMETER KeyboardLayout
The keyboard layout set in boot.wim, as input locale and layout identifiers, for example
0407:00000407 for German. Technicians type their password with it, and Windows PE otherwise assumes
US English. The default is this computer's first keyboard layout.

.PARAMETER TftpBlockSize
Written to the BCD as ramdisktftpblocksize, the block size bootmgr requests for boot.wim. DDT
never serves more than its own cap of 1380, which fits a WireGuard tunnel.

.PARAMETER TftpWindowSize
Written to the BCD as ramdisktftpwindowsize. Only 4 has Microsoft backing. DDT caps the window at
DDT:Pxe:TftpMaxWindowSize, so raise both together when measuring 8 or 16.

.PARAMETER WimLibraryPath
A libwim-15.dll of your own, for example one built from modified wimlib source, as wimlib's licence,
the GNU LGPL, provides for. It is copied to X:\DDT\libwim-15.dll, next to the agent, which then uses
it instead of the copy it carries and logs both SHA-256 values. Needs -AgentPath.

.PARAMETER SkipPowerShell
Builds the lean image without the PowerShell components, for sites where netboot time matters more.
A task sequence step that runs PowerShell in Windows PE cannot run on machines booted from it.

.EXAMPLE
.\build\Build-BootImage.ps1 -AgentPath .\artifacts\agent\ddt-agent.exe -ServerUrl https://ddt.example:8443 -RootCertificatePath .\ddt-root.pem
#>
[CmdletBinding()]
param(
    [string] $AgentPath,

    [string] $ServerUrl,

    [string] $RootCertificatePath,

    [string] $KeyboardLayout,

    [string] $Destination,

    [string] $WorkDirectory,

    [ValidateRange(512, 1380)]
    [int] $TftpBlockSize = 1380,

    [ValidateRange(1, 64)]
    [int] $TftpWindowSize = 4,

    [string] $WimLibraryPath,

    [switch] $SkipPowerShell
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Defaults are resolved here rather than in param(): Windows PowerShell leaves $PSScriptRoot empty
# there when the script is started with powershell -File.
if (-not $Destination) { $Destination = Join-Path $PSScriptRoot '..\artifacts\boot' }
if (-not $WorkDirectory) { $WorkDirectory = Join-Path $PSScriptRoot '..\artifacts\winpe' }

# Resolved against the PowerShell location. [IO.Path]::GetFullPath uses the process directory, which
# Set-Location does not change, and the work directory is deleted recursively further down.
$Destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
$WorkDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkDirectory)
$Mount = Join-Path $WorkDirectory 'mount'

# WMI, NetFx, Scripting and PowerShell in that order, which the other three each need.
# WinPE-SecureBootCmdlets has no language resources, so the ADK ships no en-us package for it.
$powerShellComponents = @(
    [pscustomobject]@{ Name = 'WinPE-WMI'; HasLanguagePackage = $true }
    [pscustomobject]@{ Name = 'WinPE-NetFx'; HasLanguagePackage = $true }
    [pscustomobject]@{ Name = 'WinPE-Scripting'; HasLanguagePackage = $true }
    [pscustomobject]@{ Name = 'WinPE-PowerShell'; HasLanguagePackage = $true }
    [pscustomobject]@{ Name = 'WinPE-DismCmdlets'; HasLanguagePackage = $true }
    [pscustomobject]@{ Name = 'WinPE-StorageWMI'; HasLanguagePackage = $true }
    [pscustomobject]@{ Name = 'WinPE-SecureBootCmdlets'; HasLanguagePackage = $false }
)

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

function Get-ComponentPackages {
    # A language package has to match the image's language, and copype's image is en-us.
    $packages = foreach ($component in $powerShellComponents) {
        Join-Path $adk.Components "$($component.Name).cab"
        if ($component.HasLanguagePackage) {
            Join-Path $adk.Components "en-us\$($component.Name)_en-us.cab"
        }
    }

    foreach ($package in $packages) {
        if (-not (Test-Path -LiteralPath $package)) {
            throw "$package is missing from the Windows PE add-on. Repair the add-on, or build with -SkipPowerShell."
        }
    }

    return $packages
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

if ($AgentPath) {
    if (-not (Test-Path -LiteralPath $AgentPath)) {
        throw "Agent not found at $AgentPath."
    }

    if (-not $ServerUrl -or -not $RootCertificatePath) {
        throw 'An agent needs -ServerUrl and -RootCertificatePath to reach the server.'
    }

    if (-not $KeyboardLayout) {
        $KeyboardLayout = Get-WinUserLanguageList |
            ForEach-Object { $_.InputMethodTips } |
            Where-Object { $_ -match '^[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8}$' } |
            Select-Object -First 1
    }

    $keyboardLayoutName = $null

    if ($KeyboardLayout) {
        if ($KeyboardLayout -notmatch '^[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8}$') {
            throw "-KeyboardLayout takes an identifier such as 0407:00000407, not $KeyboardLayout."
        }

        # The agent shows this name at the sign in prompt, where a wrong layout otherwise looks like a wrong password.
        $layoutKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layouts\$($KeyboardLayout.Split(':')[1])"
        $keyboardLayoutName = if (Test-Path -LiteralPath $layoutKey) { (Get-ItemProperty -LiteralPath $layoutKey).'Layout Text' } else { $KeyboardLayout }
    }

    $rootCertificate = $null

    if ($RootCertificatePath) {
        if (-not (Test-Path -LiteralPath $RootCertificatePath)) {
            throw "Root certificate not found at $RootCertificatePath."
        }

        # Read as a plain string: Get-Content attaches properties that ConvertTo-Json writes out as an object.
        $rootCertificate = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $RootCertificatePath).ProviderPath)

        if ($rootCertificate -notmatch '-----BEGIN CERTIFICATE-----') {
            throw "$RootCertificatePath is not a PEM certificate."
        }

        # Anyone who can netboot can read boot.wim.
        if ($rootCertificate -match 'PRIVATE KEY') {
            throw "$RootCertificatePath contains a private key. Export the certificate alone."
        }

        # A server certificate pinned in place of its root stops working at its next renewal, weeks later.
        $base64 = ($rootCertificate -split '-----BEGIN CERTIFICATE-----')[1]
        $base64 = ($base64 -split '-----END CERTIFICATE-----')[0] -replace '\s', ''
        $pinned = [Security.Cryptography.X509Certificates.X509Certificate2]::new([Convert]::FromBase64String($base64))
        $constraints = $pinned.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension] }
        if (-not $constraints -or -not $constraints.CertificateAuthority) {
            Write-Warning ("$RootCertificatePath is not a CA certificate: $($pinned.Subject). The boot image stops reaching " +
                'DDT when that certificate is replaced. For DDT''s own certificate pass ddt-root.pem instead.')
        }
    }
}

if ($WimLibraryPath) {
    if (-not $AgentPath) {
        throw 'A libwim needs -AgentPath: only the agent uses it.'
    }

    if (-not (Test-Path -LiteralPath $WimLibraryPath -PathType Leaf)) {
        throw "libwim not found at $WimLibraryPath."
    }
}

# Checked before anything is built, so a missing package does not cost a copype run first.
$packages = @(if (-not $SkipPowerShell) { Get-ComponentPackages })

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
    foreach ($package in $packages) {
        Write-Host "Adding $(Split-Path -Leaf $package)"
        Invoke-Native $dism "/Image:$Mount" /Add-Package "/PackagePath:$package" | Out-Null
    }

    if ($AgentPath) {
        New-Item -ItemType Directory -Force -Path (Join-Path $Mount 'DDT') | Out-Null
        Copy-Item -LiteralPath $AgentPath -Destination (Join-Path $Mount 'DDT\ddt-agent.exe')

        # The agent uses a libwim-15.dll it finds next to itself instead of writing out its own copy.
        if ($WimLibraryPath) {
            Copy-Item -LiteralPath $WimLibraryPath -Destination (Join-Path $Mount 'DDT\libwim-15.dll')
        }

        $configuration = [ordered]@{
            serverUrl       = $ServerUrl
            rootCertificate = $rootCertificate
            keyboardLayout  = $keyboardLayoutName
        }

        # Written without a byte order mark, which Windows PowerShell's Set-Content -Encoding UTF8 adds.
        [IO.File]::WriteAllText(
            (Join-Path $Mount 'DDT\agent.json'),
            ($configuration | ConvertTo-Json),
            (New-Object Text.UTF8Encoding $false))
    }

    # wpeinit brings up the network. WaitForNetwork is unverified on this WinPE build; if it is not
    # recognised, the agent's own retry has to cover the time DHCP takes. The path lets the prompt
    # left after the agent stops run ddt-agent --licenses, as the agent's legal notices say.
    $startnet = @(
        '@echo off'
        'wpeinit'
        'wpeutil WaitForNetwork'
        'set PATH=%PATH%;X:\DDT'
        'if exist X:\DDT\ddt-agent.exe X:\DDT\ddt-agent.exe'
    )
    Set-Content -LiteralPath (Join-Path $Mount 'Windows\System32\startnet.cmd') -Value $startnet -Encoding Ascii

    # The NativeAOT agent imports the universal C runtime. Stock WinPE carries it; fail if that changes.
    if (-not (Test-Path -LiteralPath (Join-Path $Mount 'Windows\System32\ucrtbase.dll'))) {
        throw 'boot.wim has no ucrtbase.dll, which DDT.Agent needs.'
    }

    # Set in the image rather than with wpeutil SetKeyboardLayout in startnet.cmd, which by field reports only
    # reaches consoles opened after it, and the agent runs in the first one.
    if ($AgentPath -and $KeyboardLayout) {
        Invoke-Native $dism "/Image:$Mount" "/Set-InputLocale:$KeyboardLayout" | Out-Null
    }

    Invoke-Native $dism "/Image:$Mount" /Set-ScratchSpace:512 | Out-Null

    # Makes the added packages permanent and removes the component versions they superseded, the step
    # Microsoft documents for a serviced Windows PE image.
    if ($packages.Count -gt 0) {
        $scratch = New-Item -ItemType Directory -Force -Path (Join-Path $WorkDirectory 'scratch')
        Invoke-Native $dism "/Image:$Mount" /Cleanup-Image /StartComponentCleanup /ResetBase "/ScratchDir:$($scratch.FullName)" | Out-Null
    }

    Invoke-Native $dism /Unmount-Image "/MountDir:$Mount" /Commit | Out-Null
    $committed = $true
}
finally {
    if (-not $committed) {
        & $dism /Unmount-Image "/MountDir:$Mount" /Discard | Out-Null
    }
}

# Committing a mounted image adds what changed and keeps what it replaced in the file; an export copies
# only what the image still uses. copype's boot.wim marks its one image bootable, boot index 1, and the
# copy is marked the same way.
$exported = Join-Path $WorkDirectory 'boot-exported.wim'
Invoke-Native $dism /Export-Image "/SourceImageFile:$wim" /SourceIndex:1 "/DestinationImageFile:$exported" /Compress:max /Bootable | Out-Null
Move-Item -LiteralPath $exported -Destination $wim -Force

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

# A PXE netboot fetches boot.wim over TFTP, so its size is most of what the netboot takes.
$variant = if ($SkipPowerShell) { 'without PowerShell' } else { 'with PowerShell' }
Write-Host ('boot.wim, {0}: {1:N1} MB' -f $variant, ((Get-Item -LiteralPath (Join-Path $Destination 'Boot\boot.wim')).Length / 1MB))
