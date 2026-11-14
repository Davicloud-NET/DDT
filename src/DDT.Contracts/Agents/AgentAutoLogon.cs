// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// Who Windows signs in as by itself, Count times, after a run in Windows. A null Domain is a local account.
public sealed record AgentAutoLogon(string UserName, string? Domain, string Password, int Count)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() => $"AgentAutoLogon {{ UserName = {UserName}, Domain = {Domain}, Count = {Count} }}";
}
