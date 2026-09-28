// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers;
using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Messages;

namespace DDT.Server.Accounts;

// Rules for what an account may hold, and for finding the server in a share path. The Accounts page, account inputs
// and every share a step connects use them. So a password never goes to a server its account doesn't list.
public static class AccountRules
{
    // Control characters are forbidden too, but they're checked separately.
    private static readonly SearchValues<char> s_forbiddenInSharePath = SearchValues.Create("/:*?\"<>|");

    public static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Must be DOMAIN\user or user@domain, which a share connection and a logon take as is. A plain user name would be
    // looked up on whatever machine the step runs on.
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

    // A server as a share path names it: a DNS or NetBIOS name, or an IPv4 address. Extras like the WebDAV redirector's
    // @SSL or a port are refused, so the name we compare is the one Windows connects to.
    public static bool IsHost(string host) =>
        host.Length <= AccountLimits.MaxDomainLength && Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4;

    // User names, domains and servers are case-insensitive in Windows, so they are here too.
    public static bool Same(string? first, string? second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    public static bool Allows(IEnumerable<string> hosts, string host) => hosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    // The server of \\server\share, with or without folders, or null. A . or .. part, or a character no share path can
    // hold, is refused. That way the agent connects the same path whose server was checked.
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

    // The server part of a share path as written, before its template is filled in. Null if the path doesn't start
    // with two backslashes.
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
