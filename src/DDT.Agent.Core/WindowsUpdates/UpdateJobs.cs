// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.WindowsUpdates;

// Runs a pass's download and installation, each within JobLimit. The step's percent follows the jobs': the download
// fills the first half, the installation the second.
public sealed class UpdateJobs(IWindowsUpdate updates, TimeProvider timeProvider)
{
    public static readonly TimeSpan JobLimit = TimeSpan.FromHours(6);

    public static string Downloading(int count) => count == 1 ? "Downloading 1 update" : $"Downloading {count} updates";

    public static string Installing(int count) => count == 1 ? "Installing 1 update" : $"Installing {count} updates";

    public static string TooLong(string job) => $"Windows Update's {job} did not end within 6 hours.";

    // A stop ends the download at once.
    public async Task<IReadOnlyList<UpdateOutcome>> DownloadAsync(IReadOnlyList<AvailableUpdate> list, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(context);

        context.Detail.Report(Downloading(list.Count));
        using CancellationTokenSource limit = new(JobLimit, timeProvider);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);

        try
        {
            return await updates.DownloadAsync(list, new ScaledProgress(context.Progress, 0, 50), linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new DeploymentStepException(TooLong("download"));
        }
    }

    // A stop is passed on, and the installation is waited for: Windows Update cannot abort one.
    public async Task<UpdateInstallation> InstallAsync(IReadOnlyList<AvailableUpdate> list, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(context);

        context.Detail.Report(Installing(list.Count));
        using CancellationTokenSource abort = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<UpdateInstallation> job = updates.InstallAsync(list, new ScaledProgress(context.Progress, 50, 100), abort.Token);

        try
        {
            return await job.WaitAsync(JobLimit, timeProvider, CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await abort.CancelAsync().ConfigureAwait(false);

            throw new DeploymentStepException(TooLong("installation"));
        }
    }
}
