# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Checks that files carry a valid Authenticode signature with a timestamp, and says who signed them.

.DESCRIPTION
The release workflow runs it on what comes back from signing, before those files go into the MSI or into a release.
It stops at the first file that is unsigned, changed since it was signed, signed by a certificate Windows does not
trust, or signed without a timestamp. A signature without one ends when its certificate does.

.EXAMPLE
.\build\Test-Signature.ps1 artifacts\installer\DDT.msi
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string[]] $Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($file in $Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $file

    if ($signature.Status -ne 'Valid') {
        throw "$file has no valid signature: $($signature.Status)."
    }

    if (-not $signature.TimeStamperCertificate) {
        throw "$file is signed without a timestamp."
    }

    Write-Host ('{0}: signed by {1}' -f (Split-Path -Leaf $file), $signature.SignerCertificate.Subject)
}
