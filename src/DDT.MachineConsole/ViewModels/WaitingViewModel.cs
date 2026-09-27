// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// Authorized and waiting: for a sequence someone assigns on the Machines page, or, while the person here chooses, for
// the agent's next question.
public sealed class WaitingViewModel(Localizer localizer) : StageViewModel(localizer)
{
    private ConsoleState? _state;

    public bool IsChoosing => _state?.Stage == ConsoleStage.Choosing;

    public string Title => IsChoosing ? T("One moment") : T("Waiting for a task sequence");

    public Tag Tag => IsChoosing ? Tag.Of(T("Choosing"), TagTone.Run) : Tag.Of(T("Authorized"), TagTone.Ok);

    public string Intro => IsChoosing
        ? T("The agent gets what this machine can run. The choices appear here in a moment.")
        : T("This machine is authorized. Assign a task sequence to it on the Machines page, and it starts by itself.");

    public string FindLabel => T("Find it on the Machines page by");

    public IReadOnlyList<Fact> Identity => _state is null ? [] : MachineFacts.Identity(L, _state.Machine);

    public override void Update(ConsoleState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _state = state;
        RaiseAll();
    }
}
