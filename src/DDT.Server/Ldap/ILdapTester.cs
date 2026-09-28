// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Ldap;

// Tries values not yet saved against the directory, for the settings page.
public interface ILdapTester
{
    Task<LdapTestOutcome> TestAsync(LdapOptions options, string? userName, string? password, CancellationToken cancellationToken);
}
