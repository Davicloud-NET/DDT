// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using DDT.Contracts.Messages;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Ldap;

// Tests each step separately, for the settings page. That's the bind as the bind account, the search for the user,
// their password and their groups. The directory's own message is passed on, because only it says what's wrong.
public sealed class LdapTester(ILoggerFactory loggerFactory) : ILdapTester
{
    public Task<LdapTestOutcome> TestAsync(LdapOptions options, string? userName, string? password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Test(new LdapDirectoryReader(options, loggerFactory.CreateLogger<LdapAuthenticator>()), options, userName, password));
    }

    private static LdapTestOutcome Test(LdapDirectoryReader directory, LdapOptions options, string? userName, string? password)
    {
        string server = $"{options.Host}:{options.Port.ToString(CultureInfo.InvariantCulture)}";
        LdapConnection search;

        try
        {
            search = directory.CreateConnection();
            search.Bind(new NetworkCredential(options.BindDn, options.BindPassword));
        }
        catch (Exception exception) when (exception is LdapException or DirectoryOperationException or InvalidOperationException
            or ArgumentException or TypeInitializationException or DllNotFoundException)
        {
            return new(
                false,
                null,
                null,
                [],
                ServerMessages.SettingsLdapTestBindFailed.With("account", options.BindDn, "server", server, "error", exception.Message));
        }

        using (search)
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                return new(true, null, null, [], ServerMessages.SettingsLdapTestBound.With("account", options.BindDn, "server", server));
            }

            try
            {
                return TestUser(directory, search, options, userName, password);
            }
            catch (Exception exception) when (exception is LdapException or DirectoryOperationException)
            {
                return new(
                    true,
                    null,
                    null,
                    [],
                    ServerMessages.SettingsLdapTestSearchFailed.With("name", userName, "baseDn", options.BaseDn, "error", exception.Message));
            }
        }
    }

    private static LdapTestOutcome TestUser(LdapDirectoryReader directory, LdapConnection search, LdapOptions options, string userName, string? password)
    {
        (LdapLookupStatus status, SearchResultEntry? entry) = directory.FindUser(search, userName);

        if (entry is null)
        {
            return new(true, false, null, [], (status == LdapLookupStatus.Ambiguous
                ? ServerMessages.SettingsLdapTestManyEntries
                : ServerMessages.SettingsLdapTestNoEntry).With("baseDn", options.BaseDn, "name", userName));
        }

        bool? accepted = string.IsNullOrEmpty(password) ? null : directory.TryVerifyPassword(entry.DistinguishedName, password);

        if (accepted == false)
        {
            return new(true, true, false, [], ServerMessages.SettingsLdapTestPasswordRefused.With("entry", entry.DistinguishedName));
        }

        if (directory.ReadImmutableId(entry) is null)
        {
            return new(
                true,
                true,
                accepted,
                [],
                ServerMessages.SettingsLdapTestNoImmutableId.With("entry", entry.DistinguishedName, "attribute", options.ImmutableIdAttribute));
        }

        List<string> groups = directory.ReadGroups(search, entry.DistinguishedName);

        return new(true, true, accepted, groups, ServerMessages.SettingsLdapTestFound.With("entry", entry.DistinguishedName, "count", groups.Count));
    }
}
