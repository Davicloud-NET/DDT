// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Deployments;

// OrganizationalUnit: a Join the domain step's own, or null for the configured default.
public sealed record DomainJoinCheckRequest(string? OrganizationalUnit);

// CanJoin: the join account can join a machine into Container now. Findings say what was checked, in order, and what to
// change. Container is the organizational unit or the default Computers container, null when the check stopped before.
public sealed record DomainJoinCheckView(
    bool CanJoin,
    string? Domain,
    string? UserName,
    string? Controller,
    string? Container,
    IReadOnlyList<DomainJoinFinding> Findings,
    DateTimeOffset CheckedUtc);

public sealed record DomainJoinFinding(DomainJoinFindingLevel Level, string Text);

public enum DomainJoinFindingLevel
{
    Passed,
    Warning,
    Problem,
}
