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
// interval, and at once when the run's state or activity changes, it reports a snapshot of the run and sends one log
// batch. Each report brings fresh tokens, which keeps the session alive through a download of any length, and the
// run token, which saveRunToken writes to the disk when it is a new one.
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

    private readonly SemaphoreSlim _sender = new(1, 1);
    private readonly Lock _lock = new();
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SequenceState? _state;
    private Guid? _stepId;
    private int _percent;
    private RunActivity _activity = RunActivity.Preparing;
    private CancellationTokenSource? _stop;
    private CancellationTokenSource? _run;
    private Task? _loop;

    // Why the heartbeat ended the run: AgentTokenRejectedException, also for a step's own call, or a
    // DeploymentStepException carrying the server's refusal. Null while it runs or when it was stopped.
    public Exception? Failure { get; private set; }

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

            changed?.TrySetResult();
        }
    }

    // Every state the run saves, which the store passes on once it is written.
    public void Update(SequenceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        TaskCompletionSource changed;

        lock (_lock)
        {
            Guid? running = state.Steps.FirstOrDefault(step => step.State == StepState.Running)?.StepId;

            if (running != _stepId)
            {
                _stepId = running;
                _percent = 0;
            }

            _state = state;
            changed = _changed;
        }

        changed.TrySetResult();
    }

    // The running step's percent, which waits for the next beat.
    public void Report(StepPercent value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_lock)
        {
            if (value.StepId == _stepId)
            {
                _percent = Math.Clamp(value.Percent, 0, 100);
            }
        }
    }

    // The run as it stands, as a report in the given state: every step that has left Pending, the running one and its
    // percent. A failed report names the step that was running as the one that failed.
    public AgentRunReport Snapshot(DeploymentState state, string? error = null)
    {
        lock (_lock)
        {
            IEnumerable<StepRunState> steps = (_state?.Steps ?? []).Where(step => step.State != StepState.Pending);

            if (error is not null)
            {
                steps = steps.Select(step => step.State == StepState.Running ? step with { State = StepState.Failed, Error = error } : step);
            }

            return new AgentRunReport(state, _state?.Phase ?? SequencePhase.WindowsPE, [.. steps], _stepId, _percent, _activity, error);
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

        while (!stop.IsCancellationRequested)
        {
            if (!await WaitForBeatAsync(stop).ConfigureAwait(false))
            {
                return;
            }

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

    // True when a beat is due: the interval passed or the run changed. False once stopped.
    private async Task<bool> WaitForBeatAsync(CancellationToken stop)
    {
        Task changed;

        lock (_lock)
        {
            changed = _changed.Task;
        }

        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task elapsed = Task.Delay(interval, timeProvider, waiting.Token);
        await Task.WhenAny(changed, elapsed).ConfigureAwait(false);
        await waiting.CancelAsync().ConfigureAwait(false);

        return !stop.IsCancellationRequested;
    }
}
