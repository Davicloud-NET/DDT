// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Authentication;

namespace DDT.Server.Tokens;

// Keeps the roles in order, so a token can be held to the lower of its own role and its user's role. Each role may do
// everything the roles below it may. The policies list them the same way.
public static class ApiTokenRoles
{
    private static readonly string[] s_ascending = [DdtRoleNames.Viewer, DdtRoleNames.Operator, DdtRoleNames.Administrator];

    // Returns the role's name as DDT spells it, or null for a role DDT doesn't know.
    public static string? Known(string? role) =>
        s_ascending.FirstOrDefault(known => string.Equals(known, role?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string? Highest(IEnumerable<string?> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        return roles.Select(Known).OfType<string>().MaxBy(Rank);
    }

    public static string? Lower(string? first, string? second) =>
        Known(first) is not { } a || Known(second) is not { } b ? null
        : Rank(a) <= Rank(b) ? a
        : b;

    private static int Rank(string role) => Array.IndexOf(s_ascending, role);
}
