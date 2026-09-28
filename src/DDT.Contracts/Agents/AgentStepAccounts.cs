// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// The accounts a step uses, fetched from AgentRoutes.RunStepAccounts only while the step runs and kept only in memory:
// the account a script runs as, in the Windows phase only, and the shares to connect, with their paths worked out by
// the server. The agent hands none of it to a script.
public sealed record AgentStepAccounts(AgentAccount? RunAs, IReadOnlyList<AgentShareConnection> Shares);

// UserName stays whole, DOMAIN\user or a UPN.
public sealed record AgentAccount(string UserName, string Password)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() => $"AgentAccount {{ UserName = {UserName} }}";
}

public sealed record AgentShareConnection(string Path, string UserName, string Password)
{
    public override string ToString() => $"AgentShareConnection {{ Path = {Path}, UserName = {UserName} }}";
}
