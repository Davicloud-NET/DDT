// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Authentication;

// The result of applying a group map to an account's groups, for both LDAP and OIDC. Decides is true when the map has
// entries. Then the groups set the role at each sign-in, and an account in none of them is refused. Role is the highest
// role in Matches.
public sealed record GroupRoles(bool Decides, IReadOnlyList<GroupRole> Matches, string? Role)
{
    // The map's own comparer decides how groups match.
    public static GroupRoles From(IEnumerable<string> groups, IReadOnlyDictionary<string, string> map)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(map);

        List<GroupRole> matches = [];

        foreach (string group in groups.Distinct(StringComparer.Ordinal))
        {
            if (map.TryGetValue(group, out string? role) && DdtRoleNames.Canonical(role) is { } known)
            {
                matches.Add(new GroupRole(group, known));
            }
        }

        return new GroupRoles(map.Count > 0, matches, DdtRoleNames.Highest(matches.Select(match => match.Role)));
    }
}
