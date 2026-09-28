# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Downloads the driver packages DDT flags for the boot image into Folder, one subfolder per package, and returns what
# DDT listed, with the folder.
function Save-ServerDriver {
    param(
        [Parameter(Mandatory)][string] $ServerUrl,
        [Parameter(Mandatory)][byte[]] $RootCertificate,
        [Parameter(Mandatory)][string] $Token,
        [Parameter(Mandatory)][string] $Folder
    )

    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
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
            Folder        = $Folder
        }
    }
    finally {
        $client.Dispose()
    }
}

# An HttpClient that sends the API token and trusts the pinned root alone, as the agent does.
function Connect-DdtServer {
    param(
        [Parameter(Mandatory)][byte[]] $RootCertificate,
        [Parameter(Mandatory)][string] $Token
    )

    # Compiled rather than a script block, because the TLS handshake calls the check on a thread that has no
    # PowerShell runspace.
    if (-not ('DdtBootImageServer' -as [type])) {
        Add-Type -AssemblyName System.Net.Http
        $compile = @{ Path = Join-Path $PSScriptRoot 'Server.cs' }

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
