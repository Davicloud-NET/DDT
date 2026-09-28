# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Checks the build's inputs before anything is built and resolves their paths. Agent is $null without -AgentPath, and
# TrimListPath is $null with -SkipTrim.
function Resolve-BuildInput {
    param(
        [string] $AgentPath,
        [string] $ServerUrl,
        [string] $RootCertificatePath,
        [string] $KeyboardLayout,
        [string] $ApiToken,
        [string] $DriverPath,
        [string] $ConsolePath,
        [string] $ExtraPath,
        [string] $TrimListPath,
        [switch] $SkipTrim,
        [string] $WimLibraryPath
    )

    $keyboard = $null
    if ($AgentPath) {
        $null = Resolve-InputPath -Path $AgentPath -Name 'Agent'
        if (-not $ServerUrl -or -not $RootCertificatePath) {
            throw 'An agent needs -ServerUrl and -RootCertificatePath to reach the server.'
        }

        $keyboard = Resolve-KeyboardLayout -KeyboardLayout $KeyboardLayout
    }

    if ($ApiToken) { Assert-ApiToken -ApiToken $ApiToken -ServerUrl $ServerUrl -RootCertificatePath $RootCertificatePath }

    # The agent pins the root in boot.wim, and the download of the drivers trusts it alone.
    $root = $null
    if ($RootCertificatePath -and ($AgentPath -or $ApiToken)) { $root = Read-RootCertificate -Path $RootCertificatePath }

    if ($DriverPath) { $DriverPath = Resolve-DriverFolder -Path $DriverPath }

    $consoleFiles = @()
    if ($ConsolePath) {
        if (-not $AgentPath) { throw 'The console needs -AgentPath: the agent starts it.' }
        $consoleFiles = @(Resolve-ConsoleFile -Path $ConsolePath)
    }

    if ($ExtraPath) { $ExtraPath = Resolve-InputPath -Path $ExtraPath -Name 'Folder' -PathType Container }
    $trimList = if ($SkipTrim) { $null } else { Resolve-InputPath -Path $TrimListPath -Name 'Trim list' -PathType Leaf }

    if ($WimLibraryPath) {
        if (-not $AgentPath) { throw 'A libwim needs -AgentPath: only the agent uses it.' }
        $null = Resolve-InputPath -Path $WimLibraryPath -Name 'libwim' -PathType Leaf
    }

    $agent = $null
    if ($AgentPath) {
        $agent = [pscustomobject]@{
            Path               = $AgentPath
            WimLibraryPath     = $WimLibraryPath
            ConsoleFiles       = $consoleFiles
            ServerUrl          = $ServerUrl
            RootCertificate    = $root.Pem
            KeyboardLayout     = $keyboard.Id
            KeyboardLayoutName = $keyboard.Name
        }
    }

    return [pscustomobject]@{
        Agent                = $agent
        RootCertificateBytes = if ($root) { $root.Bytes } else { $null }
        DriverPath           = $DriverPath
        ExtraPath            = $ExtraPath
        TrimListPath         = $trimList
    }
}

# The provider path of an input, or an error saying that it is missing.
function Resolve-InputPath {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Name,
        [ValidateSet('Any', 'Container', 'Leaf')][string] $PathType = 'Any'
    )

    if (-not (Test-Path -LiteralPath $Path -PathType $PathType)) {
        throw "$Name not found at $Path."
    }

    return (Resolve-Path -LiteralPath $Path).ProviderPath
}

