// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Authentication;

// NoRole: the password was right, but the account is in none of the groups the directory map gives a role, so it
// may not sign in. The page says so, since the password is not what to fix.
public enum LoginStatus
{
    Succeeded,
    RequiresTwoFactor,
    LockedOut,
    Failed,
    NoRole,
}
