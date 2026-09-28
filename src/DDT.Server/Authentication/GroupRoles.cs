// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Authentication;

// What a group map makes of an account's groups, for LDAP and OIDC alike. Decides: the map has entries, so the groups
// set the role at each sign-in and an account in none of them is refused. Role is the highest of Matches.
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
