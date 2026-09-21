// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// No domain field: the join password is bound to DDT:Deployment:Domain:Name, and a domain named in a sequence
// could send the join account to a foreign domain controller.
public sealed record JoinDomainStep : SequenceStep
{
    // Null takes the configured default.
    public string? OrganizationalUnit { get; init; }

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.Windows;
}
