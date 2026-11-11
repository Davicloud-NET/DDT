// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Server.Packages;

namespace DDT.Server.Applications;

// The whole library as read at one moment: applications by id, bundles, the files packages of applications by id, and
// what uses each application and bundle.
public sealed record ApplicationRows(
    IReadOnlyDictionary<Guid, Application> Applications,
    IReadOnlyList<ApplicationBundle> Bundles,
    IReadOnlyDictionary<Guid, Package> Files,
    IReadOnlyDictionary<Guid, IReadOnlyList<ApplicationUse>> Uses)
{
    public IReadOnlyList<ApplicationUse> UsesOf(Guid id) => Uses.GetValueOrDefault(id) ?? [];

    // Names in the order given; an id no longer in the library is left out.
    public IEnumerable<string> NamesOf(IEnumerable<Guid> ids) =>
        ids.Select(id => Applications.GetValueOrDefault(id)?.Name).OfType<string>();
}
