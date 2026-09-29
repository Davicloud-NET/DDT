# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Writes a BCD store whose one entry boots \Boot\boot.wim from a RAM disk that the boot manager fetches over TFTP.
function New-BootBcd {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][int] $TftpBlockSize,
        [Parameter(Mandatory)][int] $TftpWindowSize
    )

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
