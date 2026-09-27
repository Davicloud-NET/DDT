// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Ldap;

// What a sign-in reads about a user, read with the bind account instead of the user's password. Found only when the
// user filter matches exactly one entry, as a sign-in requires. ImmutableId is null when the entry has none, which a
// sign-in refuses. GroupDns are every group the user is in, nested ones included.
public sealed record LdapLookup(
    LdapLookupStatus Status,
    string? DistinguishedName,
    string? DisplayName,
    string? ImmutableId,
    IReadOnlyList<string> GroupDns)
{
    public static LdapLookup Missing(LdapLookupStatus status) => new(status, null, null, null, []);
}

public enum LdapLookupStatus
{
    Found,
    NotFound,
    Ambiguous,
}
