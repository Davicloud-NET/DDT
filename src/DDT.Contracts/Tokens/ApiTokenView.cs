// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Tokens;

// An API token as its owner and administrators see it, never with its secret.
public sealed record ApiTokenView(
    Guid Id,
    string Name,
    // The most the token may do. It never does more than its user is allowed to at that moment.
    string Role,
    Guid UserId,
    string UserName,
    // The secret's last four characters, to tell tokens apart.
    string Hint,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    DateTimeOffset? LastUsedUtc,
    string? LastUsedAddress,
    DateTimeOffset? RevokedUtc,
    string? RevokedByName);
