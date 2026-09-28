// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

public sealed record UserView(
    Guid Id,
    string UserName,
    string? DisplayName,
    string? Email,
    UserSource Source,
    // The account's highest role, null for none.
    string? Role,
    // Names the groups for an account whose groups decide its role, even while they give it none; null for any other
    // account without a role.
    RoleSource? RoleFrom,
    bool Disabled,
    // Set only while a lockout lasts.
    DateTimeOffset? LockedOutUntil,
    bool TwoFactorEnabled,
    bool HasPassword,
    bool MustChangePassword,
    // The display name of the single sign-on provider linked to the account.
    string? ExternalProvider,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LastSignInUtc);
