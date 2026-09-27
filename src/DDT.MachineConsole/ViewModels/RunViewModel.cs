// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// A run and how it ended: running, restarting, finished, failed, or the agent stopped. The sequence rail shows every
// step of the run, one module each; above it the step that runs, with its percent where the step says how far it is,
// or why the run stopped and what to do about it.
public sealed class RunViewModel(Localizer localizer) : StageViewModel(localizer)
{
    private ConsoleState? _state;

    public ConsoleStage Stage => _state?.Stage ?? ConsoleStage.Running;

    public ConsoleRun? Run => _state?.Run;

    public bool HasRun => Run is not null;

    public string SequenceName => Run?.SequenceName ?? string.Empty;

    public Tag Tag => Stage switch
    {
        ConsoleStage.Running => Tag.Of(Say.Stage(L, Stage), TagTone.Run),
        ConsoleStage.Restarting => Tag.Of(Say.Stage(L, Stage), TagTone.Run),
        ConsoleStage.Finished => Tag.Of(T("Done"), TagTone.Ok),
        ConsoleStage.Failed => Tag.Of(Say.Stage(L, Stage), TagTone.Fail),
        _ => Tag.Of(Say.Stage(L, Stage), TagTone.Fail),
    };

    private int? CurrentIndex => Run is { CurrentStepId: { } id } run ? IndexOf(run, id) : null;

    private ConsoleStep? CurrentStep => CurrentIndex is { } index ? Run!.Steps[index] : null;

    public bool ShowsPercent => Stage == ConsoleStage.Running && CurrentStep is not null && Run?.Percent is not null;

    public string Percent => Run?.Percent is { } percent ? Math.Clamp(percent, 0, 100).ToString(CultureInfo.InvariantCulture) : string.Empty;

    public string Heading => Stage switch
    {
        ConsoleStage.Running when CurrentStep is { } step => Say.StepAction(L, step.Kind) ?? step.Name,
        ConsoleStage.Running => Run is { } run ? Say.Activity(L, run.Activity) : T("Running"),
        ConsoleStage.Restarting => _state?.Restart is { } restart ? Say.RestartTarget(L, restart.Into) : T("Restarting"),
        ConsoleStage.Finished => T("Finished"),
        ConsoleStage.Failed => T("The run failed"),
        _ => T("The agent has stopped"),
    };

    // The step's own name under the heading, where the heading says what its kind does.
    public string? Detail => Stage switch
    {
        ConsoleStage.Running when CurrentStep is { } step => Say.StepAction(L, step.Kind) is null ? null : step.Name,
        ConsoleStage.Restarting => _state?.Restart is { } restart ? Say.RestartReason(L, restart.Reason) : null,
        ConsoleStage.Finished => T("Every step is done. The agent sends its last lines and reports."),
        _ => null,
    };

    public bool HasDetail => !string.IsNullOrEmpty(Detail);

    public string? Position
    {
        get
        {
            if (Run is not { } run || run.Steps.Count == 0)
            {
                return null;
            }

            if (Stage == ConsoleStage.Running && CurrentIndex is { } index)
            {
                (string, string)[] values = [("number", L.Number(index + 1)), ("count", L.Number(run.Steps.Count))];

                return run.Steps[index].Phase == ConsolePhase.WindowsPE
                    ? F("Step {number} of {count}, in Windows PE", values)
                    : F("Step {number} of {count}, in the installed Windows", values);
            }

            if (FailedIndex is { } failed)
            {
                return F(
                    "Failed at step {number} of {count}, {name}",
                    ("number", L.Number(failed + 1)),
                    ("count", L.Number(run.Steps.Count)),
                    ("name", run.Steps[failed].Name));
            }

            int done = run.Steps.Count(step => step.State is ConsoleStepState.Done or ConsoleStepState.Skipped);

            return F("{done} of {count} steps done", ("done", L.Number(done)), ("count", L.Number(run.Steps.Count)));
        }
    }

    public bool HasPosition => !string.IsNullOrEmpty(Position);

    private int? FailedIndex
    {
        get
        {
            if (Run is not { } run)
            {
                return null;
            }

            for (int index = 0; index < run.Steps.Count; index++)
            {
                if (run.Steps[index].State == ConsoleStepState.Failed)
                {
                    return index;
                }
            }

            return null;
        }
    }

    // The failed step's error, in the agent's words, where the problem does not say it already.
    public string? StepError =>
        FailedIndex is { } index && Run!.Steps[index].Error is { } error && _state?.Problem?.Reason.Contains(error, StringComparison.Ordinal) != true
            ? error
            : null;

    public bool HasStepError => !string.IsNullOrEmpty(StepError);

    public bool HasProblem => Stage is ConsoleStage.Failed or ConsoleStage.Stopped && _state?.Problem is not null;

    public string ProblemLabel => T("What went wrong");

    // The agent's words.
    public string Problem => _state?.Problem?.Reason ?? string.Empty;

    public string RemedyLabel => T("What to do");

    public string Remedy => _state?.Problem is { } problem ? Say.Remedy(L, problem.Remedy) : string.Empty;

    public string? Note => Stage switch
    {
        ConsoleStage.Running when Run?.Steps.Any(step => step.Phase == ConsolePhase.Windows) == true =>
            T("Leave this machine on. It restarts by itself and finishes in Windows."),
        ConsoleStage.Running => T("Leave this machine on. This screen says when the run is done."),
        ConsoleStage.Restarting => T("Leave this machine on. It starts again by itself."),
        _ => null,
    };

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public IReadOnlyList<RailStep> Steps => Run is { } run
        ? [.. run.Steps.Select((step, index) => new RailStep(
            (index + 1).ToString("00", CultureInfo.InvariantCulture),
            step.Name,
            step.State,
            step.Id == run.CurrentStepId ? run.Percent : null,
            F("Step {number}, {name}: {state}", ("number", L.Number(index + 1)), ("name", step.Name), ("state", Say.StepState(L, step.State)))))]
        : [];

    // The phases the steps run in, above the rail, where the run has more than one.
    public IReadOnlyList<RailPhase> Phases
    {
        get
        {
            if (Run is not { } run || run.Steps.Select(step => step.Phase).Distinct().Count() < 2)
            {
                return [];
            }

            List<RailPhase> phases = [];

            foreach (ConsoleStep step in run.Steps)
            {
                if (phases.Count > 0 && phases[^1].Phase == step.Phase)
                {
                    phases[^1] = phases[^1] with { Steps = phases[^1].Steps + 1 };
                }
                else
                {
                    phases.Add(new RailPhase(step.Phase, Say.Phase(L, step.Phase), 1));
                }
            }

            return phases;
        }
    }

    public override void Update(ConsoleState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _state = state;
        RaiseAll();
    }

    private static int? IndexOf(ConsoleRun run, Guid id)
    {
        for (int index = 0; index < run.Steps.Count; index++)
        {
            if (run.Steps[index].Id == id)
            {
                return index;
            }
        }

        return null;
    }
}

// A module of the sequence rail. Percent is set on the running step where it says how far it is.
public sealed record RailStep(string Number, string Name, ConsoleStepState State, int? Percent, string Description)
{
    public bool IsRunning => State == ConsoleStepState.Running;

    public bool IsWaiting => State == ConsoleStepState.Pending;
}

public sealed record RailPhase(ConsolePhase Phase, string Label, int Steps);
