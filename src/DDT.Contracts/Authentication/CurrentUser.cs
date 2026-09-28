// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Authentication;

public sealed record CurrentUser(
    Guid Id,
    string UserName,
    string? DisplayName,
    string Source,
    bool TwoFactorEnabled,
    IReadOnlyList<string> Roles,
    // The user signed in with a password that an administrator was shown. Every other request is refused until they
    // set their own.
    bool MustChangePassword);
