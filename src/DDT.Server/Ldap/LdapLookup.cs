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
    // Null when the entry has none, which a sign-in refuses.
    string? ImmutableId,
    // Every group the user is in, nested ones included; empty while ResolveNestedGroups is off.
    IReadOnlyList<string> GroupDns)
{
    public static LdapLookup Missing(LdapLookupStatus status) => new(status, null, null, null, []);
}
