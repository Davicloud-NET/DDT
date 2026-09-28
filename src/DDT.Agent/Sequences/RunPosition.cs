// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Where a run stands, which the heartbeat reports and the console shows: its state, the running step and its percent,
// what the run is doing, and a pause's message. A change of state or activity completes NextChange, which a beat waits
// for; a new percent waits for the next beat.
internal sealed class RunPosition
{
    private readonly Lock _lock = new();
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

    // After every change, on the thread that made it.
    public event Action? Changed;

    public (SequenceState? State, Guid? StepId, int? Percent, RunActivity Activity) Current
    {
        get
        {
            lock (_lock)
            {
                return (_state, _stepId, _percentKnown ? _percent : null, _activity);
            }
        }
    }

    public Task NextChange
    {
        get
        {
            lock (_lock)
            {
                return _changed.Task;
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

    // In a tree the groups, IFs and repeats around the running step are Running too; the current step is the leaf.
    public void Update(SequenceState state)
    {
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

    // Only a new percent counts as a change, as a download reports far more often than its percent moves. It waits for
    // the next beat.
    public void Report(StepPercent value)
    {
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

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public void Pause(string message)
    {
        lock (_lock)
        {
            _pauseMessage = message;
        }

        Activity = RunActivity.Paused;
    }

    public void Resume()
    {
        Activity = RunActivity.Step;

        lock (_lock)
        {
            _pauseMessage = null;
        }
    }

    // Before a beat takes its snapshot, so a change in between beats once more rather than never.
    public void RenewChange()
    {
        lock (_lock)
        {
            _changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    // A failed report names the step that was running as the one that failed. The variables come along only when they
    // changed since the server took them last.
    public AgentRunReport Snapshot(DeploymentState state, string? error)
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

    // The variables of a report the server took.
    public void Took(IReadOnlyDictionary<string, string> variables)
    {
        lock (_lock)
        {
            _reportedVariables = variables;
        }
    }

    // In order of their names, never the agent's own, and cut to what a report may hold, so a sequence that sets many
    // long ones cannot make it too large.
    private static IReadOnlyDictionary<string, string> ReportedVariables(SequenceState? state)
    {
        Dictionary<string, string> variables = new(StringComparer.Ordinal);

        foreach ((string name, string value) in (state?.Variables ?? new Dictionary<string, string>())
            .Where(pair => !RunVariables.IsOwn(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(RunHeartbeat.MaxReportedVariables))
        {
            variables[name] = value.Length > RunHeartbeat.MaxReportedValueLength ? value[..RunHeartbeat.MaxReportedValueLength] : value;
        }

        return variables;
    }

    private static bool Same(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second) =>
        first.Count == second.Count
        && first.All(pair => second.TryGetValue(pair.Key, out string? value) && string.Equals(value, pair.Value, StringComparison.Ordinal));

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
}
