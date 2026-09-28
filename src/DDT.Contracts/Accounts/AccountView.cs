// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Settings;

namespace DDT.Contracts.Accounts;

// An account steps use, stored with its password encrypted; the password never leaves the server except to the step
// that uses it, while it runs. The account is bound to its destinations.
public sealed record AccountView(
    Guid Id,
    string Name,
    string UserName,
    // The domain a join with it may join.
    string? Domain,
    // The share hosts it may connect to.
    IReadOnlyList<string> Hosts,
    // Whether a script may run as it.
    bool RunAs,
    SecretState Password,
    IReadOnlyList<AccountUse> UsedBy,
    long Revision,
    DateTimeOffset UpdatedUtc,
    string? UpdatedBy);
