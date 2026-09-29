// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Ldap;

// DDT's access to the directory. It signs people in and explains a sign-in to an administrator. The lookups search with
// the bind account, just like a sign-in, and throw LdapUnavailableException when the directory can't be queried.
public interface ILdapAuthenticator
{
    Task<LdapIdentity?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);

    Task<LdapLookup> LookUpAsync(string userName, CancellationToken cancellationToken);

    // Returns groups under the base DN. Names that start with the text come first, then names that contain it.
    Task<IReadOnlyList<LdapGroup>> SearchGroupsAsync(string text, int limit, CancellationToken cancellationToken);

    // The common name of each group, or null if the directory doesn't have it.
    Task<IReadOnlyDictionary<string, string?>> GroupNamesAsync(IReadOnlyCollection<string> distinguishedNames, CancellationToken cancellationToken);
}
