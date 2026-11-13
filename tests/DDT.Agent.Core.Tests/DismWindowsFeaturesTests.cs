// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.WindowsFeatures;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class DismWindowsFeaturesTests
{
    private readonly RecordingToolRunner _tools = new();

    private Task<FeatureOutcome> EnableAsync(int exitCode)
    {
        _tools.AnswerExitCode = (_, _, _) => exitCode;

        return new DismWindowsFeatures(_tools, @"C:\Windows\System32\dism.exe").EnableAsync("NetFx3", TestContext.Current.CancellationToken);
    }

    // An enabled feature ends with 0 too. 0x800F080C is the feature name DISM does not know.
    [Theory]
    [InlineData(0, FeatureOutcome.Enabled)]
    [InlineData(3010, FeatureOutcome.RestartNeeded)]
    [InlineData(unchecked((int)0x800F080C), FeatureOutcome.Unknown)]
    public async Task TellsTheOutcomeByDismsExitCode(int exitCode, FeatureOutcome outcome)
    {
        Assert.Equal(outcome, await EnableAsync(exitCode));
        Assert.Equal(
            [@"C:\Windows\System32\dism.exe /Online /Enable-Feature /FeatureName:NetFx3 /All /NoRestart /Quiet"],
            _tools.Calls);
    }

    // 0x800F081F: the source files of NetFx3 could not be found.
    [Fact]
    public async Task AnyOtherExitCodeFailsWithItInHexAndWhereDismLogs()
    {
        DeploymentStepException failure = await Assert.ThrowsAsync<DeploymentStepException>(() => EnableAsync(unchecked((int)0x800F081F)));

        Assert.Equal(@"DISM could not enable NetFx3 (exit code 0x800F081F). Its log is %WINDIR%\Logs\DISM\dism.log.", failure.Message);
    }
}
