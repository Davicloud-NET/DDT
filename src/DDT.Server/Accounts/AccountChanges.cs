// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Accounts;

// Describes what an account save changed, for its audit row. It never includes the password, only that it was set
// or cleared.
internal static class AccountChanges
{
    public static string Created(AccountFields fields, bool passwordSet)
    {
        ArgumentNullException.ThrowIfNull(fields);

        List<string> described =
        [
            $"userName '{fields.UserName}'",
            $"domain '{fields.Domain}'",
            $"hosts {Hosts(fields.Hosts)}",
            $"runAs {OnOff(fields.RunAs)}",
            .. passwordSet ? ["password set"] : Array.Empty<string>(),
        ];

        return string.Join(", ", described);
    }

    // Lists the changes that would send a kept password somewhere it wasn't entered for: another user name, domain or
    // server. Removing servers, even all of them, doesn't count.
    public static List<string> NewDestination(Account account, AccountFields fields)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(fields);

        IReadOnlyList<string> before = AccountRules.ReadHosts(account.Hosts);

        return
        [
            .. AccountRules.Same(account.UserName, fields.UserName) ? Array.Empty<string>() : ["userName"],
            .. AccountRules.Same(account.Domain, fields.Domain) ? Array.Empty<string>() : ["domain"],
            .. fields.Hosts.All(host => AccountRules.Allows(before, host)) ? Array.Empty<string>() : ["hosts"],
        ];
    }

    public static List<string> Of(Account account, AccountFields fields, bool passwordSet, bool passwordCleared)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(fields);

        List<string> changes = [];

        if (account.Name != fields.Name)
        {
            changes.Add($"name '{account.Name}' to '{fields.Name}'");
        }

        if (account.UserName != fields.UserName)
        {
            changes.Add($"userName '{account.UserName}' to '{fields.UserName}'");
        }

        if (account.Domain != fields.Domain)
        {
            changes.Add($"domain '{account.Domain}' to '{fields.Domain}'");
        }

        changes.AddRange(HostChanges(AccountRules.ReadHosts(account.Hosts), fields.Hosts));

        if (account.RunAs != fields.RunAs)
        {
            changes.Add($"runAs {OnOff(fields.RunAs)}");
        }

        if (passwordSet)
        {
            changes.Add("password set");
        }
        else if (passwordCleared)
        {
            changes.Add("password cleared");
        }

        return changes;
    }

    private static IEnumerable<string> HostChanges(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        string[] added = [.. after.Where(host => !before.Contains(host, StringComparer.Ordinal))];
        string[] removed = [.. before.Where(host => !after.Contains(host, StringComparer.Ordinal))];

        if (added.Length > 0)
        {
            yield return $"hosts added {Hosts(added)}";
        }

        if (removed.Length > 0)
        {
            yield return $"hosts removed {Hosts(removed)}";
        }

        if (added.Length == 0 && removed.Length == 0 && !before.SequenceEqual(after, StringComparer.Ordinal))
        {
            yield return $"hosts in the order {Hosts(after)}";
        }
    }

    private static string Hosts(IReadOnlyList<string> hosts) => hosts.Count == 0 ? "none" : string.Join(" ", hosts);

    private static string OnOff(bool value) => value ? "on" : "off";
}
