// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers;
using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Messages;

namespace DDT.Server.Accounts;

// What an account, stored or given for one run, may hold, and which server a share path names. The same rules check the
// Accounts page, the answers to account inputs and every share a step connects, so a password goes to no server its
// account does not name.
public static class AccountRules
{
    // Besides control characters, which no part of a share path holds.
    private static readonly SearchValues<char> s_forbiddenInSharePath = SearchValues.Create("/:*?\"<>|");

    public static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // DOMAIN\user or user@domain, which a share connection and a logon take whole. A user name alone would be looked up
    // on whichever machine the step runs.
    public static ServerMessage? UserNameProblem(string? userName)
    {
        string? trimmed = Trimmed(userName);

        return trimmed is not null && trimmed.Length <= AccountLimits.MaxUserNameLength && !trimmed.Any(char.IsControl) && IsQualified(trimmed)
            ? null
            : ServerMessages.StepAccountUserNameForm.With("max", AccountLimits.MaxUserNameLength);
    }

    public static ServerMessage? PasswordProblem(string? password) =>
        string.IsNullOrEmpty(password) ? ServerMessages.StepAccountPasswordRequired.With()
        : password.Length > AccountLimits.MaxPasswordLength ? ServerMessages.StepAccountPasswordLength.With("max", AccountLimits.MaxPasswordLength)
        : null;

    public static bool IsDomain(string domain) =>
        domain.Length <= AccountLimits.MaxDomainLength && Uri.CheckHostName(domain) == UriHostNameType.Dns;

    // A server as a share path names it: a DNS or NetBIOS name, or an IPv4 address. Nothing else, such as the @SSL of
    // the WebDAV redirector or a port, so the name compared is the one Windows connects to.
    public static bool IsHost(string host) =>
        host.Length <= AccountLimits.MaxDomainLength && Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4;

    // User names, domains and servers ignore case, in Windows as here.
    public static bool Same(string? first, string? second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    public static bool Allows(IEnumerable<string> hosts, string host) => hosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    // The server of \\server\share, with folders after the share or not, or null when the path is not such a path. A
    // part that walks up (. or ..) or holds a character a share path cannot is refused, so the path an agent connects is
    // the one whose server was checked.
    public static string? ShareHost(string? path)
    {
        if (path is null || !path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        string[] parts = path[2..].Split('\\');

        if (parts.Length < 2
            || parts.Any(part => part.Length == 0 || part is "." or ".." || part.Any(char.IsControl) || part.AsSpan().IndexOfAny(s_forbiddenInSharePath) >= 0)
            || !IsHost(parts[0]))
        {
            return null;
        }

        return parts[0];
    }

    // The server part of a share path as it is written, before its template is filled in, or null when the path does not
    // start as a share path does.
    public static string? WrittenHost(string? path)
    {
        if (path is null || !path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        int end = path.IndexOf('\\', 2);

        return end < 0 ? path[2..] : path[2..end];
    }

    public static IReadOnlyList<string> ReadHosts(string? hosts) =>
        string.IsNullOrEmpty(hosts) ? [] : JsonSerializer.Deserialize(hosts, DdtJsonContext.Default.IReadOnlyListString) ?? [];

    public static string WriteHosts(IReadOnlyList<string> hosts) => JsonSerializer.Serialize(hosts, DdtJsonContext.Default.IReadOnlyListString);

    private static bool IsQualified(string userName)
    {
        string[] down = userName.Split('\\');
        string[] upn = userName.Split('@');

        return (down.Length == 2 && !string.IsNullOrWhiteSpace(down[0]) && !string.IsNullOrWhiteSpace(down[1]) && upn.Length == 1)
            || (upn.Length == 2 && !string.IsNullOrWhiteSpace(upn[0]) && !string.IsNullOrWhiteSpace(upn[1]) && down.Length == 1);
    }
}
