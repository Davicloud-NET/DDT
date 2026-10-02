# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
Builds the DDT Windows PE boot files and lays them out the way the pxe role serves them.

.DESCRIPTION
Runs copype from the Windows ADK WinPE add-on and adds DDT.Agent, its graphical console if you pass
one, and startnet.cmd to boot.wim. Then it writes a BCD that boots boot.wim from a RAM disk over
TFTP, and publishes both Microsoft signed boot managers. DDT signs nothing here, so Secure Boot only
sees Microsoft's binaries.

It also adds the Windows PE optional components that PowerShell needs, with their en-us language
packages: WinPE-WMI, WinPE-NetFx, WinPE-Scripting, WinPE-PowerShell, WinPE-DismCmdlets,
WinPE-StorageWMI and WinPE-SecureBootCmdlets. That way task sequences can run PowerShell scripts in
Windows PE. Components can't be added to a running Windows PE, so they have to be in the image.
-SkipPowerShell leaves them out.

Then it removes what DDT's Windows PE never uses, following the list in boot-image-trim.txt next to
this script. The list says for each group why it can go. A PXE netboot fetches boot.wim over TFTP, so
a smaller image saves time at every netboot. The trim runs last because it removes the servicing
stack, and nothing can be added to the image after that. -SkipTrim keeps everything, and
-TrimListPath uses a list of your own.

Either way, boot.wim is exported at the end, which drops what servicing and the trim left behind in
it. The script then prints its size in megabytes of 1,048,576 bytes.

An installed server has this script in its program folder, next to the agent and the console it came
with. Run from there without -AgentPath, it takes both, names the server by its DNS name and port
and trusts its root from the store. Without -Destination it writes a build of its own below builds
in the server's boot directory, boot in the store, and makes it the one the server serves; the build
before stays, and the Boot image page can go back to it. Each of these parameters still overrides
its default. The Build button on that page runs this script the same way.

The output layout, relative to -Destination. DDT:Pxe:BootDirectory on the server must hold the same layout.

  x64/bootmgfw.efi      boot manager signed by Microsoft Windows Production PCA 2011 (the default)
  x64/bootmgfw_ex.efi   boot manager signed by Windows UEFI CA 2023
  Boot/BCD
  Boot/boot.sdi
  Boot/boot.wim
  Boot/ddt-boot-image.json        what the build contains, for DDT's boot image page
  EFI/Microsoft/Boot/boot.stl     Secure Boot revocation list the boot manager checks
  EFI/Microsoft/Boot/Fonts/       fonts the boot manager draws its screens with

The two boot manager paths are stable. A site DHCP server that points option 67 at DDT picks the
Secure Boot variant by naming one of them. Neither file is dual signed. A machine whose firmware db
only holds the 2011 certificate needs the first, and one that has revoked it needs the second.

Drivers for a network or storage controller that Windows PE has no driver for can go into boot.wim
from a folder (-DriverPath) and from DDT itself. With -ServerUrl and -ApiToken, the script asks DDT
which driver packages are flagged for the boot image. It downloads each one, checks its SHA-256, and
adds the drivers in it. DISM refuses an unsigned driver, which Windows PE couldn't load with Secure
Boot on anyway.

Boot/ddt-boot-image.json says when the image was built, which DDT driver packages it holds, and the
versions of the ADK, the boot managers and the agent. DDT reads it to tell whether the boot image
still has the flagged drivers. It holds no secrets, because anyone who can netboot can read it, like
everything in the boot directory.

DISM and bcdedit both require elevation, even to read.

.PARAMETER AgentPath
The published agent, ddt-agent.exe from Publish-Agent.ps1. Without it the image boots to a command
prompt, which is enough to test the netboot chain.

.PARAMETER ServerUrl
The https URL the agent registers with. With -ApiToken, the drivers are downloaded from it too.
Every name in it must be in DDT's TLS certificate.

.PARAMETER RootCertificatePath
The PEM root certificate the agent trusts for the server. For DDT's own certificate this is
ddt-root.pem, next to the server certificate. In the container that's
/var/lib/ddt/certs/ddt-root.pem. The boot image pins the root, so it keeps working when DDT renews
its certificate or adds a name. For your own certificate, pass the root of its CA. Required with
-AgentPath, even for a certificate from a public CA. Windows PE only carries a handful of Microsoft
roots, not the public web ones, and the agent also fetches its own updates over this connection. The
server must send its full chain, because the agent doesn't download intermediates. Also required
with -ApiToken, because the script trusts only this root for the download, just like the agent.

