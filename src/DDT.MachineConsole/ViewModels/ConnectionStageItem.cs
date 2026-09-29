// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.ViewModels;

public sealed record ConnectionStageItem(string Name, ConnectionStageState State, string StateText)
{
    public bool IsFailed => State == ConnectionStageState.Failed;

    // The stages show as modules of the rail: passed as done, the failed one hatched, the rest empty.
    public ConsoleStepState ModuleState => State switch
    {
        ConnectionStageState.Passed => ConsoleStepState.Done,
        ConnectionStageState.Failed => ConsoleStepState.Failed,
        _ => ConsoleStepState.Pending,
    };
}
