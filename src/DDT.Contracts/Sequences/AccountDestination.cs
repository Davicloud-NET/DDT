// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Where an account may be used. A password given for one destination is never sent to another.
public sealed record AccountDestination
{
    // The domain the account joins.
    public string? Domain { get; init; }

    // The share hosts it may connect to.
    public IReadOnlyList<string> Hosts { get; init; } = [];

    // Whether a script may run as it.
    public bool RunAs { get; init; }
}
