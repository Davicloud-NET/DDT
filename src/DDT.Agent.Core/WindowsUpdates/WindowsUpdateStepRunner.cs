// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.WindowsUpdates;

// Searches, downloads and installs until Windows Update offers nothing more, restarting as the updates ask. A turn is
// one run between restarts; the checkpoint keeps what was installed and what failed over all of them. Each turn opens
// Windows Update and disposes it when it ends.
public sealed class WindowsUpdateStepRunner(Func<IWindowsUpdate> open, AgentLog log, TimeProvider timeProvider) : IStepKindRunner<WindowsUpdateStep>
{
    // Searches in one turn before the step restarts the machine anyway.
    public const int MaxPasses = 5;

    public static readonly TimeSpan BusyPoll = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan BusyLimit = TimeSpan.FromMinutes(30);

    public const string PendingFirst = "Windows needs a restart before it installs updates.";

    public const string GoesOn = "The machine restarts to finish the updates, then the step searches again.";

    public const string TriesAgain = "The machine restarts, then the step tries the updates that failed once more.";

    public const string Waits = "Windows Update is busy with another installation. The step waits for it.";

    public const string StillBusy = "Windows Update was still busy with another installation after 30 minutes.";

    public static string StillOffered(int passes) => $"Windows Update still offers updates after {passes} searches, so the machine restarts.";

    public static string OverLimit(int limit, IEnumerable<string> open) =>
        open.Any()
            ? $"The step reached its limit of {limit} restarts with updates still open: {string.Join("; ", open)}."
            : $"The step reached its limit of {limit} restarts, and Windows still needs a restart.";

    public static string CouldNotInstall(IEnumerable<FailedUpdate> failed) =>
        $"Windows Update could not install: {string.Join("; ", failed.Select(update => $"{update.Name} ({UpdateNames.HResult(update.HResult)})"))}.";

    public async Task<StepResult> RunAsync(WindowsUpdateStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        IWindowsUpdate updates = open();

        try
        {
            return await TurnAsync(updates, step, context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            (updates as IDisposable)?.Dispose();
        }
    }

    private async Task<StepResult> TurnAsync(IWindowsUpdate updates, WindowsUpdateStep step, StepContext context, CancellationToken cancellationToken)
    {
        UpdateCheckpoint done = UpdateCheckpoint.Read(context.Checkpoint.Saved);

        if (await updates.IsRestartPendingAsync(cancellationToken).ConfigureAwait(false))
        {
            log.Information(PendingFirst);

            return Restart(step, context, done, []);
        }

        if (await WaitWhileBusyAsync(updates, cancellationToken).ConfigureAwait(false) is { } busy)
        {
            return busy;
        }

        UpdatePass pass = new(updates, new UpdateJobs(updates, timeProvider), log);
        HashSet<string> named = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> failed = new(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<AvailableUpdate> open = [];

        for (int count = 0; count < MaxPasses; count++)
        {
            IReadOnlyList<AvailableUpdate> found = await updates.SearchAsync(new WindowsUpdateSearch(step.MicrosoftProducts), cancellationToken).ConfigureAwait(false);
            UpdateSelection selection = UpdateFilter.Choose(found, step, done, failed);
            NameLeftOut(selection.LeftOut, named);

            if (selection.ToInstall.Count == 0)
            {
                AvailableUpdate[] retries = [.. selection.LeftOut.Where(update => update.Reason == UpdateFilter.AfterRestart).Select(update => update.Update)];

                return retries.Length > 0 ? Restart(step, context, done, retries, TriesAgain) : Close(context, done, selection);
            }

            open = selection.ToInstall;
            Dictionary<string, int> tries = open.Select(update => update.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(id => id, id => Tries(done, id), StringComparer.OrdinalIgnoreCase);
            bool restart = await pass.RunAsync(open, context, done, cancellationToken).ConfigureAwait(false);
            failed.UnionWith(tries.Where(before => Tries(done, before.Key) > before.Value).Select(before => before.Key));

            if (restart)
            {
                return Restart(step, context, done, open);
            }
        }

        log.Information(StillOffered(MaxPasses));

        return Restart(step, context, done, open);
    }

    // Null once Windows Update is free.
    private async Task<StepResult?> WaitWhileBusyAsync(IWindowsUpdate updates, CancellationToken cancellationToken)
    {
        TimeSpan waited = TimeSpan.Zero;

        while (await updates.IsBusyAsync(cancellationToken).ConfigureAwait(false))
        {
            if (waited >= BusyLimit)
            {
                return StepResult.Failed(StillBusy);
            }

            if (waited == TimeSpan.Zero)
            {
                log.Information(Waits);
            }

            await Task.Delay(BusyPoll, timeProvider, cancellationToken).ConfigureAwait(false);
            waited += BusyPoll;
        }

        return null;
    }

    private void NameLeftOut(IReadOnlyList<LeftOutUpdate> leftOut, HashSet<string> named)
    {
        foreach (LeftOutUpdate update in leftOut.Where(update => named.Add(update.Update.Id)))
        {
            log.Information($"Leaves out {UpdateNames.Of(update.Update)}: {update.Reason}.");
        }
    }

    private static int Tries(UpdateCheckpoint done, string id) => done.Failed.TryGetValue(id, out FailedUpdate? failed) ? failed.Tries : 0;

    private StepResult Restart(WindowsUpdateStep step, StepContext context, UpdateCheckpoint done, IReadOnlyList<AvailableUpdate> open, string goesOn = GoesOn)
    {
        if (context.Checkpoint.Restarts >= step.RestartLimit)
        {
            return StepResult.Failed(OverLimit(step.RestartLimit, open.Select(UpdateNames.Of)));
        }

        log.Information(goesOn);

        return StepResult.RestartAndContinue(done.Write());
    }

    // The step's result: what it installed over all turns and what it left out. An update that failed twice fails it.
    private StepResult Close(StepContext context, UpdateCheckpoint done, UpdateSelection selection)
    {
        List<string> parts = [done.Installed.Count == 0 ? "nothing installed" : $"{done.Installed.Count} installed ({string.Join("; ", done.Installed.Values)})"];
        IEnumerable<LeftOutUpdate> leftOut = selection.LeftOut.Where(update => update.Reason != UpdateFilter.GaveUp);

        if (leftOut.Any())
        {
            parts.Add($"left out {string.Join("; ", leftOut.Select(update => $"{UpdateNames.Of(update.Update)} ({update.Reason})"))}");
        }

        string result = $"Windows Update: {string.Join(", ", parts)}.";
        List<FailedUpdate> failed = [.. done.Failed.Values.Where(update => update.Tries >= 2)];

        if (failed.Count > 0)
        {
            log.Warning(result);

            return StepResult.Failed(CouldNotInstall(failed));
        }

        context.Progress.Report(100);
        log.Information(result);

        return StepResult.Done();
    }
}
