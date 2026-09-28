// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// A Pause step's question, over the run as the run screen shows it. When an operator continues the run on the web, the
// agent withdraws the question.
public sealed class PauseViewModel : QuestionViewModel
{
    private PauseQuestion _question;
    private ConsoleState? _state;

    public PauseViewModel(Localizer localizer, int id, PauseQuestion question, ConsoleState? state, Action<int, ConsoleAnswer> answer)
        : base(localizer, id, answer)
    {
        ArgumentNullException.ThrowIfNull(question);

        _question = question;
        _state = state;
    }

    public PauseQuestion Question => _question;

    public ConsoleRun? Run => _state?.Run;

    public string SequenceName => Run?.SequenceName ?? string.Empty;

    public Tag Tag => Tag.Of(T("Paused"), TagTone.Attention);

    public string Title => _question.StepName;

    // Where on the path the run waits, once the agent's state names the step.
    public string? Position
    {
        get
        {
            if (Run is not { } run)
            {
                return null;
            }

            IReadOnlyList<ConsoleStep> steps = RunPath.Steps(run);

            if (RunPath.IndexOf(steps, run.CurrentStepId) is not { } index)
            {
                return null;
            }

            (string, string)[] values = [("number", L.Number(index + 1)), ("count", L.Number(steps.Count))];

            return steps[index].Phase == ConsolePhase.WindowsPE
                ? F("Pause, step {number} of {count} on this path, in Windows PE", values)
                : F("Pause, step {number} of {count} on this path, in the installed Windows", values);
        }
    }

    public bool HasPosition => Position is not null;

    // The author's words, as the agent filled them in.
    public string Message => _question.Message;

    public bool HasMessage => !string.IsNullOrWhiteSpace(_question.Message);

    public IReadOnlyList<RailStep> Steps => Run is { } run ? RunPath.Rail(L, run, RunPath.Steps(run)) : [];

    public IReadOnlyList<RailPhase> Phases => Run is { } run ? RunPath.Phases(L, RunPath.Steps(run)) : [];

    public bool HasSteps => Steps.Count > 0;

    public string Hint => T("When it is done, press Enter. An operator can also let the run go on from the web.");

    public string SubmitLabel => T("Continue the run");

    public override bool CanSubmit => true;

    public override bool Accept(int id, ConsoleQuestion question)
    {
        if (question is not PauseQuestion next)
        {
            return false;
        }

        _question = next;
        Reopen(id);
        RaiseAll();

        return true;
    }

    public override void Update(ConsoleState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _state = state;
        Raise(nameof(Run));
        Raise(nameof(SequenceName));
        Raise(nameof(Position));
        Raise(nameof(HasPosition));
        Raise(nameof(Steps));
        Raise(nameof(Phases));
        Raise(nameof(HasSteps));
    }

    protected override ConsoleAnswer? Answer() => new(Continue: true);
}
