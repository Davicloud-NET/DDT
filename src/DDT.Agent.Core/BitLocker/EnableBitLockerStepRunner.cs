// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsFeatures;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.BitLocker;

// Adds the TPM protector and a recovery password to the Windows volume, hands the password to the server, backs it up
// to AD on a joined machine, and only then turns protection on. The checkpoint keeps the protector id and what the
// server confirmed, never the password. A protector the server never confirmed is deleted and made anew.
public sealed class EnableBitLockerStepRunner(
    IAgentServer server,
    RunSession session,
    Func<CancellationToken, Task> reportRunning,
    BitLockerTools tools,
    AgentLog log,
    TimeProvider timeProvider) : IStepKindRunner<EnableBitLockerStep>
{
    public const string FeatureName = "BitLocker";

    public const string NoTpm = "This machine has no TPM that BitLocker can use.";

    public const string SkippedNoTpm = "This machine has no TPM that BitLocker can use, so BitLocker is not enabled.";

    public const string GoesOn = "The machine restarts to finish the BitLocker feature, then the step goes on.";

    public const string NoFeature = "This Windows has no BitLocker feature that DISM can enable.";

    public const string StillNoManageBde = "The BitLocker feature is enabled, but manage-bde is still missing.";

    public async Task<StepResult> RunAsync(EnableBitLockerStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        // The server takes the key only while it knows this step is running.
        await reportRunning(cancellationToken).ConfigureAwait(false);

        string volume = Volumes.System;
        BitLockerCheckpoint checkpoint = BitLockerCheckpoint.Read(context.Checkpoint.Saved);

        if (!tools.BitLocker.IsInstalled && await EnableFeatureAsync(checkpoint, cancellationToken).ConfigureAwait(false) is { } feature)
        {
            return feature;
        }

        if (!tools.Tpm.IsPresent())
        {
            return WithoutTpm(step);
        }

        if (!checkpoint.Confirmed && !await HandOverAsync(volume, checkpoint, context, cancellationToken).ConfigureAwait(false))
        {
            return WithoutTpm(step);
        }

        context.Progress.Report(30);
        await new ActiveDirectoryBackup(tools, log).RunAsync(step, volume, checkpoint, context.Checkpoint, cancellationToken).ConfigureAwait(false);

        log.Information($"Turning BitLocker on for {volume}.");
        await tools.BitLocker.TurnOnAsync(volume, cancellationToken).ConfigureAwait(false);

        if (!step.WaitForEncryption)
        {
            context.Progress.Report(100);
            log.Information($"BitLocker encrypts {volume} and turns protection on when it is done.");

            return StepResult.Done();
        }

        context.Progress.Report(40);

        return await new EncryptionWait(tools.BitLocker, log, timeProvider).RunAsync(step, volume, context, cancellationToken).ConfigureAwait(false);
    }

    // Windows Server has manage-bde only with its BitLocker feature, which takes a restart. Null goes on at once.
    private async Task<StepResult?> EnableFeatureAsync(BitLockerCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        if (checkpoint.FeatureEnabled)
        {
            return StepResult.Failed(StillNoManageBde);
        }

        log.Information($"manage-bde is missing, so the {FeatureName} feature is enabled first.");

        switch (await tools.Features.EnableAsync(FeatureName, cancellationToken).ConfigureAwait(false))
        {
            case FeatureOutcome.Unknown:
                return StepResult.Failed(NoFeature);
            case FeatureOutcome.RestartNeeded:
                checkpoint.FeatureEnabled = true;
                log.Information(GoesOn);

                return StepResult.RestartAndContinue(checkpoint.Write());
            default:
                return tools.BitLocker.IsInstalled ? null : StepResult.Failed(StillNoManageBde);
        }
    }

    // False when Windows finds no usable TPM after all.
    private async Task<bool> HandOverAsync(string volume, BitLockerCheckpoint checkpoint, StepContext context, CancellationToken cancellationToken)
    {
        // Leftovers of an attempt that was cut off before the server had the password. A fresh Windows has none.
        foreach (Guid leftover in await tools.BitLocker.RecoveryPasswordIdsAsync(volume, cancellationToken).ConfigureAwait(false))
        {
            log.Information($"Deleting recovery password protector {leftover:B}, which the server never confirmed.");
            await tools.BitLocker.DeleteProtectorAsync(volume, leftover, cancellationToken).ConfigureAwait(false);
        }

        if (!await tools.BitLocker.HasTpmProtectorAsync(volume, cancellationToken).ConfigureAwait(false)
            && !await tools.BitLocker.AddTpmProtectorAsync(volume, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (await tools.BitLocker.AddRecoveryPasswordAsync(volume, cancellationToken).ConfigureAwait(false) is not { } key)
        {
            return false;
        }

        checkpoint.ProtectorId = key.ProtectorId;
        await context.Checkpoint.SaveAsync(checkpoint.Write()).ConfigureAwait(false);
        log.Information($"Added recovery password protector {key.ProtectorId:B} to {volume}. Handing its password to the server.");

        await ServerCallRules.CallAsync(
            async call =>
            {
                await server.SendRunRecordAsync(session.MachineId, session.Tokens.Token, session.Run.Id, new AgentRecoveryKey(key.ProtectorId, key.Password, volume), call)
                    .ConfigureAwait(false);

                return true;
            },
            "the recovery key",
            log,
            timeProvider,
            cancellationToken).ConfigureAwait(false);

        checkpoint.Confirmed = true;
        await context.Checkpoint.SaveAsync(checkpoint.Write()).ConfigureAwait(false);
        log.Information($"The server has the recovery password of protector {key.ProtectorId:B}.");

        return true;
    }

    private StepResult WithoutTpm(EnableBitLockerStep step)
    {
        if (step.IfNoTpm == BitLockerUnavailable.Fail)
        {
            return StepResult.Failed(NoTpm);
        }

        log.Warning(SkippedNoTpm);

        return StepResult.Done();
    }
}
