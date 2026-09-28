# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

# Runs a program and throws, with its output, when it exits with anything but 0.
function Invoke-Native {
    param(
        [Parameter(Mandatory)][string] $FilePath,
        [Parameter(ValueFromRemainingArguments)][string[]] $Arguments
    )

    # Standard error is left on the console. Redirecting it in Windows PowerShell turns every line into
    # a terminating error under ErrorActionPreference Stop.
    $output = & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($Arguments -join ' ') failed with exit code $LASTEXITCODE.`n$($output -join "`n")"
    }

    return $output
}