.PARAMETER KeyboardLayout
The keyboard layout set in boot.wim, as input locale and layout identifiers, for example
0407:00000407 for German. Technicians type their password with it, and Windows PE otherwise assumes
US English. The default is this computer's first keyboard layout.

.PARAMETER Destination
Where the boot files go, in the layout above. The default is artifacts\boot in the repository.

.PARAMETER WorkDirectory
The folder copype works in, by default artifacts\winpe in the repository. Every build deletes it
first. The downloaded drivers go to a folder next to it, with the same name plus -drivers.

.PARAMETER TftpBlockSize
Written to the BCD as ramdisktftpblocksize, the block size bootmgr requests for boot.wim. DDT
never serves more than its own cap of 1380, which fits a WireGuard tunnel.

.PARAMETER TftpWindowSize
Written to the BCD as ramdisktftpwindowsize, the window size bootmgr asks for. Microsoft only
documents 4, but 16 is faster. DDT caps the window at DDT:Pxe:TftpMaxWindowSize, 16 by default. So a
site that needs a smaller window lowers that setting instead of rebuilding the image.

.PARAMETER WimLibraryPath
Your own libwim-15.dll, for example one built from modified wimlib source, as wimlib's licence (the
GNU LGPL) allows. It's copied to X:\DDT\libwim-15.dll, next to the agent. The agent then uses it
instead of its built-in copy and logs both SHA-256 values. Needs -AgentPath.

.PARAMETER ConsolePath
The folder Publish-Console.ps1 wrote. It holds ddt-console.exe, the graphical console, and the two
libraries it draws with, libSkiaSharp.dll and libHarfBuzzSharp.dll. The three files are copied to
X:\DDT, next to the agent. The agent starts the console and shows the run there, not only on the
text console. The console speaks one version of the console protocol. If the agent updates itself to
a version that speaks another, it falls back to the text console until the boot image is rebuilt.
Needs -AgentPath.

.PARAMETER ExtraPath
For development. A folder that's copied as is, without .pdb files, to X:\Extra, so you can try a
program in Windows PE, such as a candidate for the console. Nothing starts it, so run it from the
prompt.

.PARAMETER DriverPath
A folder of drivers to add to boot.wim. DISM adds every .inf below it, with the files each names.

.PARAMETER ServerDriverPath
A folder of driver packages that DDT unpacked itself, one subfolder per package named by its id, with
drivers.json naming them as GET /api/boot-image does. The Build button passes it, so its build needs
no API token. Not together with -ApiToken.

.PARAMETER ApiToken
An administrator's API token, ddt_ followed by 43 letters and digits. Create one on the Account
page or with POST /api/tokens. With -ServerUrl and -RootCertificatePath, the script uses it to
download the driver packages flagged for the boot image and adds their drivers. The token only
authorizes the download. It isn't put into the image or the build description. A token that expires
within a day is enough, and revoking it afterwards costs nothing.

.PARAMETER SkipPowerShell
Builds the lean image without the PowerShell components, for sites where netboot time matters more.
A task sequence step that runs PowerShell in Windows PE cannot run on machines booted from it.

.PARAMETER TrimListPath
The list of what to remove from boot.wim, by default boot-image-trim.txt next to this script. Its
first lines explain the format. The build stops when a list removes a file Windows PE needs to
start, such as ntoskrnl.exe.

.PARAMETER SkipTrim
Keeps every Windows PE file, for example to find out whether the trim causes a problem.

.EXAMPLE
.\build\Build-BootImage.ps1 -AgentPath .\artifacts\agent\ddt-agent.exe -ServerUrl https://ddt.example:8443 -RootCertificatePath .\ddt-root.pem

.EXAMPLE
.\build\Build-BootImage.ps1 -AgentPath .\artifacts\agent\ddt-agent.exe -ConsolePath .\artifacts\console -ServerUrl https://ddt.example:8443 -RootCertificatePath .\ddt-root.pem

Adds the graphical console that Publish-Console.ps1 published to artifacts\console.

.EXAMPLE
.\build\Build-BootImage.ps1 -AgentPath .\artifacts\agent\ddt-agent.exe -ServerUrl https://ddt.example:8443 -RootCertificatePath .\ddt-root.pem -ApiToken $env:DDT_API_TOKEN -DriverPath .\drivers\winpe

