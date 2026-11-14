// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Deployment;
using DDT.ConsoleProtocol;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.BitLocker;

// Waits until the volume is fully encrypted and protection is on, at most the step's minutes. The step's percent
// runs from 40 to 100 with the encryption's.
internal sealed class EncryptionWait(IBitLocker bitLocker, AgentLog log, TimeProvider timeProvider)
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private const int Start = 40;

    public static string TooLong(int minutes) =>
        string.Create(CultureInfo.InvariantCulture, $"BitLocker had not finished encrypting the disk after {minutes} minutes.");

    public async Task<StepResult> RunAsync(EnableBitLockerStep step, string volume, StepContext context, CancellationToken cancellationToken)
    {
        int polls = (int)Math.Ceiling(TimeSpan.FromMinutes(step.WaitMinutes) / PollInterval);
        log.Information($"Waiting until {volume} is encrypted and protected.");

        for (int poll = 1; ; poll++)
        {
            VolumeStatus status = await bitLocker.StatusAsync(volume, cancellationToken).ConfigureAwait(false);
            int percent = Math.Clamp(status.Percent, 0, 100);
            context.Detail.Report(StepDetails.Coded(StepDetails.Encrypting, percent));
            context.Progress.Report(Start + (percent * (100 - Start) / 100));

            if (status.IsDone)
            {
                log.Information($"{volume} is encrypted, and BitLocker protects it.");

                return StepResult.Done();
            }

            if (poll > polls)
            {
                return StepResult.Failed(TooLong(step.WaitMinutes));
            }

            await Task.Delay(PollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
