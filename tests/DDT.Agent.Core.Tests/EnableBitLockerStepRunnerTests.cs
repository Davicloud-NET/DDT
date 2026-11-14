// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.BitLocker;
using DDT.Agent.WindowsFeatures;
using DDT.ConsoleProtocol;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class EnableBitLockerStepRunnerTests
{
    private static readonly string s_volume = Volumes.System;

    // Protection goes on only once the server has the password.
    [Fact]
    public async Task AddsTheProtectorsHandsTheKeyOverAndOnlyThenTurnsBitLockerOn()
    {
        using BitLockerRunFixture run = new();
        run.ServerTakesTheKey();

        StepResult result = await run.EnableAsync();

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(
            [$"list {s_volume}", $"has-tpm {s_volume}", $"add-tpm {s_volume}", $"add-password {s_volume}", "server has the key", $"turn-on {s_volume}"],
            run.Events);
        Assert.Equal(new AgentRecoveryKey(FakeBitLocker.ProtectorId, FakeBitLocker.Password, s_volume), run.Key);
        Assert.Equal([$"run-report {DeploymentState.Running} session", "run-record AgentRecoveryKey session"], run.Server.Calls);
        Assert.Equal([30, 100], run.Progress.Values);
    }

    [Fact]
    public async Task KeepsATpmProtectorThatIsThere()
    {
        using BitLockerRunFixture run = new();
        run.ServerTakesTheKey();
        run.BitLocker.TpmProtector = true;

        await run.EnableAsync();

        Assert.DoesNotContain($"add-tpm {s_volume}", run.Events);
    }

    [Fact]
    public async Task NeverLogsOrSavesThePassword()
    {
        using BitLockerRunFixture run = new();
        run.ServerTakesTheKey();

        await run.EnableAsync();

        Assert.DoesNotContain(FakeBitLocker.Password, await run.EverythingLoggedAsync(), StringComparison.Ordinal);
        Assert.Equal(2, run.Saves.Count);
        Assert.All(run.Saves, saved => Assert.DoesNotContain(FakeBitLocker.Password, saved, StringComparison.Ordinal));
        Assert.True(BitLockerCheckpoint.Read(run.Saves[^1]) is { Confirmed: true } confirmed && confirmed.ProtectorId == FakeBitLocker.ProtectorId);
    }

    // As manage-bde and the recovery screen show it.
    [Fact]
    public async Task NamesTheKeyIdInUpperCase()
    {
        using BitLockerRunFixture run = new();
        run.ServerTakesTheKey();

        await run.EnableAsync();

        Assert.Contains("The server has the recovery password of protector {748DC9C3-3024-4646-9069-B9EFBEED68E4}.", await run.EverythingLoggedAsync(), StringComparison.Ordinal);
    }

    // The server refused or never answered: the step fails, protection stays off, and the failure names no password.
    [Fact]
    public async Task AKeyTheServerRefusesFailsTheStepWithProtectionOff()
    {
        using BitLockerRunFixture run = new();
        run.Server.OnRunRecord(_ => throw new AgentRequestException("409", "Only a running Enable BitLocker step hands over a recovery key.", System.Net.HttpStatusCode.Conflict));

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => run.EnableAsync());

        Assert.DoesNotContain(FakeBitLocker.Password, failure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain($"turn-on {s_volume}", run.Events);
        Assert.DoesNotContain(FakeBitLocker.Password, await run.EverythingLoggedAsync(), StringComparison.Ordinal);
        Assert.False(BitLockerCheckpoint.Read(Assert.Single(run.Saves)).Confirmed);
    }

    // A cut-off turn added a protector the server never confirmed; it goes, and a new one is made.
    [Fact]
    public async Task DeletesAnUnconfirmedProtectorAndMakesANewOne()
    {
        using BitLockerRunFixture run = new();
        run.ServerTakesTheKey();
        Guid leftover = Guid.Parse("4a5359b5-b088-431b-91e0-c915fee4f6b4");
        run.BitLocker.Leftovers.Add(leftover);
        run.BitLocker.TpmProtector = true;

        StepResult result = await run.EnableAsync(saved: new BitLockerCheckpoint { ProtectorId = leftover }.Write());

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal([$"list {s_volume}", $"delete {s_volume} {leftover:B}", $"has-tpm {s_volume}", $"add-password {s_volume}"], run.Events.Take(4));
        Assert.Equal(FakeBitLocker.ProtectorId, run.Key?.ProtectorId);
    }

    [Fact]
    public async Task AConfirmedKeyIsNotMadeAgain()
    {
        using BitLockerRunFixture run = new();

        StepResult result = await run.EnableAsync(saved: new BitLockerCheckpoint { ProtectorId = FakeBitLocker.ProtectorId, Confirmed = true }.Write());

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal([$"turn-on {s_volume}"], run.Events);
        Assert.DoesNotContain(run.Server.Calls, call => call.StartsWith("run-record", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutATpmFailsOrSkipsAsSet(bool skip)
    {
        using BitLockerRunFixture run = new();
        run.Machine.Present = false;

        StepResult result = await run.EnableAsync(BitLockerRunFixture.Enable with { IfNoTpm = skip ? BitLockerUnavailable.Skip : BitLockerUnavailable.Fail });

        Assert.Equal(skip ? StepResult.Done() : StepResult.Failed(EnableBitLockerStepRunner.NoTpm), result);
        Assert.Empty(run.Events);
        Assert.Equal(skip, (await run.EverythingLoggedAsync()).Contains(EnableBitLockerStepRunner.SkippedNoTpm, StringComparison.Ordinal));
    }

    // manage-bde's own "no TPM" error counts the same.
    [Fact]
    public async Task ATpmManageBdeCannotUseCountsAsNone()
    {
        using BitLockerRunFixture run = new();
        run.BitLocker.HasTpm = false;

        StepResult result = await run.EnableAsync();

        Assert.Equal(StepResult.Failed(EnableBitLockerStepRunner.NoTpm), result);
        Assert.DoesNotContain(run.Events, call => call.StartsWith("add-password", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnAServerEnablesTheFeatureFirstAndGoesOnAfterTheRestart()
    {
        using BitLockerRunFixture run = new();
        run.BitLocker.IsInstalled = false;
        run.Features.Outcomes["BitLocker"] = FeatureOutcome.RestartNeeded;

        StepResult first = await run.EnableAsync();
        run.BitLocker.IsInstalled = true;
        run.ServerTakesTheKey();
        StepResult second = await run.EnableAsync(saved: first.Progress);

        Assert.Equal(StepOutcome.RestartAndContinue, first.Outcome);
        Assert.Equal(["BitLocker"], run.Features.Asked);
        Assert.Equal(StepResult.Done(), second);
        Assert.Contains($"turn-on {s_volume}", run.Events);
    }

    [Fact]
    public async Task AFeatureThatLeavesNoManageBdeFails()
    {
        using BitLockerRunFixture run = new();
        run.BitLocker.IsInstalled = false;

        StepResult result = await run.EnableAsync(saved: new BitLockerCheckpoint { FeatureEnabled = true }.Write());

        Assert.Equal(StepResult.Failed(EnableBitLockerStepRunner.StillNoManageBde), result);
        Assert.Empty(run.Features.Asked);
    }

    // Only the absent values are set, and exactly those removed after the backup.
    [Fact]
    public async Task OnAJoinedMachineBacksUpToActiveDirectoryWithThePolicyForTheCallOnly()
    {
        using BitLockerRunFixture run = new();
        run.ServerTakesTheKey();
        run.Machine.Joined = true;
        run.Policy.Values["OSRecovery"] = 1;

        StepResult result = await run.EnableAsync();

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal(
            [
                "set OSActiveDirectoryBackup",
                "set OSActiveDirectoryInfoToStore",
                "set ActiveDirectoryBackup",
                "set ActiveDirectoryInfoToStore",
                $"backup {s_volume} {FakeBitLocker.ProtectorId:B}",
                "remove OSActiveDirectoryBackup,OSActiveDirectoryInfoToStore,ActiveDirectoryBackup,ActiveDirectoryInfoToStore",
                $"turn-on {s_volume}",
            ],
            run.Events.SkipWhile(call => call != "server has the key").Skip(1));
        Assert.Equal(new Dictionary<string, int> { ["OSRecovery"] = 1 }, run.Policy.Values);
    }

    [Fact]
    public async Task AFailedBackupFailsTheStepWithProtectionOffAndThePolicyBack()
    {
        using BitLockerRunFixture run = new();
        run.ServerTakesTheKey();
        run.Machine.Joined = true;
        run.BitLocker.BackupFails = true;

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(() => run.EnableAsync());

        Assert.Contains("0x8031006A", failure.Message, StringComparison.Ordinal);
        Assert.Empty(run.Policy.Values);
        Assert.DoesNotContain($"turn-on {s_volume}", run.Events);
    }

    [Fact]
    public async Task DoesNotBackUpWhenNotJoinedOrNotSet()
    {
        using BitLockerRunFixture run = new();
        run.ServerTakesTheKey();
        run.ServerTakesTheKey();

        await run.EnableAsync();
        run.Machine.Joined = true;
        await run.EnableAsync(BitLockerRunFixture.Enable with { BackupToActiveDirectory = false });

        Assert.DoesNotContain(run.Events, call => call.StartsWith("backup", StringComparison.Ordinal) || call.StartsWith("set", StringComparison.Ordinal));
    }

    // A backup cut off by a power loss left its values: the next turn removes them first.
    [Fact]
    public async Task RemovesThePolicyValuesACutOffBackupLeft()
    {
        using BitLockerRunFixture run = new();
        run.Policy.Values["OSActiveDirectoryBackup"] = 1;
        BitLockerCheckpoint saved = new() { ProtectorId = FakeBitLocker.ProtectorId, Confirmed = true, Policy = ["OSActiveDirectoryBackup"] };

        await run.EnableAsync(BitLockerRunFixture.Enable with { BackupToActiveDirectory = false }, saved.Write());

        Assert.Equal(["remove OSActiveDirectoryBackup", $"turn-on {s_volume}"], run.Events);
        Assert.Empty(run.Policy.Values);
    }

    [Fact]
    public async Task WaitsUntilTheDiskIsEncryptedAndProtected()
    {
        using BitLockerRunFixture run = new();
        run.ServerTakesTheKey();
        run.BitLocker.Statuses.Enqueue(new VolumeStatus(2, 74, false));
        run.BitLocker.Statuses.Enqueue(new VolumeStatus(1, 100, true));

        StepResult result = await run.EnableAsync(BitLockerRunFixture.Enable with { WaitForEncryption = true });

        Assert.Equal(StepResult.Done(), result);
        Assert.Equal([StepDetails.Coded(StepDetails.Encrypting, 74), StepDetails.Coded(StepDetails.Encrypting, 100)], run.Details.Values);
        Assert.Equal([30, 40, 84, 100], run.Progress.Values);
        Assert.Equal([EncryptionWait.PollInterval], run.Time.Delays);
    }

    // One minute is the first look and four more, 15 s apart.
    [Fact]
    public async Task GivesUpAfterTheWaitLimit()
    {
        using BitLockerRunFixture run = new();
        run.ServerTakesTheKey();
        run.BitLocker.Statuses.Enqueue(new VolumeStatus(2, 10, false));

        StepResult result = await run.EnableAsync(BitLockerRunFixture.Enable with { WaitForEncryption = true, WaitMinutes = 1 });

        Assert.Equal(StepResult.Failed(EncryptionWait.TooLong(1)), result);
        Assert.Equal(5, run.Events.Count(call => call.StartsWith("status", StringComparison.Ordinal)));
    }
}
