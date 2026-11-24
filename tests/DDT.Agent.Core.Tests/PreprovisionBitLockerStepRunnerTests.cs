// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.BitLocker;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class PreprovisionBitLockerStepRunnerTests
{
    [Fact]
    public async Task StartsEncryptingTheWindowsPartition()
    {
        using BitLockerRunFixture run = new();

        StepResult result = await run.PreprovisionAsync();

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal([$"preprovision {Volumes.DriveOf(run.Tools.Volumes.Windows)}"], run.Events);
        Assert.Equal([100], run.Progress.Values);
    }

    public static TheoryData<string, string> Unavailable => new()
    {
        { "no manage-bde", PreprovisionBitLockerStepRunner.NoManageBde },
        { "no TPM", PreprovisionBitLockerStepRunner.NoTpm },
        { "manage-bde finds no TPM", PreprovisionBitLockerStepRunner.NoTpm },
    };

    [Theory]
    [MemberData(nameof(Unavailable))]
    public async Task SkipsWithAWarningWhenBitLockerIsUnavailable(string why, string reason)
    {
        using BitLockerRunFixture run = new();
        MakeUnavailable(run, why);

        StepResult result = await run.PreprovisionAsync();

        Assert.Equal(StepResult.Done(), result);
        Assert.Contains($"{reason} {PreprovisionBitLockerStepRunner.Skipped}", await run.EverythingLoggedAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Unavailable))]
    public async Task FailsWhenSetToAndBitLockerIsUnavailable(string why, string reason)
    {
        using BitLockerRunFixture run = new();
        MakeUnavailable(run, why);

        StepResult result = await run.PreprovisionAsync(BitLockerRunFixture.Preprovision with { IfUnavailable = BitLockerUnavailable.Fail });

        Assert.Equal(StepResult.Failed(reason), result);
    }

    private static void MakeUnavailable(BitLockerRunFixture run, string why)
    {
        switch (why)
        {
            case "no manage-bde":
                run.BitLocker.IsInstalled = false;
                break;
            case "no TPM":
                run.Machine.Present = false;
                break;
            default:
                run.BitLocker.HasTpm = false;
                break;
        }
    }
}
