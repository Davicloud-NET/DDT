# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
Builds the DDT Windows PE boot files and lays them out as the pxe role serves them.

.DESCRIPTION
Runs copype from the Windows ADK WinPE add-on, injects DDT.Agent, its graphical console where one is
given, and startnet.cmd into boot.wim, writes a BCD that boots boot.wim from a RAM disk over TFTP,
and publishes both Microsoft signed boot managers. Nothing here is signed by DDT: Secure Boot sees
only Microsoft's binaries.

It also adds the Windows PE optional components PowerShell needs, WinPE-WMI, WinPE-NetFx,
WinPE-Scripting, WinPE-PowerShell, WinPE-DismCmdlets, WinPE-StorageWMI and WinPE-SecureBootCmdlets,
with their en-us language packages, so task sequences can run PowerShell scripts in Windows PE.
Components cannot be added to a running Windows PE, so they have to be in the image. -SkipPowerShell
leaves them out.

Then it removes what DDT's Windows PE never uses, by the list in boot-image-trim.txt next to this
script, which says for each group why it can go. boot.wim is what a PXE netboot fetches over TFTP, so
this is time saved at every netboot. The trim runs last, as it removes the servicing stack, so nothing
can be added to the image afterwards. -SkipTrim keeps everything, and -TrimListPath takes a list of
your own.

Either way boot.wim is exported at the end, which drops what servicing and the trim left behind in
it, and its size is printed, in megabytes of 1,048,576 bytes.

Output layout, relative to -Destination, which is what DDT:Pxe:BootDirectory should contain:

  x64/bootmgfw.efi      boot manager signed by Microsoft Windows Production PCA 2011 (the default)
  x64/bootmgfw_ex.efi   boot manager signed by Windows UEFI CA 2023
  Boot/BCD
  Boot/boot.sdi
  Boot/boot.wim
  Boot/ddt-boot-image.json        what the build holds, for DDT's boot image page
  EFI/Microsoft/Boot/boot.stl     Secure Boot revocation list the boot manager checks
  EFI/Microsoft/Boot/Fonts/       fonts the boot manager draws its screens with

The two boot manager paths are stable. A site DHCP server that points option 67 at DDT chooses the
Secure Boot variant by naming one of them. Neither file is dual signed: a machine whose firmware db
holds only the 2011 certificate needs the first, one that has revoked it needs the second.

Drivers for a network or storage controller that Windows PE has no driver for can go into boot.wim
from a folder, -DriverPath, and from DDT itself: with -ServerUrl and -ApiToken the script asks DDT
which driver packages are flagged for the boot image, downloads each, checks its SHA-256, and adds
the drivers it holds. DISM refuses a driver that is not signed, which Windows PE could not load with
Secure Boot on anyway.

Boot/ddt-boot-image.json says when the image was built, which DDT driver packages it holds, and the
versions of the ADK, the boot managers and the agent. DDT reads it to tell whether the boot image
still carries the drivers that are flagged. It holds no secret: like everything in the boot
directory, anyone who can netboot can read it.

DISM and bcdedit both require elevation, even to read.

.PARAMETER AgentPath
The published agent, ddt-agent.exe from Publish-Agent.ps1. Without it the image boots to a command
prompt, which is enough to test the netboot chain.

.PARAMETER ServerUrl
The https URL the agent registers with, and the one the drivers are downloaded from with -ApiToken.
Every name in it must be in DDT's TLS certificate.

.PARAMETER RootCertificatePath
The PEM root the agent trusts for the server. For DDT's own certificate this is ddt-root.pem, next to
the server certificate: /var/lib/ddt/certs/ddt-root.pem in the container. The boot image pins the
root, so it keeps working when DDT renews its certificate or adds a name. For a certificate of your
own, pass the root of its CA. Required with -AgentPath, even for a certificate from a public CA:
Windows PE carries only a handful of Microsoft roots, not the public web ones, and the agent also
fetches its own updates over this connection. The server must send its full chain, because the agent
does not download intermediates. Required with -ApiToken too: the script trusts this root, and only
it, for the download, the way the agent does.

