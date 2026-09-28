// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Accounts;

public static class AccountLimits
{
    public const int MaxNameLength = 128;

    // A user principal name, or a domain and a user name.
    public const int MaxUserNameLength = 256;

    // A DNS name's longest.
    public const int MaxDomainLength = 253;
}
