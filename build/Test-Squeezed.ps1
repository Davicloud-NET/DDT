# Copyright (C) 2026 Davicloud
# SPDX-License-Identifier: GPL-3.0-or-later
# Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

#Requires -Version 5.1

<#
.SYNOPSIS
Runs the .NET tests the way a busy CI runner does, to find the tests that only fail there.

.DESCRIPTION
A test that gives something a fixed time to happen, or that races a task in the background, passes on a developer's
computer and fails now and then in CI. Running the tests again and again does not find it, and neither does one CPU
core alone, because xUnit then runs one test at a time. What finds it is many tests at once on few cores: this runs
each test project's executable that way, several times, and lists the tests that failed and how often.

Build first, with dotnet build DDT.slnx. The end to end tests are left out. The output of a round with failures stays
in artifacts\squeezed.

.PARAMETER Project
The test projects, by name, as DDT.Agent.Tests. Without it, every test project but the end to end tests.

.PARAMETER Rounds
How often each project runs.

.PARAMETER Cores
The CPU cores the tests get. One is the harshest.

.PARAMETER AtOnce
How many tests run at once, however few cores there are.

.EXAMPLE
.\build\Test-Squeezed.ps1 -Project DDT.Agent.Tests -Rounds 10
#>
[CmdletBinding()]
param(
    [string[]] $Project,

    [ValidateRange(1, 100)]
    [int] $Rounds = 5,

    [ValidateRange(1, 16)]
    [int] $Cores = 2,

    [ValidateRange(1, 256)]
    [int] $AtOnce = 32
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$logs = Join-Path $root 'artifacts\squeezed'
New-Item -ItemType Directory -Force -Path $logs | Out-Null

if (-not $Project) {
    $Project = Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Directory |
        Where-Object { $_.Name -like '*.Tests' } |
        ForEach-Object { $_.Name }
}

# The last cores, so the first one stays free for the rest of the computer
$all = [Environment]::ProcessorCount
$Cores = [Math]::Min($Cores, $all)
$mask = [IntPtr] ((([long] 1 -shl $Cores) - 1) -shl ($all - $Cores))
$suffix = if ($env:OS -eq 'Windows_NT') { '.exe' } else { '' }
$failures = @{}

foreach ($name in $Project) {
    $executable = Get-ChildItem -LiteralPath (Join-Path $root "tests\$name\bin\Debug") -Recurse -Filter "$name$suffix" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $executable) {
        throw "$name is not built. Run dotnet build DDT.slnx first."
    }

    for ($round = 1; $round -le $Rounds; $round++) {
        $log = Join-Path $logs "$name-$round.log"
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $process = Start-Process -FilePath $executable.FullName -ArgumentList '-noLogo', '-maxThreads', $AtOnce `
            -WorkingDirectory $executable.DirectoryName -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
        $process.ProcessorAffinity = $mask
        $process.WaitForExit()

        $text = @(Get-Content -LiteralPath $log)
        $failed = @($text | Where-Object { $_ -match '\[FAIL\]\s*$' } | ForEach-Object { ($_ -replace '\[FAIL\]\s*$', '').Trim() } | Sort-Object -Unique)
        $summary = $text | Where-Object { $_ -match 'Total: \d+' } | Select-Object -Last 1

        if (-not $summary) {
            $failed += "$name did not finish"
        }

        foreach ($test in $failed) {
            $failures[$test] = 1 + $(if ($failures.ContainsKey($test)) { $failures[$test] } else { 0 })
        }

        Write-Host ('{0} round {1}: {2} failed in {3} s' -f $name, $round, $failed.Count, [int] $watch.Elapsed.TotalSeconds)

        if ($failed.Count -eq 0) {
            Remove-Item -LiteralPath $log, "$log.err" -Force
        }
    }
}

if ($failures.Count -eq 0) {
    Write-Host "No test failed in $Rounds rounds with $AtOnce tests at once on $Cores of $all cores."

    return
}

$failures.GetEnumerator() |
    Sort-Object -Property @{ Expression = 'Value'; Descending = $true }, Name |
    ForEach-Object { [pscustomobject] @{ Failed = "$($_.Value) of $Rounds"; Test = $_.Name } } |
    Format-Table -AutoSize -Wrap |
    Out-String -Width 200 |
    Write-Host

throw "$($failures.Count) tests failed with $AtOnce tests at once on $Cores of $all cores. Their output is in $logs."
