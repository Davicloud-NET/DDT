// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Ldap;

// The connections and searches a sign-in makes, for one version of the ldap section.
internal sealed class LdapDirectoryReader(LdapOptions options, ILogger<LdapAuthenticator> logger)
{
    public LdapConnection CreateConnection()
    {
        LdapDirectoryIdentifier identifier = new(options.Host, options.Port, fullyQualifiedDnsHostName: true, connectionless: false);
        LdapConnection connection = new(identifier)
        {
            AuthType = AuthType.Basic,
            Timeout = options.Timeout,
        };

        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        switch (options.Transport)
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
                throw new InvalidOperationException($"Unsupported LDAP transport {options.Transport}.");
        }

        return connection;
    }

    // A second match is enough to refuse, so the search stops there; the directory answers that with a size limit error.
    public (LdapLookupStatus Status, SearchResultEntry? Entry) FindUser(LdapConnection connection, string userName)
    {
        string filter = string.Format(CultureInfo.InvariantCulture, options.UserFilter, LdapFilter.EscapeValue(userName));
        SearchRequest request = new(
            options.BaseDn,
            filter,
            SearchScope.Subtree,
            options.ImmutableIdAttribute,
            options.DisplayNameAttribute,
            options.EmailAttribute)
        {
            SizeLimit = 2,
        };

        List<SearchResultEntry> entries = Entries(connection, request);
        if (entries.Count == 0)
        {
            LdapLog.UserNotFound(logger, userName, options.BaseDn);
            return (LdapLookupStatus.NotFound, null);
        }

        if (entries.Count > 1)
        {
            LdapLog.AmbiguousMatch(logger, userName, entries.Count);
            return (LdapLookupStatus.Ambiguous, null);
        }

        return (LdapLookupStatus.Found, entries[0]);
    }

    public bool TryVerifyPassword(string distinguishedName, string password)
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

    public string? ReadImmutableId(SearchResultEntry entry)
    {
        DirectoryAttribute? attribute = entry.Attributes[options.ImmutableIdAttribute];
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

    public List<string> ReadGroups(LdapConnection connection, string userDn)
    {
        if (!options.ResolveNestedGroups)
        {
            return [];
        }

        string filter = string.Format(
            CultureInfo.InvariantCulture,
            "(member:1.2.840.113556.1.4.1941:={0})",
            LdapFilter.EscapeValue(userDn));

        SearchRequest request = new(options.BaseDn, filter, SearchScope.Subtree, "distinguishedName");
        SearchResponse response = (SearchResponse)connection.SendRequest(request);

        List<string> groups = new(response.Entries.Count);
        foreach (SearchResultEntry group in response.Entries)
        {
            groups.Add(group.DistinguishedName);
        }

        return groups;
    }

    // Null when the entry does not exist, or lies outside what the directory serves, as a referral.
    public static string? ReadCommonName(LdapConnection connection, string distinguishedName)
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

    // A search that reaches its size limit fails, with the entries it found up to there.
    public static List<SearchResultEntry> Entries(LdapConnection connection, SearchRequest request)
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

    public static string? ReadString(SearchResultEntry entry, string attributeName)
    {
        DirectoryAttribute? attribute = entry.Attributes[attributeName];
        return attribute is { Count: > 0 } ? attribute[0] as string : null;
    }
}
