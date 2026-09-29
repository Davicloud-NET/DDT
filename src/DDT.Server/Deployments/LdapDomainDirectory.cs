// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.DirectoryServices.Protocols;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DDT.Server.Ldap;

namespace DDT.Server.Deployments;

// Reads what the join account may do, signed in as that account, so Active Directory itself answers with the account's
// effective rights. The password never crosses the network in the clear. LDAPS comes first. If it fails for any reason
// but a wrong password, plain LDAP with signing and sealing follows, but only on Windows, where the LDAP client can
// sign and seal a Kerberos or NTLM sign-in.
public sealed partial class LdapDomainDirectory : IDomainDirectory
{
    private const int LdapsPort = 636;
    private const int LdapPort = 389;

    // LDAP_INVALID_CREDENTIALS, which ResultCode leaves out because only a bind returns it.
    private const int InvalidCredentials = 49;

    // The well-known GUID of the default Computers container, which redircmp may have moved.
    private const string ComputersContainerGuid = "AA312825768811D1ADED00C04FD8D5CD";

    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    public Task<DomainDirectoryFacts> ReadAsync(DomainDirectoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The LDAP client blocks on every call, for up to its timeout.
        return Task.Run(() => Read(request), cancellationToken);
    }

    private static DomainDirectoryFacts Read(DomainDirectoryRequest request)
    {
        (LdapConnection connection, string method) = Connect(request);

        using (connection)
        {
            string namingContext = ReadString(connection, string.Empty, "defaultNamingContext")
                ?? throw new DomainDirectoryException(DomainDirectoryFailure.Unreachable, $"{request.Controller} named no domain it serves.");

            string? container = request.OrganizationalUnit is { } organizationalUnit
                ? FindDistinguishedName(connection, organizationalUnit)
                : FindDistinguishedName(connection, $"<WKGUID={ComputersContainerGuid},{namingContext}>");

            bool canCreate = container is not null && ReadValues(connection, container, "allowedChildClassesEffective")
                .Contains("computer", StringComparer.OrdinalIgnoreCase);

            int? quota = int.TryParse(ReadString(connection, namingContext, "ms-DS-MachineAccountQuota"), CultureInfo.InvariantCulture, out int value)
                ? value
                : null;

            return new DomainDirectoryFacts(method, namingContext, container, canCreate, quota, CountCreatedComputers(connection, namingContext, request.UserName));
        }
    }

    private static (LdapConnection Connection, string Method) Connect(DomainDirectoryRequest request)
    {
        LdapConnection ldaps = NewConnection(request.Controller, LdapsPort);
        ldaps.SessionOptions.SecureSocketLayer = true;
        ldaps.AuthType = AuthType.Basic;

        try
        {
            ldaps.Bind(new NetworkCredential(request.UserName, request.Password));

            return (ldaps, "LDAPS");
        }
        catch (LdapException exception) when (exception.ErrorCode == InvalidCredentials)
        {
            ldaps.Dispose();

            throw Refused(exception);
        }
        catch (LdapException exception)
        {
            ldaps.Dispose();

            if (!OperatingSystem.IsWindows())
            {
                throw new DomainDirectoryException(DomainDirectoryFailure.NoSecureConnection, exception.Message, exception);
            }
        }

        LdapConnection sealedLdap = NewConnection(request.Controller, LdapPort);
        sealedLdap.AuthType = AuthType.Negotiate;
        sealedLdap.SessionOptions.Signing = true;
        sealedLdap.SessionOptions.Sealing = true;

        try
        {
            sealedLdap.Bind(Credential(request.UserName, request.Password));

            return (sealedLdap, "LDAP signed and sealed with Kerberos or NTLM");
        }
        catch (LdapException exception)
        {
            sealedLdap.Dispose();

            throw exception.ErrorCode == InvalidCredentials
                ? Refused(exception)
                : new DomainDirectoryException(DomainDirectoryFailure.Unreachable, exception.Message, exception);
        }
    }

