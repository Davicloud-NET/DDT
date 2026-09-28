// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Tokens;

public enum ApiTokenRevocation
{
    // Also when it was revoked before.
    Revoked,
    NotFound,

    // Another user's token, which only an administrator revokes.
    NotOwner,
}
