// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

// Role is the highest role of the account, null for none. RoleFrom names the groups for an account whose groups decide
// its role, even while they give it none, and is null for any other account without a role. LockedOutUntil is set only
// while a lockout lasts. ExternalProvider is the display name of the single sign-on provider linked to the account.
public sealed record UserView(
    Guid Id,
    string UserName,
    string? DisplayName,
    string? Email,
    UserSource Source,
    string? Role,
    RoleSource? RoleFrom,
    bool Disabled,
    DateTimeOffset? LockedOutUntil,
    bool TwoFactorEnabled,
    bool HasPassword,
    bool MustChangePassword,
    string? ExternalProvider,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastSignInUtc);
