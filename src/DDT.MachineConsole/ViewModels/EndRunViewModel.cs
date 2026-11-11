// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// Confirms ending a run that went to the installed Linux. Back restarts into it.
public sealed class EndRunViewModel : TypedWordViewModel
{
    public EndRunViewModel(Localizer localizer, int id, EndRunQuestion question, Action<int, ConsoleAnswer> answer)
        : base(localizer, id, question?.Word ?? throw new ArgumentNullException(nameof(question)), answer)
    {
        Question = question;
    }

    public EndRunQuestion Question { get; }

    public string Title => T("End the run?");

    public Tag Tag => Tag.Of(T("Ends the run"), TagTone.Fail);

    public string Warning => Question.Taken
        ? T("The run goes on in the installed Linux. Ending it means authorizing this machine again.")
        : T("The installed Linux has not taken the run over. Ending it means authorizing this machine again.");

    public string Label => F("Type {word} to end the run", ("word", Word));

    public string SubmitLabel => T("End the run");

    public string BackLabel => T("Back, restart into Linux");
}
