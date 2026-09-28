// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Sequences;

// What the server's answers to a run's reports bring: how soon it wants the next report while the run waits, a pause
// continued on the web, and the run's values once the inputs it waited for are answered.
internal sealed class RunAnswers
{
    // What the server may ask for, so a mistake neither floods it nor leaves the run waiting unseen.
    private static readonly TimeSpan s_shortestWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_longestWait = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();
    private readonly TaskCompletionSource<IReadOnlyDictionary<string, string>> _valuesArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TimeSpan? _reportAfter;
    private (Guid StepId, int Pass)? _continued;
    private (Guid StepId, int Pass, TaskCompletionSource Continued)? _continueWaiter;
    private IReadOnlyDictionary<string, string>? _values;

    // Null unless the last answer asked for the next report sooner or later than usual.
    public TimeSpan? ReportAfter
    {
        get
        {
            lock (_lock)
            {
                return _reportAfter;
            }
        }
    }

    // The one that started the run, or the first after its inputs were answered. Null before any did.
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

    // The continue may have come already, with the answer to the pause's first report.
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

    public Task<IReadOnlyDictionary<string, string>> WaitForValuesAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return _values is { } values ? Task.FromResult(values) : _valuesArrived.Task.WaitAsync(cancellationToken);
        }
    }

    public void Took(AgentRunReportResult result)
    {
        TaskCompletionSource? continued = null;
        IReadOnlyDictionary<string, string>? arrived = null;

        lock (_lock)
        {
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
                arrived = values;
            }
        }

        continued?.TrySetResult();

        if (arrived is not null)
        {
            _valuesArrived.TrySetResult(arrived);
        }
    }
}
