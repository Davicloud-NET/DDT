// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// What stops a tool with everything it started, where the system lets the agent find that: the tool's own tree, a
// process whose parent has ended, and one in a session of its own.
public interface IStrayProcesses
{
    // Called once the tool with this process id has started. The answer is called when the tool has to be stopped.
    IToolStop WatchFrom(int processId);
}
