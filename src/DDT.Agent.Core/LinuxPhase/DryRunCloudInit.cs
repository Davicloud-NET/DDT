// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.LinuxPhase;

// A dry run's Linux was never started, so cloud-init does not run in it and nothing is waited for.
public sealed class DryRunCloudInit(AgentLog log) : ICloudInit
{
    public bool Enabled
    {
        get
        {
            log.Information(
                $"Dry run: cloud-init counts as not running. In the installed Linux the agent would wait for /{CloudInitFiles.ResultPath}, " +
                "and then for a restart the user-data asks for.");

            return false;
        }
    }

    public string? Result() => null;

    public string? Status() => null;

    public string? Configuration() => null;

    public string? ScheduledShutdown() => null;

    public string? StartId() => null;

    public IReadOnlyList<string> OutputTail(int lines) => [];
}
