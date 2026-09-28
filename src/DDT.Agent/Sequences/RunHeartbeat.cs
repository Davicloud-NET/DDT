// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// The only sender of run reports and log batches during a run, so none of them overlap or arrive out of order. Every
// interval, and when the run's state or activity changes, at once or MinimumSpacing after the last beat, it reports a
// snapshot of the run and sends one log batch. Each report brings fresh tokens, which keeps the session alive through
// a download of any length, and the run token, which saveRunToken writes to the disk when it is a new one.
//
// While the run waits for someone, at a pause or for the answers to its inputs, it beats as often as the server asks,
// every WaitingInterval unless it says otherwise, as someone on the web may answer at any moment, and the answers come
// back with the reports: a pause continued on the web, and the run's values once its inputs are answered. A report
// carries the sequence's variables whenever they changed since the server last took them.
public sealed class RunHeartbeat(
    IAgentServer server,
    AgentLog log,
    DeploymentTokens tokens,
    Guid machineId,
    Guid runId,
    Func<CancellationToken, Task> saveRunToken,
    TimeSpan interval,
    TimeProvider timeProvider) : IProgress<StepPercent>
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);

    // The server takes 60 calls a minute from a machine, and a beat is two: its report and a batch of log lines. Quick
    // steps change the run many times a second, so the changes that follow a beat this closely share the next one.
    public static readonly TimeSpan MinimumSpacing = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan WaitingInterval = TimeSpan.FromSeconds(5);

    // What a report holds of the variables at most, so a sequence that sets many long ones cannot make it too large.
    public const int MaxReportedVariables = 64;

    public const int MaxReportedValueLength = 1024;

    // What the server may ask for, so a mistake neither floods it nor leaves the run waiting unseen.
    private static readonly TimeSpan s_shortestWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_longestWait = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _sender = new(1, 1);
    private readonly Lock _lock = new();
    private readonly TaskCompletionSource<IReadOnlyDictionary<string, string>> _valuesArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SequenceState? _state;
    private SequenceDefinition? _definition;
    private HashSet<Guid> _containers = [];
    private Guid? _stepId;
    private int _percent;
    private bool _percentKnown;
    private RunActivity _activity = RunActivity.Preparing;
    private string? _pauseMessage;
    private IReadOnlyDictionary<string, string>? _reportedVariables;
    private TimeSpan? _reportAfter;
    private (Guid StepId, int Pass)? _continued;
    private (Guid StepId, int Pass, TaskCompletionSource Continued)? _continueWaiter;
    private IReadOnlyDictionary<string, string>? _values;
    private CancellationTokenSource? _stop;
    private CancellationTokenSource? _run;
    private Task? _loop;

    // After every change of the state, the running step's percent or the activity, on the thread that made it, for the
    // console at the machine, which reads Position then.
    public event Action? Changed;

    // Why the heartbeat ended the run: AgentTokenRejectedException, also for a step's own call, or a
    // DeploymentStepException carrying the server's refusal. Null while it runs or when it was stopped.
    public Exception? Failure { get; private set; }

    // The run as it stands, with every step. Percent is null until the running step has said how far it is.
    public (SequenceState? State, Guid? StepId, int? Percent, RunActivity Activity) Position
    {
        get
        {
            lock (_lock)
            {
                return (_state, _stepId, _percentKnown ? _percent : null, _activity);
            }
        }
    }

    public RunActivity Activity
    {
        get
        {
            lock (_lock)
            {
                return _activity;
            }
        }

        set
        {
            TaskCompletionSource? changed = null;

            lock (_lock)
            {
                if (value != _activity)
                {
                    _activity = value;
                    changed = _changed;
                }
            }

            if (changed is not null)
            {
                changed.TrySetResult();
                Changed?.Invoke();
            }
        }
    }

    // Every state the run saves, which the store passes on once it is written. In a tree the groups, IFs and repeats
    // around the step that runs are Running too, and come before it; the current step is the leaf.
    public void Update(SequenceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        TaskCompletionSource changed;

        lock (_lock)
        {
            HashSet<Guid> containers = ContainersOf(state.Definition);
            Guid? running = state.Steps.FirstOrDefault(step => step.State == StepState.Running && !containers.Contains(step.StepId))?.StepId;

            if (running != _stepId)
            {
                _stepId = running;
                _percent = 0;
                _percentKnown = false;
            }

            _state = state;
            changed = _changed;
        }

        changed.TrySetResult();
        Changed?.Invoke();
    }

    // The running step's percent, which waits for the next beat.
    public void Report(StepPercent value)
    {
        ArgumentNullException.ThrowIfNull(value);

        bool changed = false;

        lock (_lock)
        {
            int percent = Math.Clamp(value.Percent, 0, 100);

            if (value.StepId == _stepId && (percent != _percent || !_percentKnown))
            {
                _percent = percent;
                _percentKnown = true;
                changed = true;
            }
        }

        // Only a new percent: a download reports far more often than its percent moves.
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    // The run as it stands, as a report in the given state: every step that has left Pending, the running one and its
    // percent. A failed report names the step that was running as the one that failed. The variables come along only
    // when they changed since the last report the server took, and the pause's message while the run is paused.
    public AgentRunReport Snapshot(DeploymentState state, string? error = null)
    {
        lock (_lock)
        {
            IEnumerable<StepRunState> steps = (_state?.Steps ?? []).Where(step => step.State != StepState.Pending);

            if (error is not null)
            {
                steps = steps.Select(step => step.State == StepState.Running ? step with { State = StepState.Failed, Error = error } : step);
            }

            IReadOnlyDictionary<string, string> variables = ReportedVariables(_state);

            return new AgentRunReport(state, _state?.Phase ?? SequencePhase.WindowsPE, [.. steps], _stepId, _percent, _activity, error)
            {
                Variables = Same(variables, _reportedVariables ?? new Dictionary<string, string>()) ? null : variables,
                PauseMessage = _activity == RunActivity.Paused ? _pauseMessage : null,
            };
        }
    }

    // A Pause step waits, with its message worked out. The activity tells the server and the console at once.
    public void Pause(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_lock)
        {
            _pauseMessage = message;
        }

        Activity = RunActivity.Paused;
    }

    // The pause is over, and the run goes on with its steps.
    public void Resume()
    {
        Activity = RunActivity.Step;

        lock (_lock)
        {
            _pauseMessage = null;
        }
    }

    // Completes once a report's answer says that someone continued this visit of the Pause step on the web, which may
    // have happened before the wait began, as the answer to the pause's first report.
    public Task WaitForContinueAsync(Guid stepId, int pass, CancellationToken cancellationToken)
    {
        TaskCompletionSource continued = new(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_lock)
        {
            if (_continued == (stepId, pass))
            {
                return Task.CompletedTask;
            }

            _continueWaiter = (stepId, pass, continued);
        }

        return continued.Task.WaitAsync(cancellationToken);
    }

    // The run's values as a report's answer brought them: the one that started the run, or the first after the inputs
    // it waited for at its start were answered. Null before any did.
    public IReadOnlyDictionary<string, string>? Values
    {
        get
        {
            lock (_lock)
            {
                return _values;
            }
        }
    }

    // Completes with the run's values once a report's answer brings them, which it does once the inputs the run waited
    // for at its start are answered, on the web or at the machine.
    public Task<IReadOnlyDictionary<string, string>> WaitForValuesAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return _values is { } values ? Task.FromResult(values) : _valuesArrived.Task.WaitAsync(cancellationToken);
        }
    }

    // Beats until stopped. A 401 or a refused report cancels the run's steps through run.
    public void Start(CancellationTokenSource run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        _run = run;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = BeatAsync(run, _stop.Token, cancellationToken);
    }

    // A step's own call, such as the one for the answer file, was refused with a 401: the run ends as it does when a
    // beat is refused.
    public void TokenRejected(AgentTokenRejectedException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        Failure ??= exception;
        _run?.Cancel();
    }

    // Waits for a beat that is on its way, so nothing else reaches the server after it.
    public async Task StopAsync()
    {
        if (_stop is null || _loop is null)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
        _stop.Dispose();
        _stop = null;
        _loop = null;
    }

    public async Task<AgentRunReportResult> ReportAsync(AgentRunReport report, CancellationToken cancellationToken)
    {
        await _sender.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            AgentRunReportResult result = await server.ReportRunAsync(machineId, tokens.Token, runId, report, cancellationToken)
                .ConfigureAwait(false);

            tokens.Update(result.Token, result.ResumeToken, result.RunToken);
            Took(report, result);

            // Once a run is over, its files are gone from the disk and must stay gone.
            if (result.RunToken is not null && report.State == DeploymentState.Running)
            {
                await SaveRunTokenAsync().ConfigureAwait(false);
            }

            return result;
        }
        finally
        {
            _sender.Release();
        }
    }

    // A report of the run as it stands, at once: the server hands out a step's secrets only once it knows the step
    // runs. A refusal fails the step.
    public Task ReportNowAsync(CancellationToken cancellationToken) =>
        ServerCallRules.CallAsync(
            call => ReportAsync(Snapshot(DeploymentState.Running), call),
            "the progress report",
            log,
            timeProvider,
            cancellationToken);

    // Sends one batch of at most AgentLimits.MaxLinesPerBatch lines.
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _sender.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await log.FlushAsync(server, machineId, tokens.Token, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sender.Release();
        }
    }

    // What a report the server took says back: how soon it wants the next one while the run waits, a pause continued on
    // the web, and the run's values once the inputs it waited for are answered.
    private void Took(AgentRunReport report, AgentRunReportResult result)
    {
        TaskCompletionSource? continued = null;
        TaskCompletionSource<IReadOnlyDictionary<string, string>>? valuesArrived = null;

        lock (_lock)
        {
            if (report.Variables is { } variables)
            {
                _reportedVariables = variables;
            }

            _reportAfter = result.ReportAfterSeconds is { } seconds
                ? TimeSpan.FromSeconds(Math.Clamp(seconds, s_shortestWait.TotalSeconds, s_longestWait.TotalSeconds))
                : null;

            if (result.ContinueStepId is { } stepId && result.ContinuePass is { } pass)
            {
                _continued = (stepId, pass);

                if (_continueWaiter is { } waiter && waiter.StepId == stepId && waiter.Pass == pass)
                {
                    continued = waiter.Continued;
                    _continueWaiter = null;
                }
            }

            if (result.Values is { } values && _values is null)
            {
                _values = values;
                valuesArrived = _valuesArrived;
            }
        }

        continued?.TrySetResult();
        valuesArrived?.TrySetResult(result.Values!);
    }

    // The variables of the sequence as steps set them, in order of their names, never the agent's own, at most
    // MaxReportedVariables of them and each cut to MaxReportedValueLength.
    private static IReadOnlyDictionary<string, string> ReportedVariables(SequenceState? state)
    {
        Dictionary<string, string> variables = new(StringComparer.Ordinal);

        foreach ((string name, string value) in (state?.Variables ?? new Dictionary<string, string>())
            .Where(pair => !RunVariables.IsOwn(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(MaxReportedVariables))
        {
            variables[name] = value.Length > MaxReportedValueLength ? value[..MaxReportedValueLength] : value;
        }

        return variables;
    }

    private static bool Same(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second) =>
        first.Count == second.Count
        && first.All(pair => second.TryGetValue(pair.Key, out string? value) && string.Equals(value, pair.Value, StringComparison.Ordinal));

    // How long a beat waits for the next when nothing changes: while someone is to answer, as often as the server asks.
    private TimeSpan Interval()
    {
        lock (_lock)
        {
            return _activity is RunActivity.Paused or RunActivity.WaitingForInput ? _reportAfter ?? WaitingInterval : interval;
        }
    }

    // Under the lock. The definition of a run never changes, so its containers are worked out once.
    private HashSet<Guid> ContainersOf(SequenceDefinition definition)
    {
        if (!ReferenceEquals(definition, _definition))
        {
            _definition = definition;
            _containers = [.. SequenceTree.Nodes(definition).Where(node => node.IsContainer).Select(node => node.Id)];
        }

        return _containers;
    }

    // The token on the disk only matters after a restart, and the next save writes it again, so a failure here does
    // not end the run.
    private async Task SaveRunTokenAsync()
    {
        try
        {
            await saveRunToken(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"The new run token could not be written to the disk ({exception.Message}). The next step's save tries again.");
        }
    }

    private async Task BeatAsync(CancellationTokenSource run, CancellationToken stop, CancellationToken cancellationToken)
    {
        // Continue on the thread pool, so Start returns at once.
        await Task.Yield();

        bool warned = false;
        long? lastBeat = null;

        while (!stop.IsCancellationRequested)
        {
            if (!await WaitForBeatAsync(stop).ConfigureAwait(false))
            {
                return;
            }

            if (lastBeat is { } last && !await DelayAsync(MinimumSpacing - timeProvider.GetElapsedTime(last), stop).ConfigureAwait(false))
            {
                return;
            }

            lastBeat = timeProvider.GetTimestamp();

            // Renewed before the snapshot: a change in between beats once more rather than never.
            lock (_lock)
            {
                _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            AgentRunReport report = Snapshot(DeploymentState.Running);

            // A beat that has started finishes even when the heartbeat is stopped meanwhile: the request is bounded
            // by the client's timeout, and cancelling it could lose a report the server already stored.
            try
            {
                await ReportAsync(report, cancellationToken).ConfigureAwait(false);
                await FlushAsync(cancellationToken).ConfigureAwait(false);
                warned = false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (AgentTokenRejectedException exception)
            {
                Failure = exception;
                await run.CancelAsync().ConfigureAwait(false);

                return;
            }
            catch (Exception exception) when (ServerCallRules.IsRefusal(exception))
            {
                Failure = new DeploymentStepException(ServerCallRules.Reason(exception, "the progress report"), exception);
                await run.CancelAsync().ConfigureAwait(false);

                return;
            }
            catch (Exception exception) when (ServerCallRules.IsTransient(exception, cancellationToken))
            {
                // Once per outage: a warning per beat would fill the queue the outage keeps from draining.
                if (!warned)
                {
                    warned = true;
                    log.Warning($"Cannot report progress to the server ({exception.Message}). The run goes on.");
                }
            }
            catch (Exception exception)
            {
                // Whatever it is, it must reach the runner as a failed run rather than end the agent.
                Failure = new DeploymentStepException($"Progress could not be reported: {exception.Message}", exception);
                await run.CancelAsync().ConfigureAwait(false);

                return;
            }
        }
    }

    // False once stopped.
    private async Task<bool> DelayAsync(TimeSpan delay, CancellationToken stop)
    {
        if (delay <= TimeSpan.Zero)
        {
            return true;
        }

        try
        {
            await Task.Delay(delay, timeProvider, stop).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    // True when a beat is due: the interval passed or the run changed. False once stopped.
    private async Task<bool> WaitForBeatAsync(CancellationToken stop)
    {
        Task changed;

        lock (_lock)
        {
            changed = _changed.Task;
        }

        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task elapsed = Task.Delay(Interval(), timeProvider, waiting.Token);
        await Task.WhenAny(changed, elapsed).ConfigureAwait(false);
        await waiting.CancelAsync().ConfigureAwait(false);

        return !stop.IsCancellationRequested;
    }
}
