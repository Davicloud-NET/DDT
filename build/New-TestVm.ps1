# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1
#Requires -Modules Hyper-V
#Requires -RunAsAdministrator

<#
.SYNOPSIS
Creates or removes the Hyper-V Generation 2 machine used to test DDT netboot with Secure Boot on.

.DESCRIPTION
Builds a Generation 2 virtual machine with Secure Boot enabled, a virtual TPM, a 64 GB disk to deploy
Windows onto, and the network adapter first in the boot order. Safe to run repeatedly: existing state
is reconciled, not recreated, and a Windows Boot Manager entry a deployment added stays in the boot
order.

The disk is created in the host's virtual hard disk folder as <name>.vhdx and is reused when it already
exists. -Remove leaves it in place and prints where it is.

By default the machine joins the Hyper-V Default Switch, which already runs a DHCP server. That is
the shape DDT is built for: someone else hands out addresses and DDT answers only as ProxyDHCP.
Point DDT at it with DDT__Pxe__Interfaces="vEthernet (Default Switch)".

Pass -SwitchName with any other name to create an Internal switch instead, with a static host
address. An Internal switch has no DHCP server of its own, so the guest gets no address until you
provide one. Never use a Private switch: it gives the host no adapter on the segment, and DDT runs
on the host.

.EXAMPLE
.\build\New-TestVm.ps1 -Start -Watch

.EXAMPLE
.\build\New-TestVm.ps1 -SwitchName DDT-Test -HostIpAddress 10.13.37.1

.EXAMPLE
.\build\New-TestVm.ps1 -Remove
#>
[CmdletBinding(DefaultParameterSetName = 'Create')]
param(
    [ValidateNotNullOrEmpty()]
    [string] $Name = 'DDT-Test',

    [ValidateNotNullOrEmpty()]
    [string] $SwitchName = 'Default Switch',

    # Only used when -SwitchName names a switch this script creates.
    [string] $HostIpAddress = '10.13.37.1',

    [ValidateRange(8, 30)]
    [int] $PrefixLength = 24,

    # WinPE boots the whole boot.wim into a RAM disk, about 340 MB before the agent is added, and the
    # Windows 11 it deploys wants 4 GB.
    [ValidateRange(1GB, 32GB)]
    [long] $MemoryBytes = 4GB,

    # Windows 11 wants 64 GB. A deployment briefly holds the downloaded image and the applied one.
    [ValidateRange(32GB, 2TB)]
    [long] $DiskSizeBytes = 64GB,

    # Fixed and locally administered, so the DDT machine record survives a teardown and recreate.
    [ValidatePattern('^[0-9A-Fa-f]{12}$')]
    [string] $MacAddress = '02155D0D0D01',

    # The template whose db holds the Microsoft Windows Production PCA 2011, which signs the default
    # boot manager DDT serves.
    [ValidateNotNullOrEmpty()]
    [string] $SecureBootTemplate = 'MicrosoftWindows',

    [Parameter(ParameterSetName = 'Create')]
    [switch] $Start,

    [Parameter(ParameterSetName = 'Create')]
    [switch] $Watch,

    [Parameter(ParameterSetName = 'Remove', Mandatory)]
    [switch] $Remove
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$UsesDefaultSwitch = $SwitchName -eq 'Default Switch'
$HostAdapterName = "vEthernet ($SwitchName)"

function Initialize-Switch {
    $existing = Get-VMSwitch -Name $SwitchName -ErrorAction SilentlyContinue

    if ($UsesDefaultSwitch) {
        if (-not $existing) {
            throw "The Hyper-V Default Switch is missing on this host. Pass -SwitchName to create an Internal switch."
        }

        return
    }

    if (-not $existing) {
        $null = New-VMSwitch -Name $SwitchName -SwitchType Internal
        Write-Host "Created Internal switch '$SwitchName'."
    }
    elseif ($existing.SwitchType -ne 'Internal') {
        throw "Switch '$SwitchName' exists as $($existing.SwitchType). DDT on the host needs an Internal switch."
    }

    # The host adapter can take a moment to appear after the switch is created.
    $adapter = $null
    for ($attempt = 0; $attempt -lt 20 -and -not $adapter; $attempt++) {
        $adapter = Get-NetAdapter -Name $HostAdapterName -ErrorAction SilentlyContinue
        if (-not $adapter) { Start-Sleep -Milliseconds 500 }
    }

    if (-not $adapter) {
        throw "Host adapter '$HostAdapterName' did not appear."
    }

    $assigned = Get-NetIPAddress -InterfaceIndex $adapter.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -eq $HostIpAddress }

    if (-not $assigned) {
        # No default gateway, so the test segment never becomes a route for host traffic.
        $null = New-NetIPAddress -InterfaceIndex $adapter.ifIndex -IPAddress $HostIpAddress -PrefixLength $PrefixLength
        Write-Host "Assigned $HostIpAddress/$PrefixLength to '$HostAdapterName'."
    }
}

