// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// What the steps of a run use their accounts with: shares in both phases, and signing in for a script that runs as an
// account, which only the installed Windows asks for.
public sealed record AccountTools(IShareConnector Shares, IAccountLogons Logons)
{
    public static AccountTools Native(AgentLog log) => new(new ShareConnector(new WNetConnections(), log), new WindowsAccountLogons(log));

    public static AccountTools DryRun(AgentLog log) => new(new DryRunShareConnector(log), new DryRunAccountLogons(log));
}
