// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The Running, Restarting, Finished, Failed and Stopped stages. Above the sequence rail it shows the running step, or
// why the run stopped and what to do.
public sealed class RunViewModel(Localizer localizer) : StageViewModel(localizer)
{
    private ConsoleState? _state;

    public ConsoleStage Stage => _state?.Stage ?? ConsoleStage.Running;

    public ConsoleRun? Run => _state?.Run;

    public bool HasRun => Run is not null;

    public string SequenceName => Run?.SequenceName ?? string.Empty;

    public Tag Tag => Stage switch
    {
        ConsoleStage.Running when Run?.Activity == ConsoleActivity.Paused => Tag.Of(T("Paused"), TagTone.Attention),
        ConsoleStage.Running when Run?.Activity == ConsoleActivity.WaitingForInput => Tag.Of(T("Waiting"), TagTone.Attention),
        ConsoleStage.Running => Tag.Of(Say.Stage(L, Stage), TagTone.Run),
        ConsoleStage.Restarting => Tag.Of(Say.Stage(L, Stage), TagTone.Run),
        ConsoleStage.Finished => Tag.Of(T("Done"), TagTone.Ok),
        ConsoleStage.Failed => Tag.Of(Say.Stage(L, Stage), TagTone.Fail),
        _ => Tag.Of(Say.Stage(L, Stage), TagTone.Fail),
    };

    // The steps on the run's path. The rail shows them and the position counts them.
    private IReadOnlyList<ConsoleStep> PathSteps => Run is { } run ? RunPath.Steps(run) : [];

    private int? CurrentIndex => RunPath.IndexOf(PathSteps, Run?.CurrentStepId);

    private ConsoleStep? CurrentStep => Run is { CurrentStepId: { } id } run ? run.Steps.FirstOrDefault(step => step.Id == id) : null;

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

    // The step's name under the heading, when the heading describes what the step's kind does.
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
            IReadOnlyList<ConsoleStep> steps = PathSteps;

            if (Run is not { } run || steps.Count == 0)
            {
                return null;
            }

            if (Stage == ConsoleStage.Running && CurrentIndex is { } index)
            {
                (string, string)[] values = [("number", L.Number(index + 1)), ("count", L.Number(steps.Count))];

                return (steps[index].Phase, RunPath.IsTree(run)) switch
                {
                    (ConsolePhase.WindowsPE, false) => F("Step {number} of {count}, in Windows PE", values),
                    (_, false) => F("Step {number} of {count}, in the installed Windows", values),
                    (ConsolePhase.WindowsPE, true) => F("Step {number} of {count} on this path, in Windows PE", values),
                    _ => F("Step {number} of {count} on this path, in the installed Windows", values),
                };
            }

            if (FailedIndex is { } failed)
            {
                return F(
                    "Failed at step {number} of {count}, {name}",
                    ("number", L.Number(failed + 1)),
                    ("count", L.Number(steps.Count)),
                    ("name", steps[failed].Name));
            }

            int done = steps.Count(step => step.State is ConsoleStepState.Done or ConsoleStepState.Skipped);

            return F("{done} of {count} steps done", ("done", L.Number(done)), ("count", L.Number(steps.Count)));
        }
    }

    public bool HasPosition => !string.IsNullOrEmpty(Position);

    private int? FailedIndex
    {
        get
        {
            IReadOnlyList<ConsoleStep> steps = PathSteps;

            for (int index = 0; index < steps.Count; index++)
            {
                if (steps[index].State == ConsoleStepState.Failed)
                {
                    return index;
                }
            }

            return null;
        }
    }

    // The failed step's error, in the agent's words, unless the problem already says it.
    public string? StepError =>
        FailedIndex is { } index && PathSteps[index].Error is { } error && _state?.Problem?.Reason.Contains(error, StringComparison.Ordinal) != true
            ? error
            : null;

    public bool HasStepError => !string.IsNullOrEmpty(StepError);

    public bool HasProblem => Stage is ConsoleStage.Failed or ConsoleStage.Stopped && _state?.Problem is not null;

    public string ProblemLabel => T("What went wrong");

    // As the agent wrote it, never translated.
    public string Problem => _state?.Problem?.Reason ?? string.Empty;

    public string RemedyLabel => T("What to do");

    public string Remedy => _state?.Problem is { } problem ? Say.Remedy(L, problem.Remedy) : string.Empty;

    // Why nothing runs while the run waits, or otherwise a note to leave the machine on. The restart into Windows is
    // only mentioned while WinPE steps are still to come.
    public string? Note => Stage switch
    {
        ConsoleStage.Running when Run?.Activity == ConsoleActivity.WaitingForInput =>
            T("Nothing runs until the sequence's inputs are answered, here or on the machine's page on the web."),
        ConsoleStage.Running when Run?.Activity == ConsoleActivity.Paused =>
            T("Nothing runs until someone continues the run, here or on the machine's page on the web."),
        ConsoleStage.Running when PathSteps.Any(step => step.Phase == ConsolePhase.Windows)
            && PathSteps.Any(step => step.Phase == ConsolePhase.WindowsPE && step.State is ConsoleStepState.Pending or ConsoleStepState.Running) =>
            T("Leave this machine on. It restarts by itself and finishes in Windows."),
        ConsoleStage.Running => T("Leave this machine on. This screen says when the run is done."),
        ConsoleStage.Restarting => T("Leave this machine on. It starts again by itself."),
        _ => null,
    };

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public IReadOnlyList<RailStep> Steps => Run is { } run ? RunPath.Rail(L, run, PathSteps) : [];

    // The phases the steps run in, shown above the rail when the run has more than one.
    public IReadOnlyList<RailPhase> Phases => RunPath.Phases(L, PathSteps);

    public override void Update(ConsoleState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _state = state;
        RaiseAll();
    }
}
