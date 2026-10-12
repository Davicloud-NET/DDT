// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Stops a tool that was watched from its start, with what it started. The agent ends the tool's own tree in between.
public interface IToolStop
{
    // Before the tree is ended: asks the tool and all it started to end, and gives them a moment to. A script's own
    // clean-up runs then. tool is its name for the log.
    Task AskAsync(string tool);

    // After the tree is ended: ends what the tool started and its tree no longer holds.
    Task EndAsync();
}
