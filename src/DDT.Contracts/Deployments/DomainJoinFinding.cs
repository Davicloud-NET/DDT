// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Contracts.Deployments;

// Text is English; Code and Args say the same for a client in the person's language.
public sealed record DomainJoinFinding(DomainJoinFindingLevel Level, string Text, string? Code = null, IReadOnlyDictionary<string, object>? Args = null)
{
    public static DomainJoinFinding From(DomainJoinFindingLevel level, ServerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return new(level, message.Text, message.Code, message.Args);
    }

    // Equal when they say the same: the code and its values repeat Text, and a dictionary compares only by reference.
    public bool Equals(DomainJoinFinding? other) => other is not null && Level == other.Level && Text == other.Text;

    public override int GetHashCode() => HashCode.Combine(Level, Text);
}
