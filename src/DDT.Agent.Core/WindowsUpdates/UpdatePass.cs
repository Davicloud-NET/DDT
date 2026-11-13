// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Sequences;

namespace DDT.Agent.WindowsUpdates;

// Downloads and installs one search's updates, and records each one's own result in the checkpoint. The job's overall
// code is never read: it says "succeeded with errors" for a mix.
public sealed class UpdatePass(IWindowsUpdate updates, UpdateJobs jobs, AgentLog log)
{
    // True when the machine has to restart before the next search.
    public async Task<bool> RunAsync(IReadOnlyList<AvailableUpdate> list, StepContext context, UpdateCheckpoint done, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(done);

        foreach (AvailableUpdate update in list)
        {
            log.Information($"To install: {UpdateNames.Of(update)}.");

            if (!update.EulaAccepted)
            {
                log.Information($"Accepting the licence terms of {UpdateNames.Of(update)}.");
                await updates.AcceptEulaAsync(update, cancellationToken).ConfigureAwait(false);
            }
        }

        IReadOnlyList<UpdateOutcome> downloads = await jobs.DownloadAsync(list, context, cancellationToken).ConfigureAwait(false);
        Record(list, downloads, done, "download");
        List<AvailableUpdate> ready = [.. list.Where(update => Outcome(downloads, update)?.Result == UpdateResult.Succeeded)];
        bool restart = false;

        if (ready.Count > 0)
        {
            UpdateInstallation installation = await jobs.InstallAsync(ready, context, cancellationToken).ConfigureAwait(false);
            Record(ready, installation.Outcomes, done, "installation");
            restart = installation.RestartNeeded || installation.Outcomes.Any(outcome => outcome.RestartNeeded);
        }

        await context.Checkpoint.SaveAsync(done.Write()).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return restart || await updates.IsRestartPendingAsync(cancellationToken).ConfigureAwait(false);
    }

    private static UpdateOutcome? Outcome(IReadOnlyList<UpdateOutcome> outcomes, AvailableUpdate update) =>
        outcomes.FirstOrDefault(outcome => string.Equals(outcome.Id, update.Id, StringComparison.OrdinalIgnoreCase));

    // A download only records its failures: the installation tells whether an update is in.
    private void Record(IReadOnlyList<AvailableUpdate> list, IReadOnlyList<UpdateOutcome> outcomes, UpdateCheckpoint done, string job)
    {
        bool installing = job == "installation";

        foreach (AvailableUpdate update in list)
        {
            string name = UpdateNames.Of(update);

            switch (Outcome(outcomes, update))
            {
                case { Result: UpdateResult.Succeeded } outcome when installing:
                    done.Succeeded(update.Id, name);
                    log.Information(outcome.RestartNeeded ? $"Installed {name}, which needs a restart." : $"Installed {name}.");
                    break;
                case { Result: UpdateResult.Failed } outcome:
                    done.Failure(update.Id, name, outcome.HResult);
                    log.Warning(done.GaveUp(update.Id)
                        ? $"The {job} of {name} failed again ({UpdateNames.HResult(outcome.HResult)}). It is left out."
                        : $"The {job} of {name} failed ({UpdateNames.HResult(outcome.HResult)}). It is tried once more.");
                    break;
                case { Result: UpdateResult.Aborted } or null:
                    log.Information($"The {job} of {name} was aborted.");
                    break;
            }
        }
    }
}
