// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Ldap;

// What a sign-in reads about a user with the bind account, not with the user's password.
public sealed record LdapLookup(
    // Found only when the user filter matches exactly one entry.
    LdapLookupStatus Status,
    string? DistinguishedName,
    string? DisplayName,
    // Null when the entry has no immutable id. A sign-in refuses that.
    string? ImmutableId,
    // Every group the user is in, including nested ones. Empty while ResolveNestedGroups is off.
    IReadOnlyList<string> GroupDns)
{
    public static LdapLookup Missing(LdapLookupStatus status) => new(status, null, null, null, []);
}
