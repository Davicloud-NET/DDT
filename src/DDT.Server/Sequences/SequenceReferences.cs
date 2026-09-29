// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Accounts;
using DDT.Server.Images;
using DDT.Server.Packages;

namespace DDT.Server.Sequences;

// What a sequence's steps refer to outside the sequence. It's read once for all the sequences a request checks.
public sealed record SequenceReferences(
    IReadOnlyDictionary<Guid, Image> Images,
    IReadOnlyDictionary<Guid, Package> Packages,
    bool DomainConfigured,
    bool LocalAdministratorConfigured)
{
    // The stored accounts that steps may use, by ID.
    public IReadOnlyDictionary<Guid, AccountFacts> Accounts { get; init; } = new Dictionary<Guid, AccountFacts>();

    // The value names that rules, machine roles and the deployment defaults set, ignoring case. A sequence may use them
    // without declaring them.
    public IReadOnlySet<string> ValueNames { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
