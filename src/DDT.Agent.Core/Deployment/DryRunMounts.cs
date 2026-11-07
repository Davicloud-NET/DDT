// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Logs each mount instead of asking the kernel, and answers like a kernel that did it. So a dry run reaches no server
// with a step's accounts. The options are not logged here either.
public sealed class DryRunMounts(AgentLog log) : IMounts
{
    public int Mount(string source, string target, string fileSystem, string options)
    {
        log.Information($"Dry run: not mounted, and taken as mounted: {source} as {fileSystem} at {target}.");

        return 0;
    }

    public int Unmount(string target, bool detach) => 0;
}
