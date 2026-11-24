// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;
using static DDT.Core.Tests.Sequences.TreeSteps;

namespace DDT.Core.Tests.Sequences;

// The Pre-provision BitLocker and Enable BitLocker steps, as the validator sees them.
public sealed class BitLockerStepRulesTests
{
    private static PreprovisionBitLockerStep Preprovision() => new() { Id = Guid.NewGuid(), Name = "Pre-provision BitLocker" };

    private static EnableBitLockerStep Enable() => new() { Id = Guid.NewGuid(), Name = "Enable BitLocker" };

    [Fact]
    public void PreprovisionsBetweenPartitionAndImageAndEnablesInWindowsWithVersion8()
    {
        SequenceDefinition definition = Definition(Partition(), Preprovision(), ApplyImage(), WriteUnattend(), Enable());

        Assert.Empty(Validate(definition));
        Assert.Equal(8, definition.RequiredVersion());
        Assert.Equal("sequence.versionTooLow", Assert.Single(Validate(definition with { Version = 7 })).Code);
    }

    [Fact]
    public void KnowsTheirPhasesRestartsAndResumes()
    {
        Assert.Equal((SequencePhase.WindowsPE, 0, false), (Preprovision().RequiredPhase, Preprovision().RestartLimit, Preprovision().Resumable));
        Assert.Equal((SequencePhase.Windows, 2, true), (Enable().RequiredPhase, Enable().RestartLimit, Enable().Resumable));
    }

    [Fact]
    public void DefaultsToSkipBeforeWindowsAndToFailWithBackupInIt()
    {
        EnableBitLockerStep enable = Enable();

        Assert.Equal(BitLockerUnavailable.Skip, Preprovision().IfUnavailable);
        Assert.Equal((BitLockerUnavailable.Fail, true, false, 120), (enable.IfNoTpm, enable.BackupToActiveDirectory, enable.WaitForEncryption, enable.WaitMinutes));
    }

    [Fact]
    public void PreprovisionsAfterPartition()
    {
        PreprovisionBitLockerStep preprovision = Preprovision();

        Assert.Contains(Validate(preprovision, Partition(), ApplyImage()), problem => problem.StepId == preprovision.Id && problem.Code == "sequence.bitLockerBeforePartition");
    }

    [Fact]
    public void PreprovisionsBeforeTheImage()
    {
        PreprovisionBitLockerStep preprovision = Preprovision();

        AssertOnly(Validate(Partition(), ApplyImage(), preprovision), preprovision, null, "sequence.bitLockerAfterImage");
    }

    [Fact]
    public void PreprovisionsOnce()
    {
        PreprovisionBitLockerStep again = Preprovision();

        AssertOnly(Validate(Partition(), Preprovision(), again, ApplyImage()), again, null, "sequence.oneBitLockerPreprovision");
    }

    [Fact]
    public void EnablesOnce()
    {
        EnableBitLockerStep again = Enable();

        AssertOnly(Validate(Partition(), ApplyImage(), Enable(), again), again, null, "sequence.oneBitLocker");
    }

    [Fact]
    public void DoesNotRunInARepeat()
    {
        EnableBitLockerStep enable = Enable();

        Assert.Contains(Validate(Partition(), ApplyImage(), Repeat(enable)), problem => problem.StepId == enable.Id && problem.Code == "sequence.onceInRepeat");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(EnableBitLockerStep.MaxWaitMinutes + 1)]
    public void WaitsOneToMaxMinutes(int minutes)
    {
        EnableBitLockerStep enable = Enable() with { WaitForEncryption = true, WaitMinutes = minutes };

        AssertOnly(Validate(Partition(), ApplyImage(), enable), enable, "waitMinutes", "sequence.bitLockerWaitMinutes");
    }

    [Fact]
    public void BelongToInstallingWindows()
    {
        PreprovisionBitLockerStep preprovision = Preprovision();

        AssertOnly(Validate(WriteRawImage(), preprovision), preprovision, null, "sequence.windowsWithRawImage");
    }
}
