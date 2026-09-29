// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

// Marks nothing, because a mark would delete the path when this computer next starts.
public sealed class DryRunRestartDeleter(AgentLog log) : IRestartDeleter
{
    public void DeleteAtRestart(string path) =>
        log.Information($"Dry run: in Windows, {path} would be marked for deletion when Windows next starts.");
}
