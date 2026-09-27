// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Core.Configuration;
using DDT.Server.Authentication;
using DDT.Server.Settings;

namespace DDT.Server.Ldap;

// The group map decides who may sign in at all, since a directory user in none of its groups is refused, so a map
// that cannot work is refused instead of refusing everyone at their next sign-in. The connection values are checked
// only while directory sign-in is on, so a section that is off can be filled in step by step.
public static class LdapOptionsValidation
{
    public static IReadOnlyList<SettingProblem> FindProblems(LdapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<SettingProblem> problems = [];

        if (options.Enabled && string.IsNullOrWhiteSpace(options.Host))
        {
            problems.Add(new(nameof(LdapOptions.Host), "Required while directory sign-in is on. Name the directory server, such as dc1.corp.example."));
        }

        if (options.Port is < 1 or > 65535)
        {
            problems.Add(new(nameof(LdapOptions.Port), $"{options.Port} is not a port number. LDAPS uses 636, and StartTLS 389."));
        }

        if (!Enum.IsDefined(options.Transport))
        {
            problems.Add(new(nameof(LdapOptions.Transport), "Must be Ldaps, StartTls or UnencryptedDangerous."));
        }

        if (!options.UserFilter.Contains("{0}", StringComparison.Ordinal))
        {
            problems.Add(new(
                nameof(LdapOptions.UserFilter),
                "Must contain {0}, which DDT replaces with the user name, such as (&(objectClass=user)(sAMAccountName={0}))."));
        }
        else if (!Formats(options.UserFilter))
        {
            problems.Add(new(nameof(LdapOptions.UserFilter), "Is not a valid template: a brace that is not part of {0} has to be written twice, as {{ or }}."));
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            problems.Add(new(nameof(LdapOptions.Timeout), "Must be longer than zero, such as 00:00:10."));
        }

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

    // Current is what applies before a save; the re-key warning is about the change itself.
    public static IReadOnlyList<SettingWarning> FindWarnings(LdapOptions options, LdapOptions? current)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<SettingWarning> warnings = [];

        if (options.Transport == LdapTransport.UnencryptedDangerous)
        {
            warnings.Add(new(
                nameof(LdapOptions.Transport),
                "The bind password and every password typed at sign-in cross the network in clear text.",
                SettingWarningCodes.LdapUnencrypted));
        }

        if (current is not null && !string.Equals(current.ImmutableIdAttribute, options.ImmutableIdAttribute, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new(
                nameof(LdapOptions.ImmutableIdAttribute),
                $"Every directory account is keyed on {current.ImmutableIdAttribute}. With {options.ImmutableIdAttribute} each " +
                "one is taken for a new person at its next sign-in, and its roles and history stay with the old account.",
                SettingWarningCodes.LdapRekey));
        }

        if (options.GroupRoleMap.Count > 0
            && !options.GroupRoleMap.Values.Any(role => DdtRoleNames.Canonical(role) == DdtRoleNames.Administrator))
        {
            warnings.Add(new(
                nameof(LdapOptions.GroupRoleMap),
                "No group maps to Administrator, so every directory account that is an administrator loses the role at its next sign-in.",
                SettingWarningCodes.LdapNoAdministrator));
        }

        return warnings;
    }

    private static bool Formats(string filter)
    {
        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, filter, "user");

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
