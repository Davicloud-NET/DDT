// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// One run of a sequence in one phase, shared by the runner's parts. It ends with that phase.
internal sealed class SequenceRun
{
    public required RunRequest Request { get; init; }

    // Where the run continues: in WinPE, or in the installed Windows.
    public required SequencePhase Phase { get; init; }

    // Called as soon as a restart back into the current phase is due, before the server hears about it.
    public required Action RecordRestart { get; init; }

    public required RunSession Session { get; init; }

    public required FileRunStateStore Store { get; init; }

    public required RunHeartbeat Heartbeat { get; init; }

    public required SequenceState State { get; set; }

    public required MachineVariables Machine { get; set; }

    // Whether this run put its boot entry first. A run that doesn't finish puts the old boot order back.
    public bool WindowsFirst { get; set; }

    public LocalRun? Resumed => Request.Resumed;

    public bool InWindows => Phase == SequencePhase.Windows;

    // A restart the run asks for leads back into the current phase.
    public RestartInto SamePhase => InWindows ? RestartInto.Windows : RestartInto.WindowsPE;

    // Only a fresh run waits for its inputs. A run that continues after a restart already got its values.
    public bool WaitsForInputs => Resumed is null && Session.Run.PendingInputs is { Count: > 0 };
}
