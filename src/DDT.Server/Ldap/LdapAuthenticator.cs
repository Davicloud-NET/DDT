// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using DDT.Contracts.Messages;
using DDT.Server.Settings;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Ldap;

// Takes the ldap section once, when the scope creates it, so a sign-in uses one version of the settings throughout.
public sealed class LdapAuthenticator(LdapOptions options, ILogger<LdapAuthenticator> logger) : ILdapAuthenticator
{
    // LDAP_INVALID_CREDENTIALS, which ResultCode leaves out because only a bind returns it.
    private const int InvalidCredentials = 49;

    private readonly LdapOptions _options = options;
    private readonly ILogger<LdapAuthenticator> _logger = logger;
    private readonly LdapDirectoryReader _directory = new(options, logger);

    public LdapAuthenticator(DdtSettings settings, ILogger<LdapAuthenticator> logger)
        : this(settings?.Current.Ldap ?? throw new ArgumentNullException(nameof(settings)), logger)
    {
    }

    public Task<LdapIdentity?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        if (string.IsNullOrEmpty(password))
        {
            LdapLog.EmptyPasswordRejected(_logger, userName);
            return Task.FromResult<LdapIdentity?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Authenticate(userName, password));
    }

    private LdapIdentity? Authenticate(string userName, string password)
    {
        using LdapConnection search = _directory.CreateConnection();
        try
        {
            search.Bind(new NetworkCredential(_options.BindDn, _options.BindPassword));
        }
        catch (LdapException ex)
        {
            LdapLog.ServiceBindFailed(_logger, _options.Host, _options.Port, ex);
            return null;
        }

        (_, SearchResultEntry? entry) = _directory.FindUser(search, userName);
        if (entry is null)
        {
            return null;
        }

        if (!_directory.TryVerifyPassword(entry.DistinguishedName, password))
        {
            return null;
        }

        string? immutableId = _directory.ReadImmutableId(entry);
        if (immutableId is null)
        {
            LdapLog.MissingImmutableId(_logger, entry.DistinguishedName, _options.ImmutableIdAttribute);
            return null;
        }

        return new LdapIdentity(
            immutableId,
            entry.DistinguishedName,
            userName,
            LdapDirectoryReader.ReadString(entry, _options.DisplayNameAttribute),
            LdapDirectoryReader.ReadString(entry, _options.EmailAttribute),
            _directory.ReadGroups(search, entry.DistinguishedName));
    }

    public Task<LdapLookup> LookUpAsync(string userName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Ask(search =>
        {
            (LdapLookupStatus status, SearchResultEntry? entry) = _directory.FindUser(search, userName);

            return entry is null
                ? LdapLookup.Missing(status)
                : new LdapLookup(
                    status,
                    entry.DistinguishedName,
                    LdapDirectoryReader.ReadString(entry, _options.DisplayNameAttribute),
                    _directory.ReadImmutableId(entry),
                    _directory.ReadGroups(search, entry.DistinguishedName));
        }));
    }

    // A search for the text anywhere in a name cannot use the directory's indexes and stops at the size limit, so the
    // names that start with it, which are what someone typing a name is after, are searched for first.
    public Task<IReadOnlyList<LdapGroup>> SearchGroupsAsync(string text, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        string value = LdapFilter.EscapeValue(text.Trim());

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Ask<IReadOnlyList<LdapGroup>>(search =>
        {
            if (value.Length == 0)
            {
                return SearchGroups(search, "(objectClass=group)", limit);
            }

            List<LdapGroup> found = SearchGroups(search, $"(&(objectClass=group)(|(cn={value}*)(sAMAccountName={value}*)))", limit);

            if (found.Count < limit)
            {
                HashSet<string> seen = new(found.Select(group => group.DistinguishedName), StringComparer.OrdinalIgnoreCase);

                found.AddRange(SearchGroups(search, $"(&(objectClass=group)(|(cn=*{value}*)(name=*{value}*)(sAMAccountName=*{value}*)))", limit + found.Count)
                    .Where(group => seen.Add(group.DistinguishedName)));
            }

            return [.. found.Take(limit)];
        }));
    }

    public Task<IReadOnlyDictionary<string, string?>> GroupNamesAsync(IReadOnlyCollection<string> distinguishedNames, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(distinguishedNames);

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Ask<IReadOnlyDictionary<string, string?>>(search =>
        {
            Dictionary<string, string?> names = new(StringComparer.OrdinalIgnoreCase);

            foreach (string distinguishedName in distinguishedNames)
            {
                names[distinguishedName] = LdapDirectoryReader.ReadCommonName(search, distinguishedName);
            }

            return names;
        }));
    }

    // Signed in as the bind account, as a sign-in searches. Whatever keeps the directory from answering becomes one
    // exception with a message for an administrator.
    private T Ask<T>(Func<LdapConnection, T> question)
    {
        string server = $"{_options.Host}:{_options.Port.ToString(CultureInfo.InvariantCulture)}";

        try
        {
            using LdapConnection search = _directory.CreateConnection();
            search.Bind(new NetworkCredential(_options.BindDn, _options.BindPassword));

            return question(search);
        }
        catch (LdapException exception) when (exception.ErrorCode == InvalidCredentials)
        {
            LdapLog.ServiceBindFailed(_logger, _options.Host, _options.Port, exception);

            throw new LdapUnavailableException(ServerMessages.DirectoryBindRefused.With("server", server, "bindDn", _options.BindDn), exception);
        }
        catch (LdapException exception)
        {
            throw new LdapUnavailableException(ServerMessages.DirectoryUnreachable.With("server", server, "detail", exception.Message), exception);
        }
        catch (DirectoryOperationException exception)
        {
            throw new LdapUnavailableException(
                ServerMessages.DirectorySearchRefused.With("server", server, "baseDn", _options.BaseDn, "detail", exception.Message),
                exception);
        }
    }

    private List<LdapGroup> SearchGroups(LdapConnection connection, string filter, int limit)
    {
        SearchRequest request = new(_options.BaseDn, filter, SearchScope.Subtree, "cn", "description") { SizeLimit = limit };

        return [.. LdapDirectoryReader.Entries(connection, request)
            .Select(entry => new LdapGroup(entry.DistinguishedName, LdapDirectoryReader.ReadString(entry, "cn"), LdapDirectoryReader.ReadString(entry, "description")))
            .OrderBy(group => group.Name ?? group.DistinguishedName, StringComparer.OrdinalIgnoreCase)];
    }
}
