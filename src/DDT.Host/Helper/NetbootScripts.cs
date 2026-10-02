// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Host.Helper;

// What the helper runs in PowerShell for the DHCP server and WDS of this computer. Each script is fixed. What a request
// gives reaches it in environment variables, checked first, and never as a part of the script.
public static class NetbootScripts
{
    // The name WDS lists DDT's boot image under, and by which a new build finds the last one
    public const string ImageName = "DDT";

    // One line of JSON: each scope with what its options 66 and 67 say now.
    public const string DhcpScopes = """
        $ErrorActionPreference = 'Stop'
        Import-Module DhcpServer
        $scopes = @(Get-DhcpServerv4Scope | ForEach-Object {
            $options = @(Get-DhcpServerv4OptionValue -ScopeId $_.ScopeId -All -ErrorAction SilentlyContinue)
            $server = $options | Where-Object OptionId -eq 66 | Select-Object -First 1
            $file = $options | Where-Object OptionId -eq 67 | Select-Object -First 1
            [pscustomobject]@{
                scopeId    = "$($_.ScopeId)"
                name       = "$($_.Name)"
                active     = ("$($_.State)" -eq 'Active')
                bootServer = if ($server) { "$($server.Value[0])" } else { $null }
                bootFile   = if ($file) { "$($file.Value[0])" } else { $null }
            }
        })
        ConvertTo-Json -InputObject $scopes -Compress
        """;

    public const string DhcpOptions = """
        $ErrorActionPreference = 'Stop'
        Import-Module DhcpServer
        foreach ($scope in $env:DDT_SCOPES -split ',') {
            Set-DhcpServerv4OptionValue -ScopeId $scope -OptionId 66 -Value $env:DDT_BOOT_SERVER
            Set-DhcpServerv4OptionValue -ScopeId $scope -OptionId 67 -Value $env:DDT_BOOT_FILE
            Write-Output "Scope $scope sends machines to $env:DDT_BOOT_SERVER for $env:DDT_BOOT_FILE."
        }
        """;

    public const string WdsReplace = """
        $ErrorActionPreference = 'Stop'
        Stop-Service -Name WDSServer -Force
        Set-Service -Name WDSServer -StartupType Disabled
        Write-Output 'Windows Deployment Services is stopped and no longer starts with Windows.'
        """;

    public const string WdsRestore = """
        $ErrorActionPreference = 'Stop'
        Set-Service -Name WDSServer -StartupType Automatic
        Start-Service -Name WDSServer
        Write-Output 'Windows Deployment Services runs again and starts with Windows.'
        """;

    // DDT_ONLY_REPLACE is set after a build: WDS then gets the new image only where it had DDT's before.
    public const string WdsBootImage = """
        $ErrorActionPreference = 'Stop'
        if (-not (Get-Service -Name WDSServer -ErrorAction SilentlyContinue)) {
            if ($env:DDT_ONLY_REPLACE -eq '1') { exit 0 }
            throw 'Windows Deployment Services is not installed on this computer.'
        }
        Import-Module WDS
        $mine = Get-WdsBootImage -Architecture X64 -ImageName $env:DDT_IMAGE_NAME -ErrorAction SilentlyContinue
        if (-not $mine -and $env:DDT_ONLY_REPLACE -eq '1') { exit 0 }
        if ($mine) { Remove-WdsBootImage -Architecture X64 -ImageName $env:DDT_IMAGE_NAME }
        Import-WdsBootImage -Path $env:DDT_WIM -NewImageName $env:DDT_IMAGE_NAME -NewDescription 'DDT, the Davicloud Deployment Toolkit' -SkipVerify | Out-Null
        Write-Output "The boot menu of Windows Deployment Services offers $env:DDT_IMAGE_NAME now."
        """;
}
