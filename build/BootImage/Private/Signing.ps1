# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Checks that copype produced both boot managers and that each is signed by the CA its path promises.
function Assert-BootManager {
    param(
        [Parameter(Mandatory)][string] $Pca2011Path,
        [Parameter(Mandatory)][string] $Uefi2023Path
    )

    # Add-ons before 10.1.26100.2454 produce neither file.
    foreach ($file in $Pca2011Path, $Uefi2023Path) {
        if (-not (Test-Path -LiteralPath $file)) {
            throw "copype did not produce $file. Update the Windows PE add-on to 10.1.26100.2454 or later."
        }
    }

    Assert-BootManagerIssuer -Path $Pca2011Path -Expected 'Microsoft Windows Production PCA 2011'
    Assert-BootManagerIssuer -Path $Uefi2023Path -Expected 'Windows UEFI CA 2023'
}

function Assert-BootManagerIssuer {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Expected
    )

    $issuer = Get-BootManagerIssuer -Path $Path
    if (-not $issuer -or $issuer -notlike "*$Expected*") {
        throw "$Path is signed by '$issuer', expected '$Expected'. The ADK layout may have changed."
    }
}

# The issuer of the certificate that signed a boot manager, or $null for an unsigned file.
function Get-BootManagerIssuer {
    param([Parameter(Mandatory)][string] $Path)

    # Get-AuthenticodeSignature doesn't work here. It prefers the OS catalog and reports the 2011 PCA for
    # both files. So the embedded signature is read from the PE security directory instead.
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
