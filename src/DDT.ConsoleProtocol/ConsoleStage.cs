// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

public enum ConsoleStage
{
    // The agent has started and checks for a newer agent.
    Starting,

    // The agent registers with the server.
    Connecting,

    // Registered, until someone signs in at the machine or an operator approves it on the Machines page.
    WaitingForAuthorization,

    // Authorized, until a sequence is assigned on the Machines page.
    WaitingForSequence,

    // Authorized, and the person at the machine can choose a sequence.
    Choosing,

    // A run goes on.
    Running,

    // The machine restarts.
    Restarting,

    // The run is done, and the agent sends its last lines and reports.
    Finished,

    // The last run failed, and the machine waits for the next one.
    Failed,

    // The agent has stopped for good; nothing more comes.
    Stopped,
}