Adds the driver packages flagged for the boot image on the server, and the drivers in a local folder.
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
    [int] $TftpWindowSize = 16,

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

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Next to an installed server, the build takes what the release brought and what the installer set up.
. (Join-Path $PSScriptRoot 'BootImage\Private\Release.ps1')
$release = Get-ReleaseDefault -Folder $PSScriptRoot

# A build of its own below the server's boot directory, which the server then serves
$servedBuild = $null

if ($release) {
    if (-not $Destination) {
        $servedBuild = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss', [Globalization.CultureInfo]::InvariantCulture)
        $Destination = Join-Path (Join-Path $release.Destination 'builds') $servedBuild
    }

    if (-not $WorkDirectory) { $WorkDirectory = $release.WorkDirectory }

    if (-not $AgentPath -and $release.AgentPath) {
        $AgentPath = $release.AgentPath
        if (-not $ServerUrl) { $ServerUrl = $release.ServerUrl }
        if (-not $RootCertificatePath -and $release.RootCertificatePath) { $RootCertificatePath = $release.RootCertificatePath }

        if (-not $ConsolePath -and $release.ConsolePackage) {
            $ConsolePath = Expand-ReleaseConsole -Package $release.ConsolePackage -WorkDirectory $WorkDirectory
        }
    }
}

# A builder from the Boot image page builds for the server it came from, with what its folder holds
$builder = if ($release) { $null } else { Get-BuilderDefault -Folder $PSScriptRoot }

if ($builder) {
    if (-not $Destination) { $Destination = $builder.Destination }
    if (-not $WorkDirectory) { $WorkDirectory = $builder.WorkDirectory }
    if (-not $AgentPath) { $AgentPath = $builder.AgentPath }
    if (-not $ServerUrl) { $ServerUrl = $builder.ServerUrl }
    if (-not $RootCertificatePath) { $RootCertificatePath = $builder.RootCertificatePath }
    if (-not $ServerDriverPath -and -not $ApiToken) { $ServerDriverPath = $builder.ServerDriverPath }

    if (-not $ConsolePath -and $builder.ConsolePackage) {
        $ConsolePath = Expand-ReleaseConsole -Package $builder.ConsolePackage -WorkDirectory $WorkDirectory
    }
}

# Defaults are set here instead of in param(), because Windows PowerShell leaves $PSScriptRoot empty
# there when the script is started with powershell -File.
if (-not $Destination) { $Destination = Join-Path $PSScriptRoot '..\artifacts\boot' }
if (-not $WorkDirectory) { $WorkDirectory = Join-Path $PSScriptRoot '..\artifacts\winpe' }
if (-not $TrimListPath) { $TrimListPath = Join-Path $PSScriptRoot 'boot-image-trim.txt' }

Import-Module (Join-Path $PSScriptRoot 'BootImage\DdtBootImage.psm1') -Force -Verbose:$false

$build = @{
    AgentPath           = $AgentPath
    ServerUrl           = $ServerUrl
    RootCertificatePath = $RootCertificatePath
    KeyboardLayout      = $KeyboardLayout
    Destination         = $Destination
    WorkDirectory       = $WorkDirectory
    TftpBlockSize       = $TftpBlockSize
    TftpWindowSize      = $TftpWindowSize
    WimLibraryPath      = $WimLibraryPath
    ConsolePath         = $ConsolePath
    ExtraPath           = $ExtraPath
    DriverPath          = $DriverPath
    ApiToken            = $ApiToken
    ServerDriverPath    = $ServerDriverPath
    SkipPowerShell      = $SkipPowerShell
    TrimListPath        = $TrimListPath
    SkipTrim            = $SkipTrim
}

# Asked before the build, which takes minutes: a token that no longer uploads would waste it
if ($builder) {
    Test-DdtBuilderToken -ServerUrl $builder.ServerUrl -RootCertificatePath $builder.RootCertificatePath -Token $builder.UploadToken
}

# A module doesn't see the preferences set for this script, so the two that its commands use are passed along.
New-DdtBootImage @build -WarningAction $WarningPreference -Verbose:($VerbosePreference -ne 'SilentlyContinue')

if ($builder) {
    Send-DdtBootImage -ServerUrl $builder.ServerUrl -RootCertificatePath $builder.RootCertificatePath -Token $builder.UploadToken -Folder $Destination
}

if ($servedBuild) {
    Set-CurrentBootBuild -BootDirectory $release.Destination -Build $servedBuild
    Write-Host "The server serves this build, $servedBuild, from now on."
}
