// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// Shown while the boot environment counts down to the restart into the installed Linux. The agent withdraws it when
// the time is up.
public sealed class StartOverOfferViewModel(Localizer localizer, int id, StartOverOfferQuestion question, Action<int, ConsoleAnswer> answer)
    : QuestionViewModel(localizer, id, answer)
{
    public StartOverOfferQuestion Question { get; } = question ?? throw new ArgumentNullException(nameof(question));

    public string Title => T("Restarting into the installed Linux");

    public string Text => F("In {seconds} seconds. To end the run and start over here, stop the restart.", ("seconds", L.Number(Question.Seconds)));

    public string SubmitLabel => T("Stop the restart");

    public override bool CanSubmit => true;

    protected override ConsoleAnswer? Answer() => new(Continue: true);
}