function Initialize-Vm {
    $template = (Get-VMHost).SecureBootTemplates | Where-Object { $_.Name -eq $SecureBootTemplate }

    # The template name is a plain string with no validation, and a wrong one only shows up as a boot
    # failure that looks exactly like a DDT bug.
    if (-not $template) {
        $available = ((Get-VMHost).SecureBootTemplates | ForEach-Object { $_.Name }) -join ', '
        throw "Secure Boot template '$SecureBootTemplate' is not registered. Available: $available"
    }

    $vm = Get-VM -Name $Name -ErrorAction SilentlyContinue

    if ($vm -and $vm.Generation -ne 2) {
        throw "Virtual machine '$Name' is generation $($vm.Generation). Remove it with -Remove first."
    }

    if (-not $vm) {
        $vm = New-VM -Name $Name -Generation 2 -MemoryStartupBytes $MemoryBytes -SwitchName $SwitchName -NoVHD -BootDevice NetworkAdapter
        Write-Host "Created generation 2 virtual machine '$Name'."
    }
    elseif ($vm.State -ne 'Off') {
        Stop-VM -VM $vm -TurnOff -Force
    }

    # Static memory: the RAM disk needs the whole image resident before any balloon driver runs.
    Set-VM -VM $vm -StaticMemory -MemoryStartupBytes $MemoryBytes `
        -AutomaticCheckpointsEnabled $false -CheckpointType Disabled `
        -AutomaticStartAction Nothing -AutomaticStopAction TurnOff
    Set-VMProcessor -VM $vm -Count 2

    $adapters = @(Get-VMNetworkAdapter -VM $vm)
    if ($adapters.Count -eq 0) {
        $adapters = @(Add-VMNetworkAdapter -VM $vm -SwitchName $SwitchName -Passthru)
    }

    foreach ($extra in $adapters | Select-Object -Skip 1) {
        Remove-VMNetworkAdapter -VMNetworkAdapter $extra
    }

    $adapter = $adapters[0]
    if ($adapter.SwitchName -ne $SwitchName) {
        Connect-VMNetworkAdapter -VMNetworkAdapter $adapter -SwitchName $SwitchName
    }

    Set-VMNetworkAdapter -VMNetworkAdapter $adapter -StaticMacAddress $MacAddress.ToUpperInvariant()

    if (-not (Get-VMHardDiskDrive -VM $vm)) {
        $disk = Join-Path (Get-VMHost).VirtualHardDiskPath "$Name.vhdx"

        if (-not (Test-Path -LiteralPath $disk)) {
            $null = New-VHD -Path $disk -SizeBytes $DiskSizeBytes -Dynamic
            Write-Host "Created virtual disk $disk."
        }

        Add-VMHardDiskDrive -VM $vm -Path $disk
    }

    $firmware = Get-VMFirmware -VM $vm
    $tpmEnabled = (Get-VMSecurity -VM $vm).TpmEnabled

    # Hyper-V refuses to change the template, even to the same value, once the virtual TPM is initialized.
    if ($firmware.SecureBootTemplateId -ne $template.Id) {
        if ($tpmEnabled) {
            throw "Virtual machine '$Name' has a virtual TPM, so its Secure Boot template cannot change. Remove it with -Remove first."
        }

        Set-VMFirmware -VM $vm -SecureBootTemplateId $template.Id
    }

    if ($firmware.SecureBoot -ne 'On') {
        Set-VMFirmware -VM $vm -EnableSecureBoot On
    }

    # PauseAfterBootFailure keeps the firmware error on screen instead of scrolling past it.
    Set-VMFirmware -VM $vm -PreferredNetworkBootProtocol IPv4 -PauseAfterBootFailure On

    # A local key protector is enough for a test machine. Windows 11 setup is not run, but Windows expects a
    # TPM once deployed. It comes after the template, which the TPM freezes.
    if (-not $tpmEnabled) {
        Set-VMKeyProtector -VM $vm -NewLocalKeyProtector
        Enable-VMTPM -VM $vm
    }

    # BootOrder replaces the whole list, so the adapter goes first and nothing else is dropped. File
    # entries are the Windows Boot Manager a deployment added; the list would lose them if rebuilt from
    # devices alone.
    $files = @((Get-VMFirmware -VM $vm).BootOrder | Where-Object { $_.BootType -eq 'File' })
    $drives = @(@(Get-VMHardDiskDrive -VM $vm) + @(Get-VMDvdDrive -VM $vm) | Where-Object { $_ })
    Set-VMFirmware -VM $vm -BootOrder (@($adapter) + $files + $drives)

    $first = (Get-VMFirmware -VM $vm).BootOrder | Select-Object -First 1
    if ($first.BootType -ne 'Network') {
        throw "The boot order did not take: the first entry is $($first.BootType)."
    }
}

