// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.BitLocker;

// Starts encrypting the empty Windows partition with a clear key, so Apply image writes onto it encrypted. It doesn't
// wait. Without manage-bde or a TPM it skips with a warning or fails, as the step is set.
public sealed class PreprovisionBitLockerStepRunner(IBitLocker bitLocker, ITpm tpm, RunSession session, AgentLog log)
    : IStepKindRunner<PreprovisionBitLockerStep>
{
    public const string NoManageBde = "This boot image has no manage-bde. Build it again with build\\Build-BootImage.ps1 to pre-provision BitLocker.";

    public const string NoTpm = "This machine has no TPM that Windows PE can use.";

    public const string Skipped = "BitLocker is not pre-provisioned; Enable BitLocker encrypts the disk in Windows.";

    public async Task<StepResult> RunAsync(PreprovisionBitLockerStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        if (!bitLocker.IsInstalled)
        {
            return Unavailable(step, NoManageBde);
        }

        if (!tpm.IsPresent())
        {
            return Unavailable(step, NoTpm);
        }

        string volume = Volumes.DriveOf(session.RequireVolumes().Windows);
        log.Information($"Pre-provisioning BitLocker on {volume}.");

        if (!await bitLocker.PreprovisionAsync(volume, cancellationToken).ConfigureAwait(false))
        {
            return Unavailable(step, NoTpm);
        }

        context.Progress.Report(100);
        log.Information($"{volume} is encrypting with a clear key until Enable BitLocker protects it.");

        return StepResult.Done();
    }

    private StepResult Unavailable(PreprovisionBitLockerStep step, string reason)
    {
        if (step.IfUnavailable == BitLockerUnavailable.Fail)
        {
            return StepResult.Failed(reason);
        }

        log.Warning($"{reason} {Skipped}");

        return StepResult.Done();
    }
}
