// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Ldap;

// The directory as DDT uses it, to sign people in and to explain a sign-in to an administrator. The lookups search with
// the bind account as a sign-in does, and throw LdapUnavailableException when the directory cannot be asked.
public interface ILdapAuthenticator
{
    Task<LdapIdentity?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);

    Task<LdapLookup> LookUpAsync(string userName, CancellationToken cancellationToken);

    // Groups under the base DN whose name starts with the text first, then those that contain it.
    Task<IReadOnlyList<LdapGroup>> SearchGroupsAsync(string text, int limit, CancellationToken cancellationToken);

    // The common name of each group, or null for one the directory does not have.
    Task<IReadOnlyDictionary<string, string?>> GroupNamesAsync(IReadOnlyCollection<string> distinguishedNames, CancellationToken cancellationToken);
}
