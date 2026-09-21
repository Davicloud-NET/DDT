// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// Fetched only while the JoinDomain step runs, and kept only in memory. UserName stays whole (DOMAIN\user or a UPN).
public sealed record AgentJoinDomainCredentials(string Domain, string? OrganizationalUnit, string UserName, string Password)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() =>
        $"AgentJoinDomainCredentials {{ Domain = {Domain}, OrganizationalUnit = {OrganizationalUnit}, UserName = {UserName} }}";
}
