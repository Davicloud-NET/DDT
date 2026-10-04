# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# What a builder from DDT's Boot image page does besides building: it asks the server whether its token still
# uploads, and sends the server what it built. The server is trusted by the root the builder came with.

# Stops with the server's own words when the token no longer uploads. Asked before a build that takes minutes.
function Test-DdtBuilderToken {
    [CmdletBinding()]
    [OutputType([void])]
    param(
        [Parameter(Mandatory)][string] $ServerUrl,
        [Parameter(Mandatory)][string] $RootCertificatePath,
        [Parameter(Mandatory)][string] $Token
    )

    $root = Read-RootCertificate -Path $RootCertificatePath
    $client = Connect-DdtServer -RootCertificate $root.Bytes

    try {
        $answer = [DdtBootImageServer]::Send($client, 'GET', "$($ServerUrl.TrimEnd('/'))/api/boot-image/builder", $Token, $null)
        if ($answer) { throw (Format-ServerRefusal -Answer $answer) }
    }
    finally {
        $client.Dispose()
    }
}

# Zips the Boot, EFI and x64 folders of a build and sends them to the server, which checks and then serves them.
function Send-DdtBootImage {
    [CmdletBinding()]
    [OutputType([void])]
    param(
        [Parameter(Mandatory)][string] $ServerUrl,
        [Parameter(Mandatory)][string] $RootCertificatePath,
        [Parameter(Mandatory)][string] $Token,
        [Parameter(Mandatory)][string] $Folder
    )

    $Folder = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Folder).TrimEnd('\')
    $zip = "$Folder.zip"
    $root = Read-RootCertificate -Path $RootCertificatePath

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }

    # boot.wim is compressed already
    [IO.Compression.ZipFile]::CreateFromDirectory($Folder, $zip, [IO.Compression.CompressionLevel]::NoCompression, $false)
    Write-Host ('Uploading the boot image to {0}, {1:N1} MB' -f $ServerUrl, ((Get-Item -LiteralPath $zip).Length / 1MB))

    $client = Connect-DdtServer -RootCertificate $root.Bytes

    try {
        $answer = [DdtBootImageServer]::Send($client, 'PUT', "$($ServerUrl.TrimEnd('/'))/api/boot-image", $Token, $zip)
        if ($answer) { throw (Format-ServerRefusal -Answer $answer) }
    }
    finally {
        $client.Dispose()
        Remove-Item -LiteralPath $zip -Force
    }

    Write-Host 'The server serves this boot image from now on.'
}

# The server answers a refusal with a problem document, whose title says why.
function Format-ServerRefusal {
    param([Parameter(Mandatory)][string] $Answer)

    $status, $body = $Answer -split ' ', 2

    try { $title = ($body | ConvertFrom-Json).title } catch { $title = $null }

    if ($title) { return "DDT refused: $title" }

    return "DDT answered $status."
}
