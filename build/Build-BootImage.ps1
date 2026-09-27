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
script, which says for each group why it can go: 32-bit Windows, which the ADK's Windows PE cannot run
anyway, precompiled .NET assemblies, the servicing stack, shell resources, .NET assemblies no script
loads, boot files, ICU, the debugger engine and a few fonts. boot.wim is what a PXE netboot fetches
over TFTP, so this is time saved at every netboot. On the test machine, with the PowerShell
components, the agent and the console, boot.wim went from 486 MB to 305 MB, and TFTP sent it to a
virtual machine in 5.2 to 6.4 seconds instead of 8.4. Without the PowerShell components it comes to
about 237 MB. The trim runs last, as it removes the servicing stack, so nothing can be added to the image
afterwards. -SkipTrim keeps everything, and -TrimListPath takes a list of your own.

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
the drivers it holds. Either way DISM adds every .inf below the folder, and refuses a driver that
is not signed, which Windows PE could not load with Secure Boot on anyway.

Every build writes Boot/ddt-boot-image.json next to boot.wim: when it was built, the DDT driver
packages it holds with their SHA-256, the driver set hash DDT gave for them, the ADK version, the
version of the boot managers and that of the agent. DDT reads it to tell whether the boot image
still carries the drivers that are flagged, and says so on the boot image page. It holds no secret:
like everything in the boot directory, anyone who can netboot can read it.

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
root, so it keeps working when DDT renews its certificate or adds a name. A boot image built with the
self-signed ddt.pem of a DDT from before it had a root needs this rebuild once. For a certificate of
your own, pass the root of its CA. Required with -AgentPath, even for a certificate from a public CA:
Windows PE carries only a handful of Microsoft roots, not the public web ones, and the agent also
fetches its own updates over this connection. The server must send its full chain, because the agent
does not download intermediates. Required with -ApiToken too: the script trusts this root, and only
it, for the download, the way the agent does.

.PARAMETER KeyboardLayout
The keyboard layout set in boot.wim, as input locale and layout identifiers, for example
0407:00000407 for German. Technicians type their password with it, and Windows PE otherwise assumes
US English. The default is this computer's first keyboard layout.

.PARAMETER TftpBlockSize
Written to the BCD as ramdisktftpblocksize, the block size bootmgr requests for boot.wim. DDT
never serves more than its own cap of 1380, which fits a WireGuard tunnel.

.PARAMETER TftpWindowSize
Written to the BCD as ramdisktftpwindowsize, the window bootmgr asks for. Only 4 has Microsoft
backing, but 16 measured reliable and faster. DDT caps the window at DDT:Pxe:TftpMaxWindowSize,
16 by default, so a site that needs a smaller window lowers that instead of building again.

.PARAMETER WimLibraryPath
A libwim-15.dll of your own, for example one built from modified wimlib source, as wimlib's licence,
the GNU LGPL, provides for. It is copied to X:\DDT\libwim-15.dll, next to the agent, which then uses
it instead of the copy it carries and logs both SHA-256 values. Needs -AgentPath.

.PARAMETER ConsolePath
The folder Publish-Console.ps1 wrote, with ddt-console.exe, the graphical console, and the two
libraries it draws with, libSkiaSharp.dll and libHarfBuzzSharp.dll. The three are copied to X:\DDT,
next to the agent, which starts the console and shows the run on it rather than on the text console
alone. They add about 12 MB to boot.wim, 29 MB unpacked. The console speaks one version of the
console protocol, and an agent that updates itself to one speaking another falls back to the text
console until the boot image is built again. Needs -AgentPath.

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

# Resolved against the PowerShell location. [IO.Path]::GetFullPath uses the process directory, which
# Set-Location does not change, and the work directory is deleted recursively further down.
$Destination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
$WorkDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($WorkDirectory)
$Mount = Join-Path $WorkDirectory 'mount'

# Beside the work directory rather than in it, because copype refuses a directory that exists, and the drivers are
# downloaded before copype runs, so a refused token costs no build.
$DriverDownloads = "$WorkDirectory-drivers"

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

