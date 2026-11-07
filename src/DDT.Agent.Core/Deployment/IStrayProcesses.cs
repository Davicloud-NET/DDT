// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// What a tool started and left outside its own process tree, where the system lets the agent find it: a process whose
// parent has ended, or one in a session of its own.
public interface IStrayProcesses
{
    // Called once the tool with this process id has started. The answer ends what the tool started and its tree no
    // longer holds, and is called after the tree itself was ended.
    Action WatchFrom(int processId);
}
