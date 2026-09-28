// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tokens;

public enum ApiTokenRevocation
{
    // Also returned when the token was already revoked.
    Revoked,
    NotFound,

    // The token belongs to another user, and only an administrator can revoke it.
    NotOwner,
}