.PARAMETER KeyboardLayout
The keyboard layout set in boot.wim, as input locale and layout identifiers, for example
0407:00000407 for German. Technicians type their password with it, and Windows PE otherwise assumes
US English. The default is this computer's first keyboard layout.

.PARAMETER Destination
Where the boot files go, in the layout above. The default is artifacts\boot in the repository.

.PARAMETER WorkDirectory
Where copype works, by default artifacts\winpe in the repository. Every build deletes it first, and
the downloaded drivers go to a folder next to it, named after it with -drivers.

.PARAMETER TftpBlockSize
Written to the BCD as ramdisktftpblocksize, the block size bootmgr requests for boot.wim. DDT
never serves more than its own cap of 1380, which fits a WireGuard tunnel.

.PARAMETER TftpWindowSize
Written to the BCD as ramdisktftpwindowsize, the window bootmgr asks for. Microsoft documents only 4;
16 is faster. DDT caps the window at DDT:Pxe:TftpMaxWindowSize, 16 by default, so a site that needs
a smaller window lowers that instead of building again.

.PARAMETER WimLibraryPath
A libwim-15.dll of your own, for example one built from modified wimlib source, as wimlib's licence,
the GNU LGPL, provides for. It is copied to X:\DDT\libwim-15.dll, next to the agent, which then uses
it instead of the copy it carries and logs both SHA-256 values. Needs -AgentPath.

.PARAMETER ConsolePath
The folder Publish-Console.ps1 wrote, with ddt-console.exe, the graphical console, and the two
libraries it draws with, libSkiaSharp.dll and libHarfBuzzSharp.dll. The three are copied to X:\DDT,
next to the agent, which starts the console and shows the run on it rather than on the text console
alone. The console speaks one version of the console protocol, and an agent that updates itself to
one speaking another falls back to the text console until the boot image is built again. Needs
-AgentPath.

.PARAMETER ExtraPath
For development: a folder copied as it is, without .pdb files, to X:\Extra, to try a program in
Windows PE, such as a candidate for the console. Nothing starts it; run it from the prompt.

.PARAMETER DriverPath
A folder of drivers to add to boot.wim. DISM adds every .inf below it, with the files each names.

.PARAMETER ApiToken
An API token of an administrator, ddt_ and 43 letters and digits, made on the Account page or with
POST /api/tokens. With -ServerUrl and -RootCertificatePath the script downloads the driver packages
flagged for the boot image with it and adds their drivers. The token only authorizes the download:
it is not put into the image or the description of the build. A token that expires within a day is
enough, and revoking it afterwards costs nothing.

.PARAMETER SkipPowerShell
Builds the lean image without the PowerShell components, for sites where netboot time matters more.
A task sequence step that runs PowerShell in Windows PE cannot run on machines booted from it.

.PARAMETER TrimListPath
The list of what to remove from boot.wim, by default boot-image-trim.txt next to this script. Its
first lines explain the format. The build stops when a list removes a file Windows PE needs to
start, such as ntoskrnl.exe.

.PARAMETER SkipTrim
Keeps every file of Windows PE, for example to find out whether the trim is behind a problem.

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

    [switch] $SkipPowerShell,

    [string] $TrimListPath,

    [switch] $SkipTrim
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Defaults are resolved here rather than in param(): Windows PowerShell leaves $PSScriptRoot empty
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
    SkipPowerShell      = $SkipPowerShell
    TrimListPath        = $TrimListPath
    SkipTrim            = $SkipTrim
}

# A module sees none of the preferences set for this script, so the two its commands act on are passed on.
New-DdtBootImage @build -WarningAction $WarningPreference -Verbose:($VerbosePreference -ne 'SilentlyContinue')
