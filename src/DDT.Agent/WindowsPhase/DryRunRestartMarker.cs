// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

// A dry run restarts by ending its process, or by starting over within it, so a restart it asked for always happened.
public sealed class DryRunRestartMarker(AgentLog log) : IRestartMarker
{
    public bool IsSet => false;

    public void Set() =>
        log.Information($@"Dry run: in Windows the due restart would be recorded in the volatile key HKLM\{VolatileRestartMarker.KeyPath}.");
}
