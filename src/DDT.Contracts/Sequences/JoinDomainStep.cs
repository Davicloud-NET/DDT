// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization;

namespace DDT.Contracts.Sequences;

// There's no domain field on purpose. The join password is bound to its domain, the configured one or the account's.
// A domain named in a sequence could send the join account to a foreign domain controller.
public sealed record JoinDomainStep : SequenceStep
{
    // Null uses the configured default. From version 3 on it's a template.
    public string? OrganizationalUnit { get; init; }

    // Version 3. The account that joins, which also decides the domain. Null uses the configured join account.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AccountReference? Account { get; init; }

    [JsonIgnore]
    public override SequencePhase? RequiredPhase => SequencePhase.Windows;

    [JsonIgnore]
    public override int MinimumVersion => Account is null ? 1 : 3;
}
