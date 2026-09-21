// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Authentication;

public static class DdtAuthenticationSchemes
{
    // Machines authenticate with an opaque bearer token on their own scheme. Policies name the
    // scheme explicitly so a machine token can never satisfy a human policy, or the reverse.
    public const string Machine = "DDT.Machine";
}