function Remove-TrimmedFiles {
    param(
        [Parameter(Mandatory)][string] $MountDirectory,
        [Parameter(Mandatory)][string] $ListPath
    )

    # Compiled rather than a script, for the privileges, the hard links and the tens of thousands of files. C# 5, which
    # Windows PowerShell 5.1 compiles. Windows PE's files belong to TrustedInstaller and many are read-only, so they are
    # opened for backup, which the backup and restore privileges of an administrator allow, and deleted as they are:
    # their owner and permissions stay, which matters for a file that keeps another name. Paths go to Windows with the
    # \\?\ prefix, as some in WinSxS pass 260 characters below the mount directory.
    if (-not ('DdtBootImageTrim' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

public static class DdtBootImageTrim
{
    private const uint Delete = 0x00010000;
    private const uint ShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;
    private const int FileDispositionInfoEx = 21;
    private const uint DispositionFlags = 0x1 | 0x2 | 0x10; // delete, POSIX semantics, ignore the read-only attribute
    private const uint DirectoryAttribute = 0x10;
    private const uint ReparsePointAttribute = 0x400;
    private const int ErrorNoMoreFiles = 18;
    private const int ErrorHandleEof = 38;
    private const int ErrorMoreData = 234;
    private const int ErrorDirNotEmpty = 145;
    private const int ErrorNotAllAssigned = 1300;
    private static readonly IntPtr InvalidHandle = new IntPtr(-1);

    // A copy in a WinSxS component folder, the second name most files in Windows PE have.
    private static readonly Regex ComponentCopy = new Regex(@"^\\Windows\\WinSxS\\(amd64|x86|wow64|msil)_[^\\]+\\", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivilege
    {
        public uint Count;
        public long Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindData
    {
        public uint Attributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint SizeHigh, SizeLow, Reserved0, Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AlternateName;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string system, string name, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivilege state, uint length, out TokenPrivilege previous, out uint returned);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindFirstFileExW(string name, int infoLevel, out FindData data, int searchOp, IntPtr filter, int flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool FindNextFileW(IntPtr find, out FindData data);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindFirstFileNameW(string name, uint flags, ref uint length, StringBuilder linkName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool FindNextFileNameW(IntPtr find, ref uint length, StringBuilder linkName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindClose(IntPtr find);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref uint info, uint length);

    // Removes what the lines name from the Windows PE mounted at mount and returns how many files and folders went.
    // A line is a path in the image; * stands for any characters within one name, and a path takes everything below
    // it. A line starting with ! keeps what it matches. A file whose other names are all removed or copies in a WinSxS
    // component folder goes with all its names; one that has a name anywhere else keeps it and loses only the others.
    public static int[] Trim(string mount, string[] lines)
    {
        List<string> removes = new List<string>();
        List<string> keeps = new List<string>();
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
            {
                continue;
            }

            bool keep = line.StartsWith("!");
            string path = keep ? line.Substring(1) : line;
            if (!path.StartsWith("\\") || path.Contains("/"))
            {
                throw new ArgumentException("'" + line + "' is not a path in the image from its root, such as \\Windows\\Fonts\\sylfaen.ttf.");
            }

            (keep ? keeps : removes).Add(Regex.Escape(path.TrimEnd('\\')).Replace(@"\*", @"[^\\]*"));
        }

        Regex removed = Pattern(removes);
        Regex kept = Pattern(keeps);

        string root = Path.GetFullPath(mount).TrimEnd('\\');
        // Names of a file come back relative to its volume's root, without the drive.
        string volumeRelative = root.Substring(Path.GetPathRoot(root).Length - 1);

        List<string> files = new List<string>();
        List<string> folders = new List<string>();

        long[] previous = Enable("SeBackupPrivilege", "SeRestorePrivilege");
        try
        {
            List(root, "", files, folders);

            HashSet<string> doomed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                if (!Taken(file, removed, kept) || doomed.Contains(file))
                {
                    continue;
                }

                List<string> names = Names(root, volumeRelative, file);
                bool whole = true;
                foreach (string name in names)
                {
                    if (name == null || !(Taken(name, removed, kept) || ComponentCopy.IsMatch(name)))
                    {
                        whole = false;
                        break;
                    }
                }

                if (whole)
                {
                    doomed.UnionWith(names);
                }
                else
                {
                    doomed.Add(file);
                }
            }

            foreach (string file in doomed)
            {
                Remove(root + file);
            }

            // Deepest first, so a folder's folders are gone before it is tried. One that still holds a kept file stays.
            folders.Sort(delegate (string a, string b) { return b.Length.CompareTo(a.Length); });
            int folderCount = 0;
            foreach (string folder in folders)
            {
                if (Taken(folder, removed, kept) && Remove(root + folder))
                {
                    folderCount++;
                }
            }

            return new int[] { doomed.Count, folderCount };
        }
        finally
        {
            Restore(previous);
        }
    }

    private static Regex Pattern(List<string> patterns)
    {
        if (patterns.Count == 0)
        {
            return null;
        }

        return new Regex(@"^(" + string.Join("|", patterns.ToArray()) + @")(\\.*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool Taken(string path, Regex removed, Regex kept)
    {
        return removed != null && removed.IsMatch(path) && (kept == null || !kept.IsMatch(path));
    }

    // Every file and folder below the root, as paths in the image. A junction or other reparse point is listed and
    // never followed.
    private static void List(string root, string folder, List<string> files, List<string> folders)
    {
        FindData data;
        // FindExInfoBasic, FIND_FIRST_EX_LARGE_FETCH.
        IntPtr find = FindFirstFileExW(@"\\?\" + root + folder + @"\*", 1, out data, 0, IntPtr.Zero, 2);
        if (find == InvalidHandle)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot list " + root + folder);
        }

        try
        {
            do
            {
                if (data.Name == "." || data.Name == "..")
                {
                    continue;
                }

                string path = folder + "\\" + data.Name;
                if ((data.Attributes & DirectoryAttribute) == 0)
                {
                    files.Add(path);
                }
                else
                {
                    folders.Add(path);
                    if ((data.Attributes & ReparsePointAttribute) == 0)
                    {
                        List(root, path, files, folders);
                    }
                }
            }
            while (FindNextFileW(find, out data));

            int error = Marshal.GetLastWin32Error();
            if (error != ErrorNoMoreFiles)
            {
                throw new Win32Exception(error, "Cannot list " + root + folder);
            }
        }
        finally
        {
            FindClose(find);
        }
    }

    // Every name of the file, as paths in the image; null for a name outside the mount, which keeps the file.
    private static List<string> Names(string root, string volumeRelative, string file)
    {
        List<string> names = new List<string>();
        StringBuilder name = new StringBuilder(1024);
        uint length = (uint)name.Capacity;
        IntPtr find = FindFirstFileNameW(@"\\?\" + root + file, 0, ref length, name);
        if (find == InvalidHandle && Marshal.GetLastWin32Error() == ErrorMoreData)
        {
            name = new StringBuilder((int)length);
            find = FindFirstFileNameW(@"\\?\" + root + file, 0, ref length, name);
        }

        if (find == InvalidHandle)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot list the names of " + root + file);
        }

        try
        {
            while (true)
            {
                string found = name.ToString();
                names.Add(found.StartsWith(volumeRelative + "\\", StringComparison.OrdinalIgnoreCase) ? found.Substring(volumeRelative.Length) : null);

                length = (uint)name.Capacity;
                if (FindNextFileNameW(find, ref length, name))
                {
                    continue;
                }

                int error = Marshal.GetLastWin32Error();
                if (error == ErrorMoreData)
                {
                    name = new StringBuilder((int)length);
                    if (FindNextFileNameW(find, ref length, name))
                    {
                        continue;
                    }

                    error = Marshal.GetLastWin32Error();
                }

                if (error != ErrorHandleEof)
                {
                    throw new Win32Exception(error, "Cannot list the names of " + root + file);
                }

                return names;
            }
        }
        finally
        {
            FindClose(find);
        }
    }

    // Deletes one name of a file, or an empty folder; false for a folder that is not empty.
    private static bool Remove(string path)
    {
        using (SafeFileHandle handle = CreateFileW(@"\\?\" + path, Delete, ShareAll, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero))
        {
            if (handle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open " + path + " to delete it");
            }

            uint flags = DispositionFlags;
            if (SetFileInformationByHandle(handle, FileDispositionInfoEx, ref flags, 4))
            {
                return true;
            }

            int error = Marshal.GetLastWin32Error();
            if (error == ErrorDirNotEmpty)
            {
                return false;
            }

            throw new Win32Exception(error, "Cannot delete " + path);
        }
    }

    private static long[] Enable(params string[] names)
    {
        long[] previous = new long[names.Length * 2];
        IntPtr token;
        // TOKEN_ADJUST_PRIVILEGES, TOKEN_QUERY.
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            for (int index = 0; index < names.Length; index++)
            {
                TokenPrivilege state = new TokenPrivilege();
                state.Count = 1;
                state.Attributes = 0x2;
                if (!LookupPrivilegeValue(null, names[index], out state.Luid))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                TokenPrivilege old;
                uint returned;
                if (!AdjustTokenPrivileges(token, false, ref state, 16, out old, out returned) || Marshal.GetLastWin32Error() == ErrorNotAllAssigned)
                {
                    throw new InvalidOperationException("This account does not hold " + names[index] + ", which removing files from Windows PE needs.");
                }

                // What to put back: Windows reports the state before only for a privilege it changed, so none means
                // it was enabled already.
                previous[index * 2] = state.Luid;
                previous[index * 2 + 1] = old.Count == 0 ? 0x2 : old.Attributes;
            }
        }
        finally
        {
            CloseHandle(token);
        }

        return previous;
    }

    private static void Restore(long[] previous)
    {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out token))
        {
            return;
        }

        try
        {
            for (int index = 0; index < previous.Length; index += 2)
            {
                TokenPrivilege state = new TokenPrivilege();
                state.Count = 1;
                state.Luid = previous[index];
                state.Attributes = (uint)previous[index + 1];
                TokenPrivilege old;
                uint returned;
                AdjustTokenPrivileges(token, false, ref state, 16, out old, out returned);
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
'@
    }

    $lines = [IO.File]::ReadAllLines($ListPath)
    return [DdtBootImageTrim]::Trim($MountDirectory, $lines)
}

function Get-AdkVersion {
    # The version the ADK's installers register, by which Microsoft names its releases, such as 10.1.26100.2454. The
    # Windows PE add-on's first, since boot.wim comes from it. Properties are checked before they are read, which
    # strict mode requires of uninstall entries that lack them.
    $entries = @(Get-ItemProperty -Path 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
                                        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
        Where-Object { $_.PSObject.Properties['DisplayName'] -and $_.PSObject.Properties['DisplayVersion'] -and
                       $_.DisplayName -like 'Windows Assessment and Deployment Kit*' })

    $addOn = @($entries | Where-Object { $_.DisplayName -like '*Preinstallation Environment*' })
    $entry = @($addOn + $entries) | Select-Object -First 1

    if ($entry) { return [string] $entry.DisplayVersion }

    return $null
}

function Get-FileVersion {
    param([Parameter(Mandatory)][string] $Path)

    $version = (Get-Item -LiteralPath $Path).VersionInfo.ProductVersion

    if ([string]::IsNullOrWhiteSpace($version)) { return $null }

    return $version.Trim()
}

function Connect-DdtServer {
    param(
        [Parameter(Mandatory)][byte[]] $RootCertificate,
        [Parameter(Mandatory)][string] $Token
    )

    # Compiled rather than a script block, because the TLS handshake calls the check on a thread that has no
    # PowerShell runspace. C# 5 and the .NET Framework's X509Chain, so Windows PowerShell 5.1 compiles and runs it too.
    # The server's certificate must name the host and chain to the pinned root, and to no other.
    if (-not ('DdtBootImageServer' -as [type])) {
        Add-Type -AssemblyName System.Net.Http
        $compile = @{
            TypeDefinition = @'
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

public static class DdtBootImageServer
{
    public static HttpClient Connect(byte[] rootCertificate, string token)
    {
        X509Certificate2 root = new X509Certificate2(rootCertificate);
        HttpClientHandler handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = delegate (HttpRequestMessage request, X509Certificate2 certificate, X509Chain presented, SslPolicyErrors errors)
        {
            return IsPinned(root, certificate, presented, errors);
        };

        HttpClient client = new HttpClient(handler);
        client.Timeout = TimeSpan.FromMinutes(30);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    public static string GetString(HttpClient client, string url)
    {
        using (HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult())
        {
            EnsureSuccess(response, url);

            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
    }

    public static void Download(HttpClient client, string url, string path)
    {
        using (HttpResponseMessage response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
        {
            EnsureSuccess(response, url);

            using (Stream source = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
            using (FileStream target = File.Create(path))
            {
                source.CopyTo(target);
            }
        }
    }

    // The chain the server presented only lends its intermediates. The chain is built again with the pinned root as
    // the one root there is, and its top must be that root.
    private static bool IsPinned(X509Certificate2 root, X509Certificate2 certificate, X509Chain presented, SslPolicyErrors errors)
    {
        if (certificate == null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
        {
            return false;
        }

        using (X509Chain chain = new X509Chain())
        {
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.ExtraStore.Add(root);

            if (presented != null)
            {
                foreach (X509ChainElement element in presented.ChainElements)
                {
                    chain.ChainPolicy.ExtraStore.Add(element.Certificate);
                }
            }

            if (!chain.Build(certificate))
            {
                return false;
            }

            X509Certificate2 top = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;

            return Convert.ToBase64String(top.RawData) == Convert.ToBase64String(root.RawData);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, string url)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int status = (int)response.StatusCode;
        string reason = status == 401
            ? "DDT refused the API token: it is unknown, revoked or expired, or its user is disabled or locked out."
            : status == 403
                ? "The API token may not download boot image drivers. That takes an administrator's token."
                : "DDT answered " + status + " " + response.ReasonPhrase + ".";

        throw new InvalidOperationException(url + ": " + reason);
    }
}
'@
        }

        # PowerShell 7 compiles against the whole framework already; Windows PowerShell needs to be told.
        if ($PSVersionTable.PSEdition -eq 'Desktop') {
            $compile.ReferencedAssemblies = @([Net.Http.HttpClient].Assembly.Location)
        }

        Add-Type @compile
    }

    # Windows PowerShell leaves TLS 1.2 off for scripts unless asked, and DDT offers nothing older.
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

    return [DdtBootImageServer]::Connect($RootCertificate, $Token)
}

function Get-ServerDrivers {
    param(
        [Parameter(Mandatory)][byte[]] $RootCertificate,
        [Parameter(Mandatory)][string] $Token,
        [Parameter(Mandatory)][string] $Folder
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $base = $ServerUrl.TrimEnd('/')
    $client = Connect-DdtServer -RootCertificate $RootCertificate -Token $Token

    try {
        $view = [DdtBootImageServer]::GetString($client, "$base/api/boot-image") | ConvertFrom-Json
        $drivers = @($view.drivers)

        foreach ($driver in $drivers) {
            Write-Host "Downloading $($driver.name)"
            $zip = Join-Path $Folder "$($driver.packageId).zip"
            [DdtBootImageServer]::Download($client, "$base/api/boot-image/drivers/$($driver.packageId)/content", $zip)

            # The hash DDT listed, not one the download came with, says what the package is.
            $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
            if ($hash -ne $driver.sha256) {
                throw "The download of $($driver.name) has SHA-256 $hash, but DDT lists $($driver.sha256). Build again."
            }

            # ExtractToDirectory refuses an entry that would land outside the folder, which DDT refused at upload already.
            [IO.Compression.ZipFile]::ExtractToDirectory($zip, (Join-Path $Folder ([string] $driver.packageId)))
            Remove-Item -LiteralPath $zip
        }

        return [pscustomobject]@{
            DriverSetHash = $view.driverSetHash
            Drivers       = $drivers
        }
    }
    finally {
        $client.Dispose()
    }
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
}

if ($ApiToken) {
    if (-not $ServerUrl -or -not $RootCertificatePath) {
        throw 'Downloading the drivers flagged for the boot image needs -ServerUrl and -RootCertificatePath to reach the server.'
    }

    if ($ApiToken -cnotmatch '^ddt_[0-9A-Za-z]{43}$') {
        throw '-ApiToken takes a DDT API token, ddt_ and 43 letters and digits.'
    }
}

# The agent pins the root in boot.wim, and the download of the drivers trusts it alone.
$rootCertificate = $null
$rootCertificateBytes = $null

if ($RootCertificatePath -and ($AgentPath -or $ApiToken)) {
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
    $rootCertificateBytes = [Convert]::FromBase64String($base64)
    $pinned = [Security.Cryptography.X509Certificates.X509Certificate2]::new($rootCertificateBytes)
    $constraints = $pinned.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension] }
    if (-not $constraints -or -not $constraints.CertificateAuthority) {
        Write-Warning ("$RootCertificatePath is not a CA certificate: $($pinned.Subject). The boot image stops reaching " +
            'DDT when that certificate is replaced. For DDT''s own certificate pass ddt-root.pem instead.')
    }
}

if ($DriverPath) {
    if (-not (Test-Path -LiteralPath $DriverPath -PathType Container)) {
        throw "Driver folder not found at $DriverPath."
    }

    $DriverPath = (Resolve-Path -LiteralPath $DriverPath).ProviderPath

    if (-not (Get-ChildItem -LiteralPath $DriverPath -Recurse -File -Filter '*.inf' | Select-Object -First 1)) {
        throw "$DriverPath holds no .inf file, so it has no driver DISM could add."
    }
}

# What Publish-Console.ps1 writes: the console and the native libraries it draws with, which go together.
$consoleFiles = @('ddt-console.exe', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll')

if ($ConsolePath) {
    if (-not $AgentPath) {
        throw 'The console needs -AgentPath: the agent starts it.'
    }

    if (-not (Test-Path -LiteralPath $ConsolePath -PathType Container)) {
        throw "Console folder not found at $ConsolePath. Pass the folder Publish-Console.ps1 wrote."
    }

    $ConsolePath = (Resolve-Path -LiteralPath $ConsolePath).ProviderPath

    foreach ($file in $consoleFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $ConsolePath $file) -PathType Leaf)) {
            throw "$ConsolePath has no $file. Publish the console again with Publish-Console.ps1."
        }
    }
}

if ($ExtraPath) {
    if (-not (Test-Path -LiteralPath $ExtraPath -PathType Container)) {
        throw "Folder not found at $ExtraPath."
    }

    $ExtraPath = (Resolve-Path -LiteralPath $ExtraPath).ProviderPath
}

if (-not $SkipTrim) {
    if (-not (Test-Path -LiteralPath $TrimListPath -PathType Leaf)) {
        throw "Trim list not found at $TrimListPath."
    }

    $TrimListPath = (Resolve-Path -LiteralPath $TrimListPath).ProviderPath
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

# Downloaded before anything is built too, so a refused token or an unreachable server costs no copype run.
$serverDrivers = $null

if (Test-Path -LiteralPath $DriverDownloads) {
    Remove-Item -LiteralPath $DriverDownloads -Recurse -Force
}

if ($ApiToken) {
    New-Item -ItemType Directory -Force -Path $DriverDownloads | Out-Null
    $serverDrivers = Get-ServerDrivers -RootCertificate $rootCertificateBytes -Token $ApiToken -Folder $DriverDownloads
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
    foreach ($package in $packages) {
        Write-Host "Adding $(Split-Path -Leaf $package)"
        Invoke-Native $dism "/Image:$Mount" /Add-Package "/PackagePath:$package" | Out-Null
    }

    # Without /ForceUnsigned: Windows PE could not load an unsigned driver with Secure Boot on, so DISM refusing one
    # here is the earlier and the clearer failure.
    if ($serverDrivers) {
        foreach ($driver in $serverDrivers.Drivers) {
            Write-Host "Adding the drivers of $($driver.name)"
            Invoke-Native $dism "/Image:$Mount" /Add-Driver "/Driver:$(Join-Path $DriverDownloads ([string] $driver.packageId))" /Recurse | Out-Null
        }
    }

    if ($DriverPath) {
        Write-Host "Adding the drivers in $DriverPath"
        Invoke-Native $dism "/Image:$Mount" /Add-Driver "/Driver:$DriverPath" /Recurse | Out-Null
    }

    if ($AgentPath) {
        New-Item -ItemType Directory -Force -Path (Join-Path $Mount 'DDT') | Out-Null
        Copy-Item -LiteralPath $AgentPath -Destination (Join-Path $Mount 'DDT\ddt-agent.exe')

        # The agent uses a libwim-15.dll it finds next to itself instead of writing out its own copy.
        if ($WimLibraryPath) {
            Copy-Item -LiteralPath $WimLibraryPath -Destination (Join-Path $Mount 'DDT\libwim-15.dll')
        }

        # The agent starts ddt-console.exe when it finds it next to itself.
        if ($ConsolePath) {
            foreach ($file in $consoleFiles) {
                Copy-Item -LiteralPath (Join-Path $ConsolePath $file) -Destination (Join-Path $Mount "DDT\$file")
            }
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

    if ($ExtraPath) {
        $extra = Join-Path $Mount 'Extra'
        New-Item -ItemType Directory -Force -Path $extra | Out-Null
        Get-ChildItem -LiteralPath $ExtraPath -Recurse -File | Where-Object Extension -ne '.pdb' | ForEach-Object {
            $target = Join-Path $extra $_.FullName.Substring($ExtraPath.Length).TrimStart('\')
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $target
        }
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

    # Last, because it takes the servicing stack that DISM used above. The export below then leaves out what no
    # name refers to any more. A list edited too far could take what Windows PE starts with, so these must stay.
    if (-not $SkipTrim) {
        $essential = @('Windows\System32\ntoskrnl.exe', 'Windows\System32\winload.efi', 'Windows\System32\ucrtbase.dll',
                       'Windows\SysWOW64\ntdll.dll') | Where-Object { Test-Path -LiteralPath (Join-Path $Mount $_) }

        $trimmed = Remove-TrimmedFiles -MountDirectory $Mount -ListPath $TrimListPath
        Write-Host "Removed $($trimmed[0]) files and $($trimmed[1]) folders by $TrimListPath"

        foreach ($file in $essential) {
            if (-not (Test-Path -LiteralPath (Join-Path $Mount $file))) {
                throw "$TrimListPath removes $file, which Windows PE needs to start."
            }
        }
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

# The description of this build, for DDT's boot image page, written after boot.wim so that a boot directory that has it
# has the image it describes. The time is a string, because Windows PowerShell writes a DateTime as \/Date()\/, and the
# file has no byte order mark, which Windows PowerShell's Set-Content -Encoding UTF8 would add.
$manifest = [ordered]@{
    builtUtc      = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
    driverSetHash = if ($serverDrivers) { $serverDrivers.DriverSetHash } else { $null }
    drivers       = @(if ($serverDrivers) {
        foreach ($driver in $serverDrivers.Drivers) {
            [ordered]@{ packageId = [string] $driver.packageId; name = [string] $driver.name; sha256 = [string] $driver.sha256 }
        }
    })
    adkVersion    = Get-AdkVersion
    bootManager   = Get-FileVersion -Path $bootManager2011
    agentVersion  = if ($AgentPath) { Get-FileVersion -Path $AgentPath } else { $null }
}

[IO.File]::WriteAllText(
    (Join-Path $Destination 'Boot\ddt-boot-image.json'),
    ($manifest | ConvertTo-Json -Depth 5),
    (New-Object Text.UTF8Encoding $false))

if (Test-Path -LiteralPath $DriverDownloads) {
    Remove-Item -LiteralPath $DriverDownloads -Recurse -Force
}

Get-ChildItem -LiteralPath $Destination -Recurse -File |
    Select-Object @{ Name = 'File'; Expression = { $_.FullName.Substring($Destination.Length + 1) } },
                  @{ Name = 'MB'; Expression = { [math]::Round($_.Length / 1MB, 1) } } |
    Format-Table -AutoSize

# A PXE netboot fetches boot.wim over TFTP, so its size is most of what the netboot takes.
$variant = if ($SkipPowerShell) { 'without PowerShell' } else { 'with PowerShell' }
$variant += if ($SkipTrim) { ', untrimmed' } else { ', trimmed' }
Write-Host ('boot.wim, {0}: {1:N1} MB' -f $variant, ((Get-Item -LiteralPath (Join-Path $Destination 'Boot\boot.wim')).Length / 1MB))
