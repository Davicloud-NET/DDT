# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Removes what the trim list names from the mounted image, and stops the build when that takes a file Windows PE
# starts with. It takes the servicing stack too, so nothing can be added to the image afterwards.
function Remove-TrimmedFile {
    param(
        [Parameter(Mandatory)][string] $MountDirectory,
        [Parameter(Mandatory)][string] $ListPath
    )

    # A list edited too far could take what Windows PE starts with, so these must stay.
    $essential = @('Windows\System32\ntoskrnl.exe', 'Windows\System32\winload.efi', 'Windows\System32\ucrtbase.dll',
                   'Windows\SysWOW64\ntdll.dll') | Where-Object { Test-Path -LiteralPath (Join-Path $MountDirectory $_) }

    # Compiled rather than a script, for the privileges, the hard links and the tens of thousands of files.
    if (-not ('DdtBootImageTrim' -as [type])) {
        Add-Type -Path (Join-Path $PSScriptRoot 'Trim.cs')
    }

    $lines = [IO.File]::ReadAllLines($ListPath)
    $trimmed = [DdtBootImageTrim]::Trim($MountDirectory, $lines)
    Write-Host "Removed $($trimmed[0]) files and $($trimmed[1]) folders by $ListPath"

    foreach ($file in $essential) {
        if (-not (Test-Path -LiteralPath (Join-Path $MountDirectory $file))) {
            throw "$ListPath removes $file, which Windows PE needs to start."
        }
    }
}
