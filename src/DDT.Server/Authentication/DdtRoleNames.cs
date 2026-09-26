// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Authentication;

public static class DdtRoleNames
{
    public const string Administrator = "Administrator";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";

    // Highest first.
    public static IReadOnlyList<string> All { get; } = [Administrator, Operator, Viewer];

    // An account shows one role, and a group map gives one: the highest of those it has, in its canonical spelling.
    // Names that are not DDT roles are passed over.
    public static string? Highest(IEnumerable<string?> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        HashSet<string> held = new(roles.OfType<string>(), StringComparer.OrdinalIgnoreCase);

        return All.FirstOrDefault(held.Contains);
    }

    public static string? Canonical(string? role) => All.FirstOrDefault(known => string.Equals(known, role?.Trim(), StringComparison.OrdinalIgnoreCase));
}
