// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// One run of a sequence in one phase, which the runner's parts share and which ends with it.
internal sealed class SequenceRun
{
    public required RunRequest Request { get; init; }

    // Where the run goes on: in Windows PE, or in the installed Windows.
    public required SequencePhase Phase { get; init; }

    // Called as soon as a restart back to where the run is now is due, before the server hears of it.
    public required Action RecordRestart { get; init; }

    public required RunSession Session { get; init; }

    public required FileRunStateStore Store { get; init; }

    public required RunHeartbeat Heartbeat { get; init; }

    public required SequenceState State { get; set; }

    public required MachineVariables Machine { get; set; }

    // Whether this run put a boot entry of its own first, which a run that does not finish puts back.
    public bool WindowsFirst { get; set; }

    public LocalRun? Resumed => Request.Resumed;

    public bool InWindows => Phase == SequencePhase.Windows;

    // A restart the run asks for leads back to where it runs now.
    public RestartInto SamePhase => InWindows ? RestartInto.Windows : RestartInto.WindowsPE;

    // Only a fresh run waits for its inputs: one that goes on after a restart got its values before.
    public bool WaitsForInputs => Resumed is null && Session.Run.PendingInputs is { Count: > 0 };
}
