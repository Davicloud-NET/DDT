// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsUpdates;

namespace DDT.Agent.Tests;

// Windows Update as the spike saw it: each search answers with the next of Found, a stop ends a download at once, and
// an installation held by HoldInstall ends only when the test releases it, stop or not.
internal sealed class FakeWindowsUpdate : IWindowsUpdate, IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];
    private int _searches;

    public List<IReadOnlyList<AvailableUpdate>> Found { get; } = [];

    // By update id. Missing: succeeded.
    public Dictionary<string, UpdateOutcome> Downloads { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, UpdateOutcome> Installs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool RestartPending { get; set; }

    // Windows wants a restart once an installation has ended.
    public bool PendingAfterInstall { get; set; }

    // Answers to IsBusyAsync in order, then false.
    public Queue<bool> Busy { get; } = new();

    public bool InstallationNeedsRestart { get; set; }

    // The download runs until it is stopped.
    public bool HoldDownload { get; set; }

    public TaskCompletionSource? HoldInstall { get; set; }

    public TaskCompletionSource Installing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool AbortAsked { get; private set; }

    public bool InstallEnded { get; private set; }

    public List<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public List<WindowsUpdateSearch> Searches { get; } = [];

    public int Disposals { get; private set; }

    public void Dispose() => Disposals++;

    public Task<bool> IsRestartPendingAsync(CancellationToken cancellationToken)
    {
        Call("restart?");

        return Task.FromResult(RestartPending || (InstallEnded && PendingAfterInstall));
    }

    public Task<bool> IsBusyAsync(CancellationToken cancellationToken)
    {
        Call("busy?");

        return Task.FromResult(Busy.Count > 0 && Busy.Dequeue());
    }

    public Task<IReadOnlyList<AvailableUpdate>> SearchAsync(WindowsUpdateSearch search, CancellationToken cancellationToken)
    {
        Call("search");
        Searches.Add(search);
        int at = _searches++;

        return Task.FromResult(at < Found.Count ? Found[at] : []);
    }

    public Task AcceptEulaAsync(AvailableUpdate update, CancellationToken cancellationToken)
    {
        Call($"eula {update.Id}");

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<UpdateOutcome>> DownloadAsync(IReadOnlyList<AvailableUpdate> updates, IProgress<int> percent, CancellationToken cancellationToken)
    {
        Call($"download {Ids(updates)}");

        if (HoldDownload)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        percent.Report(50);
        percent.Report(100);

        return [.. updates.Select(update => Downloads.GetValueOrDefault(update.Id) ?? new UpdateOutcome(update.Id, UpdateResult.Succeeded))];
    }

    public async Task<UpdateInstallation> InstallAsync(IReadOnlyList<AvailableUpdate> updates, IProgress<int> percent, CancellationToken cancellationToken)
    {
        Call($"install {Ids(updates)}");
        Installing.TrySetResult();

        if (HoldInstall is { } hold)
        {
            using CancellationTokenRegistration registration = cancellationToken.Register(() => AbortAsked = true);
            await hold.Task;
        }

        percent.Report(100);
        InstallEnded = true;

        return new UpdateInstallation(
            [.. updates.Select(update => Installs.GetValueOrDefault(update.Id) ?? new UpdateOutcome(update.Id, UpdateResult.Succeeded))],
            InstallationNeedsRestart);
    }

    private static string Ids(IReadOnlyList<AvailableUpdate> updates) => string.Join(",", updates.Select(update => update.Id));

    private void Call(string call)
    {
        lock (_lock)
        {
            _calls.Add(call);
        }
    }
}
