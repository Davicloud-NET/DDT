// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// The only sender of a run's reports and log batches, so none overlap or arrive out of order. It beats every interval
// and when the run's state or activity changes; each report renews the session's tokens, and saveRunToken writes a new
// run token to the disk. While someone is to answer, it beats as often as the server asks, as answers come with the
// reports.
public sealed class RunHeartbeat(
    IAgentServer server,
    AgentLog log,
    RunSession session,
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

    private readonly SemaphoreSlim _sender = new(1, 1);
    private readonly RunPosition _position = new();
    private readonly RunAnswers _answers = new();
    private CancellationTokenSource? _stop;
    private CancellationTokenSource? _run;
    private Task? _loop;

    // After every change of the state, the running step's percent or the activity, on the thread that made it, for the
    // console at the machine, which reads Position then.
    public event Action? Changed
    {
        add => _position.Changed += value;
        remove => _position.Changed -= value;
    }

    // Why the heartbeat ended the run: AgentTokenRejectedException, also for a step's own call, or a
    // DeploymentStepException carrying the server's refusal. Null while it runs or when it was stopped.
    public Exception? Failure { get; private set; }

    // Percent is null until the running step has said how far it is.
    public (SequenceState? State, Guid? StepId, int? Percent, RunActivity Activity) Position => _position.Current;

    public RunActivity Activity
    {
        get => _position.Activity;
        set => _position.Activity = value;
    }

    // The run's values as a report's answer brought them: the one that started the run, or the first after its inputs
    // were answered. Null before any did.
    public IReadOnlyDictionary<string, string>? Values => _answers.Values;

    // Every state the run saves, which the store passes on once it is written.
    public void Update(SequenceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _position.Update(state);
    }

    public void Report(StepPercent value)
    {
        ArgumentNullException.ThrowIfNull(value);

        _position.Report(value);
    }

    // Every step that has left Pending, and the running one's percent.
    public AgentRunReport Snapshot(DeploymentState state, string? error = null) => _position.Snapshot(state, error);

    // A Pause step waits, with its message worked out. The activity tells the server and the console at once.
    public void Pause(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _position.Pause(message);
    }

    public void Resume() => _position.Resume();

    // Completes once a report's answer says someone continued this visit of the Pause step on the web.
    public Task WaitForContinueAsync(Guid stepId, int pass, CancellationToken cancellationToken) =>
        _answers.WaitForContinueAsync(stepId, pass, cancellationToken);

    // Completes with the run's values once a report's answer brings them, after the inputs are answered.
    public Task<IReadOnlyDictionary<string, string>> WaitForValuesAsync(CancellationToken cancellationToken) =>
        _answers.WaitForValuesAsync(cancellationToken);

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
            AgentRunReportResult result = await server.ReportRunAsync(session.MachineId, session.Tokens.Token, session.Run.Id, report, cancellationToken)
                .ConfigureAwait(false);

            session.Tokens.Update(result.Token, result.ResumeToken, result.RunToken);
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

    // The server hands out a step's secrets only once it knows the step runs. A refusal fails the step.
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
            await log.FlushAsync(server, session.MachineId, session.Tokens.Token, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sender.Release();
        }
    }

    private void Took(AgentRunReport report, AgentRunReportResult result)
    {
        if (report.Variables is { } variables)
        {
            _position.Took(variables);
        }

        _answers.Took(result);
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

    // How long a beat waits for the next when nothing changes: while someone is to answer, as often as the server asks.
    private TimeSpan Interval() =>
        _position.Activity is RunActivity.Paused or RunActivity.WaitingForInput ? _answers.ReportAfter ?? WaitingInterval : interval;

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
            _position.RenewChange();

            if (await BeatOnceAsync(Snapshot(DeploymentState.Running), warned, cancellationToken).ConfigureAwait(false) is not { } outage)
            {
                await run.CancelAsync().ConfigureAwait(false);

                return;
            }

            warned = outage;

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    // A beat that has started finishes even when the heartbeat is stopped meanwhile: the request is bounded by the
    // client's timeout, and cancelling it could lose a report the server already stored. Returns whether an outage was
    // warned of, or null once Failure ends the run.
    private async Task<bool?> BeatOnceAsync(AgentRunReport report, bool warned, CancellationToken cancellationToken)
    {
        try
        {
            await ReportAsync(report, cancellationToken).ConfigureAwait(false);
            await FlushAsync(cancellationToken).ConfigureAwait(false);

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return warned;
        }
        catch (AgentTokenRejectedException exception)
        {
            Failure = exception;

            return null;
        }
        catch (Exception exception) when (ServerCallRules.IsRefusal(exception))
        {
            Failure = new DeploymentStepException(ServerCallRules.Reason(exception, "the progress report"), exception);

            return null;
        }
        catch (Exception exception) when (ServerCallRules.IsTransient(exception, cancellationToken))
        {
            // Once per outage: a warning per beat would fill the queue the outage keeps from draining.
            if (!warned)
            {
                log.Warning($"Cannot report progress to the server ({exception.Message}). The run goes on.");
            }

            return true;
        }
        catch (Exception exception)
        {
            // Whatever it is, it must reach the runner as a failed run rather than end the agent.
            Failure = new DeploymentStepException($"Progress could not be reported: {exception.Message}", exception);

            return null;
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
        Task changed = _position.NextChange;

        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task elapsed = Task.Delay(Interval(), timeProvider, waiting.Token);
        await Task.WhenAny(changed, elapsed).ConfigureAwait(false);
        await waiting.CancelAsync().ConfigureAwait(false);

        return !stop.IsCancellationRequested;
    }
}
