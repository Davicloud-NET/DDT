// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

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

// Text is English; Code and Args are the same sentence for a client that says it in the person's language. Two findings
// are equal when they say the same: the code and its values say it again, and a dictionary compares only by reference.
public sealed record DomainJoinFinding(DomainJoinFindingLevel Level, string Text, string? Code = null, IReadOnlyDictionary<string, object>? Args = null)
{
    public static DomainJoinFinding From(DomainJoinFindingLevel level, ServerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new(level, message.Text, message.Code, message.Args);
    }

    public bool Equals(DomainJoinFinding? other) => other is not null && Level == other.Level && Text == other.Text;

    public override int GetHashCode() => HashCode.Combine(Level, Text);
}

public enum DomainJoinFindingLevel
{
    Passed,
    Warning,
    Problem,
}
