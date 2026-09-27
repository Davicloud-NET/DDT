// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Ldap;

public sealed class LdapAuthenticator(IOptions<LdapOptions> options, ILogger<LdapAuthenticator> logger) : ILdapAuthenticator
{
    // LDAP_INVALID_CREDENTIALS, which ResultCode leaves out because only a bind returns it.
    private const int InvalidCredentials = 49;

    private readonly LdapOptions _options = options.Value;

    public Task<LdapIdentity?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        if (string.IsNullOrEmpty(password))
        {
            LdapLog.EmptyPasswordRejected(logger, userName);
            return Task.FromResult<LdapIdentity?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Authenticate(userName, password));
    }

    private LdapIdentity? Authenticate(string userName, string password)
    {
        using LdapConnection search = CreateConnection();
        try
        {
            search.Bind(new NetworkCredential(_options.BindDn, _options.BindPassword));
        }
        catch (LdapException ex)
        {
            LdapLog.ServiceBindFailed(logger, _options.Host, _options.Port, ex);
            return null;
        }

        (_, SearchResultEntry? entry) = FindUser(search, userName);
        if (entry is null)
        {
            return null;
        }

        if (!TryVerifyPassword(entry.DistinguishedName, password))
        {
            return null;
        }

        string? immutableId = ReadImmutableId(entry);
        if (immutableId is null)
        {
            LdapLog.MissingImmutableId(logger, entry.DistinguishedName, _options.ImmutableIdAttribute);
            return null;
        }

        return new LdapIdentity(
            immutableId,
            entry.DistinguishedName,
            userName,
            ReadString(entry, _options.DisplayNameAttribute),
            ReadString(entry, _options.EmailAttribute),
            ReadGroups(search, entry.DistinguishedName));
    }

    public Task<LdapLookup> LookUpAsync(string userName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Ask(search =>
        {
            (LdapLookupStatus status, SearchResultEntry? entry) = FindUser(search, userName);

            return entry is null
                ? LdapLookup.Missing(status)
                : new LdapLookup(
                    status,
                    entry.DistinguishedName,
                    ReadString(entry, _options.DisplayNameAttribute),
                    ReadImmutableId(entry),
                    ReadGroups(search, entry.DistinguishedName));
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
                names[distinguishedName] = ReadCommonName(search, distinguishedName);
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
            using LdapConnection search = CreateConnection();
            search.Bind(new NetworkCredential(_options.BindDn, _options.BindPassword));

            return question(search);
        }
        catch (LdapException exception) when (exception.ErrorCode == InvalidCredentials)
        {
            LdapLog.ServiceBindFailed(logger, _options.Host, _options.Port, exception);

            throw new LdapUnavailableException(
                $"The directory at {server} refused the bind account {_options.BindDn}. Check DDT:Ldap:BindDn and its password.",
                exception);
        }
        catch (LdapException exception)
        {
            throw new LdapUnavailableException($"The directory at {server} could not be reached: {exception.Message}", exception);
        }
        catch (DirectoryOperationException exception)
        {
            throw new LdapUnavailableException(
                $"The directory at {server} refused the search under {_options.BaseDn}: {exception.Message} Check DDT:Ldap:BaseDn.",
                exception);
        }
    }

    private List<LdapGroup> SearchGroups(LdapConnection connection, string filter, int limit)
    {
        SearchRequest request = new(_options.BaseDn, filter, SearchScope.Subtree, "cn", "description") { SizeLimit = limit };

        return [.. Entries(connection, request)
            .Select(entry => new LdapGroup(entry.DistinguishedName, ReadString(entry, "cn"), ReadString(entry, "description")))
            .OrderBy(group => group.Name ?? group.DistinguishedName, StringComparer.OrdinalIgnoreCase)];
    }

    // A search that reaches its size limit fails, with the entries it found up to there.
    private static List<SearchResultEntry> Entries(LdapConnection connection, SearchRequest request)
    {
        SearchResponse response;

        try
        {
            response = (SearchResponse)connection.SendRequest(request);
        }
        catch (DirectoryOperationException exception) when (exception.Response is SearchResponse { ResultCode: ResultCode.SizeLimitExceeded } partial)
        {
            response = partial;
        }

        return [.. response.Entries.Cast<SearchResultEntry>()];
    }

    // Null when the entry does not exist, or lies outside what the directory serves, as a referral.
    private static string? ReadCommonName(LdapConnection connection, string distinguishedName)
    {
        try
        {
            SearchResponse response = (SearchResponse)connection.SendRequest(
                new SearchRequest(distinguishedName, "(objectClass=*)", SearchScope.Base, "cn"));

            return response.Entries.Count == 1 ? ReadString(response.Entries[0], "cn") : null;
        }
        catch (DirectoryOperationException exception) when (exception.Response?.ResultCode is ResultCode.NoSuchObject or ResultCode.Referral or ResultCode.InvalidDNSyntax)
        {
            return null;
        }
    }

    private LdapConnection CreateConnection()
    {
        LdapDirectoryIdentifier identifier = new(_options.Host, _options.Port, fullyQualifiedDnsHostName: true, connectionless: false);
        LdapConnection connection = new(identifier)
        {
            AuthType = AuthType.Basic,
            Timeout = _options.Timeout,
        };

        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        switch (_options.Transport)
        {
            case LdapTransport.Ldaps:
                connection.SessionOptions.SecureSocketLayer = true;
                break;
            case LdapTransport.StartTls:
                connection.AuthType = AuthType.Anonymous;
                connection.Bind();
                connection.SessionOptions.StartTransportLayerSecurity(null);
                connection.AuthType = AuthType.Basic;
                break;
            case LdapTransport.UnencryptedDangerous:
                break;
            default:
                throw new InvalidOperationException($"Unsupported LDAP transport {_options.Transport}.");
        }

        return connection;
    }

    // A second match is enough to refuse, so the search stops there; the directory answers that with a size limit error.
    private (LdapLookupStatus Status, SearchResultEntry? Entry) FindUser(LdapConnection connection, string userName)
    {
        string filter = string.Format(CultureInfo.InvariantCulture, _options.UserFilter, LdapFilter.EscapeValue(userName));
        SearchRequest request = new(
            _options.BaseDn,
            filter,
            SearchScope.Subtree,
            _options.ImmutableIdAttribute,
            _options.DisplayNameAttribute,
            _options.EmailAttribute)
        {
            SizeLimit = 2,
        };

        List<SearchResultEntry> entries = Entries(connection, request);
        if (entries.Count == 0)
        {
            LdapLog.UserNotFound(logger, userName, _options.BaseDn);
            return (LdapLookupStatus.NotFound, null);
        }

        if (entries.Count > 1)
        {
            LdapLog.AmbiguousMatch(logger, userName, entries.Count);
            return (LdapLookupStatus.Ambiguous, null);
        }

        return (LdapLookupStatus.Found, entries[0]);
    }

    private bool TryVerifyPassword(string distinguishedName, string password)
    {
        using LdapConnection verify = CreateConnection();
        try
        {
            verify.Bind(new NetworkCredential(distinguishedName, password));
            return true;
        }
        catch (LdapException ex)
        {
            LdapLog.BindFailed(logger, distinguishedName, ex.ErrorCode);
            return false;
        }
        catch (DirectoryOperationException ex)
        {
            LdapLog.BindFailed(logger, distinguishedName, (int)(ex.Response?.ResultCode ?? ResultCode.Other));
            return false;
        }
    }

    private string? ReadImmutableId(SearchResultEntry entry)
    {
        DirectoryAttribute? attribute = entry.Attributes[_options.ImmutableIdAttribute];
        if (attribute is null || attribute.Count == 0)
        {
            return null;
        }

        object value = attribute[0];
        return value switch
        {
            byte[] { Length: 16 } bytes => new Guid(bytes).ToString("D", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToHexString(bytes),
            string text => text,
            _ => null,
        };
    }

    private static string? ReadString(SearchResultEntry entry, string attributeName)
    {
        DirectoryAttribute? attribute = entry.Attributes[attributeName];
        return attribute is { Count: > 0 } ? attribute[0] as string : null;
    }

    private List<string> ReadGroups(LdapConnection connection, string userDn)
    {
        if (!_options.ResolveNestedGroups)
        {
            return [];
        }

        string filter = string.Format(
            CultureInfo.InvariantCulture,
            "(member:1.2.840.113556.1.4.1941:={0})",
            LdapFilter.EscapeValue(userDn));

        SearchRequest request = new(_options.BaseDn, filter, SearchScope.Subtree, "distinguishedName");
        SearchResponse response = (SearchResponse)connection.SendRequest(request);

        List<string> groups = new(response.Entries.Count);
        foreach (SearchResultEntry group in response.Entries)
        {
            groups.Add(group.DistinguishedName);
        }

        return groups;
    }
}
