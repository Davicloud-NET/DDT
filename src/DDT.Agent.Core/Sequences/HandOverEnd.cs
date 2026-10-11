// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// How a run ended where it was, after a hand-over had staged it for the installed system.
public enum HandOverEnd
{
    // The run failed, and the server is told so.
    Failed,

    // The agent was stopped. The server runs the run until it learns of that.
    Stopped,

    // The server no longer takes the machine's token: it ended the run, or the agent's session ran out.
    TokenRejected,
}
