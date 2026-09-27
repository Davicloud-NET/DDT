# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Starts DDT from source with the web and pxe roles, reachable from the Hyper-V test machine.

.DESCRIPTION
Listens on every interface with a certificate DDT issues from its own root under certs in the store,
naming this computer's Default Switch DNS name, <computer>.mshome.net. That name moves with the switch
when Windows gives it a new address after a restart, so a boot image built with it keeps working. The
boot image pins the root, so it also keeps working when DDT renews the certificate or adds a name.
Deleting the certs folder makes a new root, and every boot image then has to be built again.

Serves artifacts\agent\ddt-agent.exe, which Publish-Agent.ps1 writes, as the agent every netbooting
machine switches to, and artifacts\ddt-console.zip, which Publish-Console.ps1 writes, as the console it
shows, so a published change reaches the test machine at its next boot without a new boot image.

Prints the -ServerUrl and -RootCertificatePath to build the boot image with.

.PARAMETER Interface
The interface DDT answers PXE on.

.PARAMETER Port
The HTTPS port for the web UI and the agents.

.PARAMETER StorePath
DDT:StorePath. The default is the host's own default, /var/lib/ddt on the repository's drive.

.EXAMPLE
.\build\Start-DevHost.ps1
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Interface = 'vEthernet (Default Switch)',

    [ValidateRange(1, 65535)]
    [int] $Port = 7152,

    [string] $StorePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Resolved here rather than in param(): Windows PowerShell leaves $PSScriptRoot empty there when the
# script is started with powershell -File.
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).ProviderPath
if (-not $StorePath) { $StorePath = Join-Path (Split-Path -Qualifier $repository) 'var\lib\ddt' }

$certificate = Join-Path $StorePath 'certs\ddt.pem'
$root = Join-Path $StorePath 'certs\ddt-root.pem'
$name = "$([Net.Dns]::GetHostName()).mshome.net"

$arguments = @(
    'run'
    '--project', (Join-Path $repository 'src\DDT.Host')
    '--launch-profile', 'https'
    '--'
    "--DDT:StorePath=$StorePath"
    '--DDT:Roles=web,pxe'
    "--DDT:Pxe:Interfaces=$Interface"
    "--DDT:Pxe:BootDirectory=$(Join-Path $repository 'artifacts\boot')"
    '--DDT:Pxe:BootTargets:X64Uefi:Method=Tftp'
    '--DDT:Pxe:BootTargets:X64Uefi:BootFile=x64/bootmgfw.efi'
    "--Kestrel:Endpoints:Https:Url=https://0.0.0.0:$Port"
    "--Kestrel:Certificates:Default:Path=$certificate"
    "--Kestrel:Certificates:Default:KeyPath=$(Join-Path $StorePath 'certs\ddt-key.pem')"
    "--DDT:Https:SubjectAlternativeNames=$name"
    "--DDT:Agent:BinaryPath=$(Join-Path $repository 'artifacts\agent\ddt-agent.exe')"
    "--DDT:Agent:ConsolePath=$(Join-Path $repository 'artifacts\ddt-console.zip')"
)

Write-Host "Build the boot image with -ServerUrl https://${name}:$Port -RootCertificatePath $root"

if ($PSCmdlet.ShouldProcess('DDT.Host', "dotnet $($arguments -join ' ')")) {
    & dotnet @arguments
}
