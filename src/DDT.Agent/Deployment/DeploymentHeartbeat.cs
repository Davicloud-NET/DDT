// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;

namespace DDT.Agent.Deployment;

// The only sender of reports and log batches during a run, so none of them overlap or arrive out of order. Every
// interval, and at once when the step changes, it reports the current step and percent and sends one log batch.
// Each report brings fresh tokens, which keeps the session alive through a download of any length.
public sealed class DeploymentHeartbeat(
    IAgentServer server,
    AgentLog log,
    DeploymentTokens tokens,
    Guid machineId,
    Guid deploymentId,
    TimeSpan interval,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _sender = new(1, 1);
    private readonly Lock _lock = new();
    private TaskCompletionSource _stepChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DeploymentStep _step;
    private int _percent;
    private CancellationTokenSource? _stop;
    private Task? _loop;

    // Why the heartbeat ended the run: AgentTokenRejectedException, or a DeploymentStepException carrying the
    // server's refusal. Null while it runs or when it was stopped.
    public Exception? Failure { get; private set; }

    public DeploymentStep Step
    {
        get
        {
            lock (_lock)
            {
                return _step;
            }
        }
    }

    public int Percent
    {
        get
        {
            lock (_lock)
            {
                return _percent;
            }
        }
    }

    public void Progress(DeploymentStep step, int percent)
    {
        TaskCompletionSource? changed = null;

        lock (_lock)
        {
            if (step != _step)
            {
                changed = _stepChanged;
            }

            _step = step;
            _percent = Math.Clamp(percent, 0, 100);
        }

        changed?.TrySetResult();
    }

    // Beats until stopped. A 401 or a refused report cancels the run's steps through run.
    public void Start(CancellationTokenSource run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);

        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = BeatAsync(run, _stop.Token, cancellationToken);
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

    public async Task<AgentDeploymentReportResult> ReportAsync(AgentDeploymentReport report, CancellationToken cancellationToken)
    {
        await _sender.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            AgentDeploymentReportResult result = await server
                .ReportDeploymentAsync(machineId, tokens.Token, deploymentId, report, cancellationToken)
                .ConfigureAwait(false);

            tokens.Update(result.Token, result.ResumeToken);

            return result;
        }
        finally
        {
            _sender.Release();
        }
    }

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

            DeploymentStep step;
            int percent;

            lock (_lock)
            {
                step = _step;
                percent = _percent;
                _stepChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            // A beat that has started finishes even when the heartbeat is stopped meanwhile: the request is bounded
            // by the client's timeout, and cancelling it could lose a report the server already stored.
            try
            {
                await ReportAsync(new AgentDeploymentReport(DeploymentState.Running, step, percent, null), cancellationToken).ConfigureAwait(false);
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
                    log.Warning($"Cannot report progress to the server ({exception.Message}). The deployment goes on.");
                }
            }
            catch (Exception exception)
            {
                // Whatever it is, it must reach the runner as a failed step rather than end the agent.
                Failure = new DeploymentStepException($"Progress could not be reported: {exception.Message}", exception);
                await run.CancelAsync().ConfigureAwait(false);

                return;
            }
        }
    }

    // True when a beat is due: the interval passed or the step changed. False once stopped.
    private async Task<bool> WaitForBeatAsync(CancellationToken stop)
    {
        Task stepChanged;

        lock (_lock)
        {
            stepChanged = _stepChanged.Task;
        }

        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(stop);
        Task elapsed = Task.Delay(interval, timeProvider, waiting.Token);
        await Task.WhenAny(stepChanged, elapsed).ConfigureAwait(false);
        await waiting.CancelAsync().ConfigureAwait(false);

        return !stop.IsCancellationRequested;
    }
}
