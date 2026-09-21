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

        SearchResultEntry? entry = FindUser(search, userName);
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

    private SearchResultEntry? FindUser(LdapConnection connection, string userName)
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

        SearchResponse response = (SearchResponse)connection.SendRequest(request);
        if (response.Entries.Count == 0)
        {
            LdapLog.UserNotFound(logger, userName, _options.BaseDn);
            return null;
        }

        if (response.Entries.Count > 1)
        {
            LdapLog.AmbiguousMatch(logger, userName, response.Entries.Count);
            return null;
        }

        return response.Entries[0];
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
