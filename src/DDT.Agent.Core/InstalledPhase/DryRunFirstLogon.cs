// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.InstalledPhase;

// Writes nothing, because the registry is this computer's.
public sealed class DryRunFirstLogon(AgentLog log) : IFirstLogon
{
    public void SetUp(AgentFirstLogon firstLogon)
    {
        ArgumentNullException.ThrowIfNull(firstLogon);

        foreach (string command in firstLogon.Commands)
        {
            log.Information($"Dry run: in Windows, RunOnce would run {command}");
        }

        if (firstLogon.AutoLogon is { } autoLogon)
        {
            log.Information($"Dry run: in Windows, Winlogon would sign in as {FirstLogonTexts.Account(autoLogon)} {autoLogon.Count} times.");
        }
    }
}
