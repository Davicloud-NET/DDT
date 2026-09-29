// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// An account a step uses, with its password and where it may go. Describe names it for an audit or a refusal.
public sealed record StepAccount(string Describe, string UserName, string Password, string? Domain, IReadOnlyList<string> Hosts, bool RunAs)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() => $"StepAccount {{ Describe = {Describe}, UserName = {UserName} }}";
}
