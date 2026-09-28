// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Everything the console shows besides the log and the open question.
public sealed record ConsoleState(
    ConsoleStage Stage,
    string AgentVersion,
    bool DryRun,
    ConsoleServer Server,
    ConsoleMachine Machine,
    // Once the machine has registered with the server.
    Guid? MachineId,
    // Who signed in at the machine while an operator still has to approve it on the Machines page.
    string? SignedInBy,
    // The run going on, or the last one until the next starts.
    ConsoleRun? Run,
    // While Stage is Restarting.
    ConsoleRestart? Restart,
    // The last thing that went wrong, until a run starts.
    ConsoleProblem? Problem,
    // en or de, as the server says once the agent has registered; null leaves it to Windows.
    string? Language = null,
    // A PNG for the right end of the top bar, which is dark in both themes; null shows none.
    string? Logo = null);