function Remove-Vm {
    $vm = Get-VM -Name $Name -ErrorAction SilentlyContinue

    if (-not $vm) {
        Write-Host "No virtual machine named '$Name'."
        return
    }

    $folder = $vm.Path
    $disks = @(Get-VMHardDiskDrive -VM $vm | ForEach-Object { $_.Path })

    if ($vm.State -ne 'Off') {
        Stop-VM -VM $vm -TurnOff -Force
    }

    Remove-VM -VM $vm -Force
    Write-Host "Removed virtual machine '$Name'."

    foreach ($disk in $disks) {
        Write-Host "The virtual disk $disk was left in place. Delete it to start the next machine with an empty disk."
    }

    # Remove-VM leaves configuration files behind. Only a folder named after this machine and holding
    # no virtual disks is deleted, never a shared parent such as the Hyper-V default store.
    if ($folder -and (Split-Path -Path $folder -Leaf) -eq $Name -and (Test-Path -LiteralPath $folder)) {
        $inside = @(Get-ChildItem -LiteralPath $folder -Recurse -File -Include *.vhd, *.vhdx, *.avhdx -ErrorAction SilentlyContinue)
        if ($inside.Count -eq 0) {
            Remove-Item -LiteralPath $folder -Recurse -Force
        }
    }

    if (-not $UsesDefaultSwitch) {
        Write-Host "Switch '$SwitchName' was left in place. Remove it with Remove-VMSwitch when nothing else uses it."
    }
}

if ($Remove) {
    Remove-Vm
    return
}

Initialize-Switch
Initialize-Vm

Write-Host ''
Write-Host "Ready: $Name, MAC $($MacAddress.ToUpperInvariant()), switch '$SwitchName', Secure Boot template $SecureBootTemplate."
Write-Host "Serve it with DDT__Pxe__Interfaces=`"$HostAdapterName`"."

if (-not $UsesDefaultSwitch) {
    Write-Host "'$SwitchName' has no DHCP server. DDT hands out no addresses, so provide one before booting." -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'To capture the boot on the wire, filtered on the machine because TFTP data uses ephemeral ports:'
Write-Host "  pktmon filter add DDT -m $($MacAddress -replace '(..)(?!$)', '$1-')"
Write-Host '  pktmon start --capture --pkt-size 0 --file ddt-boot.etl'
Write-Host '  pktmon stop; pktmon etl2pcap ddt-boot.etl --out ddt-boot.pcapng'

if ($Start) {
    Start-VM -Name $Name
}

if ($Watch) {
    # Windows PowerShell joins ArgumentList with spaces and does not quote, so a name with a space splits.
    Start-Process -FilePath "$env:SystemRoot\System32\vmconnect.exe" -ArgumentList $env:COMPUTERNAME, "`"$Name`""
}
