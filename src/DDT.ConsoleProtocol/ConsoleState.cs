// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Everything the console shows besides the log and the open question. Stage is what the agent does now. MachineId is the
// machine's id on the server once it registered, and SignedInBy names who signed in at the machine while an operator
// still has to approve it on the Machines page. Run is the run going on, or the last one, until the next one starts.
// Restart says why the machine restarts while Stage is Restarting. Problem is the last thing that went wrong and what
// can be done about it, until a run starts. Language is the language the server has the console speak, en or de, once
// the agent has registered; null leaves it to Windows. Logo is the path of a PNG the console shows at the right end of
// its top bar, which is dark in both themes, once the agent has registered and downloaded it; null shows none.
public sealed record ConsoleState(
    ConsoleStage Stage,
    string AgentVersion,
    bool DryRun,
    ConsoleServer Server,
    ConsoleMachine Machine,
    Guid? MachineId,
    string? SignedInBy,
    ConsoleRun? Run,
    ConsoleRestart? Restart,
    ConsoleProblem? Problem,
    string? Language = null,
    string? Logo = null);
