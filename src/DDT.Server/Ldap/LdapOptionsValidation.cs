// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Configuration;
using DDT.Server.Authentication;

namespace DDT.Server.Ldap;

// The group map decides who may sign in at all, since a directory user in none of its groups is refused, so a map
// that cannot work stops the server instead of refusing everyone at their next sign-in.
public static class LdapOptionsValidation
{
    public static IReadOnlyList<SettingProblem> FindProblems(LdapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<SettingProblem> problems = [];

        foreach ((string group, string role) in options.GroupRoleMap)
        {
            if (DdtRoleNames.Canonical(role) is null)
            {
                problems.Add(new($"GroupRoleMap:{group}", $"'{role}' is not a DDT role. Use {string.Join(", ", DdtRoleNames.All)}."));
            }
        }

        // Without nested groups DDT reads no groups at all, so every directory user would be in none of the map's.
        if (options.GroupRoleMap.Count > 0 && !options.ResolveNestedGroups)
        {
            problems.Add(new(nameof(LdapOptions.ResolveNestedGroups), "false reads no groups, so every directory user would be refused. Turn it on, or empty GroupRoleMap."));
        }

        return problems;
    }
}
