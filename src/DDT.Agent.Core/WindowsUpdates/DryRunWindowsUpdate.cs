// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;

namespace DDT.Agent.WindowsUpdates;

// Stands in for Windows Update with made-up updates: two, then one, then none, each set needing a restart. Each
// installation counts in jobsFile, because the agent starts over after a restart.
public sealed class DryRunWindowsUpdate(AgentLog log, string jobsFile) : IWindowsUpdate
{
    private static readonly AvailableUpdate[][] s_turns =
    [
        [
            new("6f1c6e3a-0000-4000-8000-000000000001", 1, "2026-11 Cumulative Update for Windows 11 (KB5070001)", ["5070001"]),
            new("6f1c6e3a-0000-4000-8000-000000000002", 1, "2026-11 Cumulative Update for .NET Framework", ["5070002"]),
        ],
        [
            new("6f1c6e3a-0000-4000-8000-000000000003", 1, "Update for Microsoft Defender Antivirus antimalware platform", ["4052623"]),
        ],
    ];

    public Task<bool> IsRestartPendingAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<bool> IsBusyAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<IReadOnlyList<AvailableUpdate>> SearchAsync(WindowsUpdateSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        int jobs = Jobs();
        IReadOnlyList<AvailableUpdate> found = jobs < s_turns.Length ? s_turns[jobs] : [];
        log.Information($"Dry run: Windows Update is not searched. {found.Count} made-up updates stand in for what it finds.");

        return Task.FromResult(found);
    }

    public Task AcceptEulaAsync(AvailableUpdate update, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<UpdateOutcome>> DownloadAsync(IReadOnlyList<AvailableUpdate> updates, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(percent);

        percent.Report(100);

        return Task.FromResult<IReadOnlyList<UpdateOutcome>>([.. updates.Select(update => new UpdateOutcome(update.Id, UpdateResult.Succeeded))]);
    }

    public async Task<UpdateInstallation> InstallAsync(IReadOnlyList<AvailableUpdate> updates, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(percent);

        foreach (AvailableUpdate update in updates)
        {
            log.Information($"Dry run: {UpdateNames.Of(update)} is not installed, and taken as installed once the machine restarts.");
        }

        await File.WriteAllTextAsync(jobsFile, (Jobs() + 1).ToString(CultureInfo.InvariantCulture), CancellationToken.None).ConfigureAwait(false);
        percent.Report(100);

        return new UpdateInstallation([.. updates.Select(update => new UpdateOutcome(update.Id, UpdateResult.Succeeded, RestartNeeded: true))], RestartNeeded: true);
    }

    private int Jobs() =>
        File.Exists(jobsFile) && int.TryParse(File.ReadAllText(jobsFile), CultureInfo.InvariantCulture, out int jobs) ? jobs : 0;
}
