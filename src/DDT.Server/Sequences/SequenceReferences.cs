// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Accounts;
using DDT.Server.Images;
using DDT.Server.Packages;

namespace DDT.Server.Sequences;

// What the steps of a sequence refer to outside of it, read once for every sequence a request checks.
public sealed record SequenceReferences(
    IReadOnlyDictionary<Guid, Image> Images,
    IReadOnlyDictionary<Guid, Package> Packages,
    bool DomainConfigured,
    bool LocalAdministratorConfigured)
{
    // The stored accounts steps may name, by id.
    public IReadOnlyDictionary<Guid, AccountFacts> Accounts { get; init; } = new Dictionary<Guid, AccountFacts>();

    // The names rules and machine roles give values, and the deployment defaults', ignoring case: a sequence may use them
    // without declaring them.
    public IReadOnlySet<string> ValueNames { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
