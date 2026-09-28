# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# PSScriptAnalyzer's settings for build/, which CI checks with
# Invoke-ScriptAnalyzer -Path build -Recurse -Settings build/PSScriptAnalyzerSettings.psd1
@{
    ExcludeRules = @(
        # The scripts report progress to the person running them. Write-Output would mix it into what they return.
        'PSAvoidUsingWriteHost'

        # The functions are steps of one run rather than commands of their own. A script that removes what someone
        # may want to keep, like New-TestVm.ps1 -Remove, takes -WhatIf itself.
        'PSUseShouldProcessForStateChangingFunctions'
    )

    Rules = @{
        # Everything here runs in Windows PowerShell 5.1, which the ADK and Hyper-V hosts have.
        PSUseCompatibleSyntax            = @{
            Enable         = $true
            TargetVersions = @('5.1')
        }

        # Invoke-Native passes a native command line on, which is positional by nature.
        PSAvoidUsingPositionalParameters = @{
            CommandAllowList = @('Invoke-Native')
        }
    }
}
