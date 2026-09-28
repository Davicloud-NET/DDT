// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Accounts;

public static class AccountLimits
{
    public const int MaxNameLength = 128;

    // A user principal name, or a domain and a user name.
    public const int MaxUserNameLength = 256;

    // The longest a DNS name can be.
    public const int MaxDomainLength = 253;

    // Active Directory takes passwords of up to 256 characters.
    public const int MaxPasswordLength = 256;

    // The most share servers one account may connect to. An account that needs more should be split.
    public const int MaxHosts = 32;
}
