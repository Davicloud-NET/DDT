// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.Applications;
using DDT.Contracts.Messages;

namespace DDT.Core.Applications;

// Applications and bundles share one namespace, unique by Normalize. Rules and answers name them in a ; list.
public static class ApplicationNames
{
    public const char Separator = ';';

    // Trims the name and turns every run of white space into one space. Null for an empty name.
    public static string? Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        StringBuilder cleaned = new(name.Length);

        foreach (string part in name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            cleaned.Append(cleaned.Length > 0 ? " " : "").Append(part);
        }

        return cleaned.ToString();
    }

    // The key two names are compared by.
    public static string? Normalize(string? name) => Clean(name)?.ToUpperInvariant();

    // Control characters are judged before cleaning, which would turn a tab or line break into a space.
    public static ServerMessage? Problem(string? name)
    {
        if (name is null || Clean(name) is not { } cleaned || cleaned.Length > ApplicationLimits.MaxNameLength || name.Any(char.IsControl))
        {
            return ServerMessages.NameLength.With("max", ApplicationLimits.MaxNameLength);
        }

        return cleaned.Contains(Separator, StringComparison.Ordinal) ? ServerMessages.ApplicationNameSemicolon.With() : null;
    }
}
