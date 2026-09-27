// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Authentication;

// What a group map makes of an account's groups, the same for the directory and for single sign-on. Decides: the map
// has entries, so the groups decide the role at each sign-in and an account in none of them is refused. Matches: the
// account's groups the map names, with the role each gives. Role: the highest of those, which is the one it gets.
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

public sealed record GroupRole(string Group, string Role);
