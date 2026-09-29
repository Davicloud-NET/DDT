// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// UserName isn't split up. It's DOMAIN\user or a UPN.
public sealed record AgentAccount(string UserName, string Password)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() => $"AgentAccount {{ UserName = {UserName} }}";
}
