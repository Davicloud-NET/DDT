// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.ViewModels;

// A module of the sequence rail. AwaitsSomeone marks the current step while the run waits at a Pause step or for
// answers, which is not running although it is current.
public sealed record RailStep(string Number, string Name, ConsoleStepState State, int? Percent, string Description, bool AwaitsSomeone = false)
{
    public bool IsRunning => State == ConsoleStepState.Running && !AwaitsSomeone;

    public bool IsWaiting => State == ConsoleStepState.Pending;
}
