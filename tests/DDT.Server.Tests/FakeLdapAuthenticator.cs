// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using DDT.Server.Ldap;

namespace DDT.Server.Tests;

// A directory where every user exists, is in the operators group and has the test password, unless a test gives it
// other groups. A user name that starts with "nobody-" matches no entry, and one that starts with "twin-" matches two.
public sealed class FakeLdapAuthenticator : ILdapAuthenticator
{
    // No "=" as in a real DN: the test host passes settings to Program as --key=value arguments.
    public const string OperatorsGroup = "operators";
    public const string AdministratorsGroup = "administrators";
    public const string ViewersGroup = "viewers";

    // In the directory, but in no map.
    public const string UnmappedGroup = "printers";

    // In the map, but no longer in the directory.
    public const string RetiredGroup = "retired";

    public const string MissingPrefix = "nobody-";
    public const string TwinPrefix = "twin-";

    public static IReadOnlyList<LdapGroup> DirectoryGroups { get; } =
    [
        new(AdministratorsGroup, "DDT Administrators", "Run DDT"),
        new(OperatorsGroup, "DDT Operators", "Deploy machines"),
        new(ViewersGroup, "DDT Viewers", null),
        new(UnmappedGroup, "Operators of printers", null),
    ];

    // Groups a user is in instead of the operators group, by user name.
    public ConcurrentDictionary<string, IReadOnlyList<string>> Groups { get; } = new(StringComparer.OrdinalIgnoreCase);

    // While set, the directory cannot be asked.
    public bool Unavailable { get; set; }

    // Runs once inside the next bind, which is the slow step a race has to hit.
    public Func<Task>? DuringBind { get; set; }

    public static string ImmutableIdOf(string userName) => $"id-{userName.ToLowerInvariant()}";

    public static string DistinguishedNameOf(string userName) => $"cn={userName}";

    public async Task<LdapIdentity?> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
    {
        if (DuringBind is { } hook)
        {
            DuringBind = null;
            await hook();
        }

        return password == DdtApplication.Password && Exists(userName)
            ? new LdapIdentity(ImmutableIdOf(userName), DistinguishedNameOf(userName), userName, userName, null, GroupsOf(userName))
            : null;
    }

    public Task<LdapLookup> LookUpAsync(string userName, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();

        return Task.FromResult(userName switch
        {
            _ when userName.StartsWith(MissingPrefix, StringComparison.Ordinal) => LdapLookup.Missing(LdapLookupStatus.NotFound),
            _ when userName.StartsWith(TwinPrefix, StringComparison.Ordinal) => LdapLookup.Missing(LdapLookupStatus.Ambiguous),
            _ => new LdapLookup(LdapLookupStatus.Found, DistinguishedNameOf(userName), userName, ImmutableIdOf(userName), GroupsOf(userName)),
        });
    }

    public Task<IReadOnlyList<LdapGroup>> SearchGroupsAsync(string text, int limit, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();

        return Task.FromResult<IReadOnlyList<LdapGroup>>(
        [
            .. DirectoryGroups
                .Where(group => group.Name!.Contains(text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(group => group.Name!.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .Take(limit),
        ]);
    }

    public Task<IReadOnlyDictionary<string, string?>> GroupNamesAsync(IReadOnlyCollection<string> distinguishedNames, CancellationToken cancellationToken)
    {
        ThrowIfUnavailable();

        return Task.FromResult<IReadOnlyDictionary<string, string?>>(distinguishedNames.ToDictionary(
            distinguishedName => distinguishedName,
            distinguishedName => DirectoryGroups.FirstOrDefault(group => string.Equals(group.DistinguishedName, distinguishedName, StringComparison.OrdinalIgnoreCase))?.Name,
            StringComparer.OrdinalIgnoreCase));
    }

    private static bool Exists(string userName) =>
        !userName.StartsWith(MissingPrefix, StringComparison.Ordinal) && !userName.StartsWith(TwinPrefix, StringComparison.Ordinal);

    private IReadOnlyList<string> GroupsOf(string userName) => Groups.TryGetValue(userName, out IReadOnlyList<string>? groups) ? groups : [OperatorsGroup];

    private void ThrowIfUnavailable()
    {
        if (Unavailable)
        {
            throw new LdapUnavailableException("The directory at dc.corp.example:636 could not be reached: The LDAP server is unavailable.");
        }
    }
}