    private static LdapConnection NewConnection(string controller, int port)
    {
        LdapConnection connection = new(new LdapDirectoryIdentifier(controller, port, fullyQualifiedDnsHostName: false, connectionless: false))
        {
            Timeout = s_timeout,
        };

        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

        return connection;
    }

    // A Negotiate sign-in takes the domain separately from the user. A user principal name is passed as is.
    private static NetworkCredential Credential(string userName, string password) =>
        userName.Split('\\') is [var domain, var user] ? new NetworkCredential(user, password, domain) : new NetworkCredential(userName, password);

    // Active Directory says why after "data" in its message, such as "data 52e" for a wrong password.
    private static DomainDirectoryException Refused(LdapException exception) =>
        new(DomainDirectoryFailure.SignInRefused, ReasonCode().Match(exception.ServerErrorMessage ?? string.Empty) is { Success: true } match
            ? match.Groups[1].Value.ToLowerInvariant()
            : null, exception);

    // Null if the entry doesn't exist, or lies outside the domain and comes back as a referral.
    private static string? FindDistinguishedName(LdapConnection connection, string distinguishedName)
    {
        try
        {
            SearchResponse response = (SearchResponse)connection.SendRequest(
                new SearchRequest(distinguishedName, "(objectClass=*)", SearchScope.Base, "distinguishedName"));

            return response.Entries.Count == 1 ? response.Entries[0].DistinguishedName : null;
        }
        catch (DirectoryOperationException exception) when (exception.Response?.ResultCode is ResultCode.NoSuchObject or ResultCode.Referral or ResultCode.InvalidDNSyntax)
        {
            return null;
        }
    }

    private static string? ReadString(LdapConnection connection, string distinguishedName, string attribute) =>
        ReadValues(connection, distinguishedName, attribute).FirstOrDefault();

    private static List<string> ReadValues(LdapConnection connection, string distinguishedName, string attribute)
    {
        SearchResponse response = (SearchResponse)connection.SendRequest(
            new SearchRequest(distinguishedName, "(objectClass=*)", SearchScope.Base, attribute));

        return response.Entries.Count == 1 && response.Entries[0].Attributes[attribute] is { } values
            ? [.. values.GetValues(typeof(string)).Cast<string>()]
            : [];
    }

    // Only computers joined within the quota carry their creator's SID. Those an account created with its own right
    // don't count against the quota.
    private static int CountCreatedComputers(LdapConnection connection, string namingContext, string userName)
    {
        string accountFilter = userName.Split('\\') is [_, var user]
            ? $"(sAMAccountName={LdapFilter.EscapeValue(user)})"
            : $"(|(userPrincipalName={LdapFilter.EscapeValue(userName)})(sAMAccountName={LdapFilter.EscapeValue(userName.Split('@')[0])}))";

        SearchResponse accounts = (SearchResponse)connection.SendRequest(
            new SearchRequest(namingContext, $"(&(objectCategory=person)(objectClass=user){accountFilter})", SearchScope.Subtree, "objectSid") { SizeLimit = 2 });

        if (accounts.Entries.Count != 1 || accounts.Entries[0].Attributes["objectSid"] is not { Count: > 0 } sid || sid[0] is not byte[] bytes)
        {
            return 0;
        }

        StringBuilder escaped = new();

        foreach (byte octet in bytes)
        {
            escaped.Append(CultureInfo.InvariantCulture, $"\\{octet:x2}");
        }

        try
        {
            SearchResponse created = (SearchResponse)connection.SendRequest(
                new SearchRequest(namingContext, $"(&(objectCategory=computer)(ms-DS-CreatorSID={escaped}))", SearchScope.Subtree, "1.1") { SizeLimit = 1000 });

            return created.Entries.Count;
        }
        catch (DirectoryOperationException exception) when (exception.Response?.ResultCode == ResultCode.SizeLimitExceeded)
        {
            return exception.Response is SearchResponse partial ? partial.Entries.Count : 1000;
        }
    }

    [GeneratedRegex(@"\bdata ([0-9a-fA-F]+)\b")]
    private static partial Regex ReasonCode();
}