# The keyboard layout for boot.wim, by default this computer's first, and the name the agent shows for it.
function Resolve-KeyboardLayout {
    param([string] $KeyboardLayout)

    if (-not $KeyboardLayout) {
        $KeyboardLayout = Get-WinUserLanguageList |
            ForEach-Object { $_.InputMethodTips } |
            Where-Object { $_ -match '^[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8}$' } |
            Select-Object -First 1
    }

    $name = $null

    if ($KeyboardLayout) {
        if ($KeyboardLayout -notmatch '^[0-9A-Fa-f]{4}:[0-9A-Fa-f]{8}$') {
            throw "-KeyboardLayout takes an identifier such as 0407:00000407, not $KeyboardLayout."
        }

        # The agent shows this name at the sign in prompt, where a wrong layout otherwise looks like a wrong password.
        $layoutKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Keyboard Layouts\$($KeyboardLayout.Split(':')[1])"
        $name = if (Test-Path -LiteralPath $layoutKey) { (Get-ItemProperty -LiteralPath $layoutKey).'Layout Text' } else { $KeyboardLayout }
    }

    return [pscustomobject]@{ Id = $KeyboardLayout; Name = $name }
}

function Assert-ApiToken {
    param(
        [Parameter(Mandatory)][string] $ApiToken,
        [string] $ServerUrl,
        [string] $RootCertificatePath
    )

    if (-not $ServerUrl -or -not $RootCertificatePath) {
        throw 'Downloading the drivers flagged for the boot image needs -ServerUrl and -RootCertificatePath to reach the server.'
    }

    if ($ApiToken -cnotmatch '^ddt_[0-9A-Za-z]{43}$') {
        throw '-ApiToken takes a DDT API token, ddt_ and 43 letters and digits.'
    }
}

# The PEM text and the DER bytes of the root the agent pins, after checking that it is one.
function Read-RootCertificate {
    param([Parameter(Mandatory)][string] $Path)

    # Read as a plain string: Get-Content attaches properties that ConvertTo-Json writes out as an object.
    $pem = [IO.File]::ReadAllText((Resolve-InputPath -Path $Path -Name 'Root certificate'))

    if ($pem -notmatch '-----BEGIN CERTIFICATE-----') {
        throw "$Path is not a PEM certificate."
    }

    # Anyone who can netboot can read boot.wim.
    if ($pem -match 'PRIVATE KEY') {
        throw "$Path contains a private key. Export the certificate alone."
    }

    # A server certificate pinned in place of its root stops working at its next renewal, weeks later.
    $base64 = ($pem -split '-----BEGIN CERTIFICATE-----')[1]
    $base64 = ($base64 -split '-----END CERTIFICATE-----')[0] -replace '\s', ''
    $bytes = [Convert]::FromBase64String($base64)
    $pinned = [Security.Cryptography.X509Certificates.X509Certificate2]::new($bytes)
    $constraints = $pinned.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension] }
    if (-not $constraints -or -not $constraints.CertificateAuthority) {
        Write-Warning ("$Path is not a CA certificate: $($pinned.Subject). The boot image stops reaching " +
            'DDT when that certificate is replaced. For DDT''s own certificate pass ddt-root.pem instead.')
    }

    return [pscustomobject]@{ Pem = $pem; Bytes = $bytes }
}

function Resolve-DriverFolder {
    param([Parameter(Mandatory)][string] $Path)

    $folder = Resolve-InputPath -Path $Path -Name 'Driver folder' -PathType Container

    if (-not (Get-ChildItem -LiteralPath $folder -Recurse -File -Filter '*.inf' | Select-Object -First 1)) {
        throw "$folder holds no .inf file, so it has no driver DISM could add."
    }

    return $folder
}

# The files Publish-Console.ps1 writes: the console and the native libraries it draws with, which go together.
function Resolve-ConsoleFile {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Console folder not found at $Path. Pass the folder Publish-Console.ps1 wrote."
    }

    $folder = (Resolve-Path -LiteralPath $Path).ProviderPath

    foreach ($file in 'ddt-console.exe', 'libSkiaSharp.dll', 'libHarfBuzzSharp.dll') {
        $filePath = Join-Path $folder $file
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            throw "$folder has no $file. Publish the console again with Publish-Console.ps1."
        }

        $filePath
    }
}
