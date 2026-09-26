// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Contracts.Sequences;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DeploymentStep = DDT.Server.Deployments.DeploymentStep;

namespace DDT.Server.Live;

// Pushes are not awaited by the request that caused them: a browser that stops reading would otherwise
// hold up an agent's poll or an operator's approval.
public sealed partial class LiveNotifier(
    IHubContext<LiveHub> hub,
    TimeProvider timeProvider,
    IHostApplicationLifetime lifetime,
    ILogger<LiveNotifier> logger)
{
    // A running sequence reports every few seconds, and every browser redraws the machine's row for each push.
    public static readonly TimeSpan MachinePushInterval = TimeSpan.FromSeconds(1);

    // An agent flushes its log every few seconds, and a watcher reads what is new with each push.
    public static readonly TimeSpan LogPushInterval = TimeSpan.FromSeconds(1);

    private readonly PushThrottle _machines = new(timeProvider, MachinePushInterval, lifetime.ApplicationStopping);

    private readonly PushThrottle _logs = new(timeProvider, LogPushInterval, lifetime.ApplicationStopping);

    // Keyed by the run rather than the machine, so a new run never waits behind the push of the machine's last one.
    private readonly PushThrottle _runs = new(timeProvider, MachinePushInterval, lifetime.ApplicationStopping);

    private readonly Lock _runLock = new();

    // What went out last for each run. Most machine pushes come from polls that change nothing in the run, and those
    // send no run push. Forgotten once it holds this many, which at worst sends a run again.
    private readonly Dictionary<Guid, RunHistoryItem> _runsSent = [];

    // Removed machines, whose runs went with them, so a run push that still waits stays unsent. A machine that comes back
    // registers under a new id, so remembering the last few hundred is enough.
    private readonly Queue<Guid> _removedOrder = [];
    private readonly HashSet<Guid> _removed = [];

    private const int RememberedRuns = 1024;
    private const int RememberedRemovals = 256;

    // The deployment the Machines page shows for this machine, see MachineSummaries.From. Taken now, so a push the
    // throttle delays still carries the latest state. The run history gets the same deployment as a row of its own.
    public void MachineChanged(Machine machine, Deployment? deployment)
    {
        ArgumentNullException.ThrowIfNull(machine);

        MachineSummary summary = MachineSummaries.From(machine, deployment);
        _machines.Push(summary.Id, () => PushAsync(summary));

        if (deployment is not null)
        {
            RunHistoryItem run = RunHistoryItems.From(machine, deployment);
            _runs.Push(deployment.Id, () => PushRunAsync(run));
        }
    }

    // Only the connections that watch the machine get its steps.
    public void RunStepsChanged(Guid machineId, IEnumerable<DeploymentStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        foreach (DeploymentStep step in steps)
        {
            _ = PushToWatchersAsync(
                machineId,
                LiveEvents.RunStepChanged,
                new RunStepChangedEvent(machineId, step.DeploymentId, DeploymentSummaries.Step(step)));
        }
    }

    public void MachineLogAppended(Guid machineId, long lastLineId) =>
        _logs.Push(
            machineId,
            () => PushToWatchersAsync(machineId, LiveEvents.MachineLogAppended, new MachineLogAppendedEvent(machineId, lastLineId)));

    // A push that still waits would bring a removed machine back to the page.
    public void MachinesRemoved(IEnumerable<Guid> machineIds)
    {
        ArgumentNullException.ThrowIfNull(machineIds);

        Guid[] removed = [.. machineIds];

        foreach (Guid machineId in removed)
        {
            _machines.Discard(machineId);
        }

        lock (_runLock)
        {
            foreach (Guid machineId in removed.Where(_removed.Add))
            {
                _removedOrder.Enqueue(machineId);
            }

            while (_removedOrder.Count > RememberedRemovals)
            {
                _removed.Remove(_removedOrder.Dequeue());
            }
        }

        _ = PushEventAsync(LiveEvents.MachinesRemoved, new MachinesRemovedEvent(removed));
    }

    public void ImagesChanged() => _ = PushEventAsync(LiveEvents.ImagesChanged);

    public void PackagesChanged() => _ = PushEventAsync(LiveEvents.PackagesChanged);

    public void RulesChanged() => _ = PushEventAsync(LiveEvents.RulesChanged);

    public void SequenceChanged(SequenceChangedEvent change)
    {
        ArgumentNullException.ThrowIfNull(change);

        _ = PushEventAsync(LiveEvents.SequenceChanged, change);
    }

    private async Task PushEventAsync(string liveEvent, object? payload = null)
    {
        try
        {
            // A null argument would still be sent as one, so an event without a payload is sent without arguments.
            await (payload is null
                ? hub.Clients.All.SendAsync(liveEvent, CancellationToken.None)
                : hub.Clients.All.SendAsync(liveEvent, payload, CancellationToken.None)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogEventPushFailed(liveEvent, exception);
        }
    }

    private async Task PushToWatchersAsync(Guid machineId, string liveEvent, object payload)
    {
        try
        {
            await hub.Clients.Group(LiveGroups.Machine(machineId)).SendAsync(liveEvent, payload, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogEventPushFailed(liveEvent, exception);
        }
    }

    private async Task PushAsync(MachineSummary summary)
    {
        try
        {
            await hub.Clients.All.SendAsync(LiveEvents.MachineChanged, summary, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogPushFailed(summary.Id, exception);
        }
    }

    private async Task PushRunAsync(RunHistoryItem run)
    {
        lock (_runLock)
        {
            if (_removed.Contains(run.MachineId) || (_runsSent.TryGetValue(run.Run.Id, out RunHistoryItem? sent) && sent == run))
            {
                return;
            }

            if (_runsSent.Count >= RememberedRuns)
            {
                _runsSent.Clear();
            }

            _runsSent[run.Run.Id] = run;
        }

        await PushEventAsync(LiveEvents.RunChanged, run).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 430, Level = LogLevel.Warning, Message = "Could not push the change to machine {MachineId}")]
    private partial void LogPushFailed(Guid machineId, Exception exception);

    [LoggerMessage(EventId = 431, Level = LogLevel.Warning, Message = "Could not push the event {LiveEvent}")]
    private partial void LogEventPushFailed(string liveEvent, Exception exception);
}
