// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

public enum RunOutcome
{
    // The run is over and the machine restarts into what it installed.
    Finished,

    // The machine restarts in the middle of the run. The run continues from its state on disk.
    Restarting,

    // The run failed or couldn't start. The machine keeps polling.
    Failed,

    // The server refused the machine's token, so the agent registers again, with the run token when there is one.
    TokenRejected,

    // The agent was asked to stop. The run's state stays on the disk.
    Stopped,
}
