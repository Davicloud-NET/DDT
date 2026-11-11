// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// A named list of applications that installs in its order, each after its dependencies.
public sealed record ApplicationBundleView(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<Guid> Members,
    IReadOnlyList<ApplicationUse> UsedBy,
    long Revision,
    DateTimeOffset UpdatedUtc,
    string? UpdatedBy);
