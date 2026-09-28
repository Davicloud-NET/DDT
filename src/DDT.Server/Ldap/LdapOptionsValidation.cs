// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Contracts.Messages;
using DDT.Core.Configuration;
using DDT.Server.Authentication;
using DDT.Server.Settings;

namespace DDT.Server.Ldap;

// A directory user in none of the map's groups is refused, so a map that cannot work is refused now rather than
// everyone at their next sign-in. The connection is checked only while directory sign-in is on, so a section that is
// off can be filled in step by step.
public static class LdapOptionsValidation
{
    // What DDT replaces with the user name in the user filter.
    private const string Placeholder = "{0}";

    public static IReadOnlyList<SettingProblem> FindProblems(LdapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<SettingProblem> problems = [];

        if (options.Enabled && string.IsNullOrWhiteSpace(options.Host))
        {
            problems.Add(new(nameof(LdapOptions.Host), ServerMessages.SettingsLdapHostRequired.With()));
        }

        if (options.Port is < 1 or > 65535)
        {
            problems.Add(new(nameof(LdapOptions.Port), ServerMessages.SettingsLdapPortInvalid.With("port", options.Port)));
        }

        if (!Enum.IsDefined(options.Transport))
        {
            problems.Add(new(nameof(LdapOptions.Transport), ServerMessages.SettingsLdapTransportInvalid.With()));
        }

        if (!options.UserFilter.Contains(Placeholder, StringComparison.Ordinal))
        {
            problems.Add(new(nameof(LdapOptions.UserFilter), ServerMessages.SettingsLdapUserFilterPlaceholder.With("placeholder", Placeholder)));
        }
        else if (!Formats(options.UserFilter))
        {
            problems.Add(new(
                nameof(LdapOptions.UserFilter),
                ServerMessages.SettingsLdapUserFilterBraces.With("placeholder", Placeholder, "open", "{{", "close", "}}")));
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            problems.Add(new(nameof(LdapOptions.Timeout), ServerMessages.SettingsLdapTimeoutNotPositive.With()));
        }

        foreach ((string group, string role) in options.GroupRoleMap)
        {
            if (DdtRoleNames.Canonical(role) is null)
            {
                problems.Add(new($"GroupRoleMap:{group}", ServerMessages.SettingsRoleUnknown.With("role", role, "roles", string.Join(", ", DdtRoleNames.All))));
            }
        }

        // Without nested groups DDT reads no groups at all, so every directory user would be in none of the map's.
        if (options.GroupRoleMap.Count > 0 && !options.ResolveNestedGroups)
        {
            problems.Add(new(nameof(LdapOptions.ResolveNestedGroups), ServerMessages.SettingsLdapNestedGroupsOff.With()));
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
            warnings.Add(new(nameof(LdapOptions.Transport), ServerMessages.SettingsLdapUnencrypted.With(), SettingWarningCodes.LdapUnencrypted));
        }

        if (current is not null && !string.Equals(current.ImmutableIdAttribute, options.ImmutableIdAttribute, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new(
                nameof(LdapOptions.ImmutableIdAttribute),
                ServerMessages.SettingsLdapRekey.With("current", current.ImmutableIdAttribute, "next", options.ImmutableIdAttribute),
                SettingWarningCodes.LdapRekey));
        }

        if (options.GroupRoleMap.Count > 0
            && !options.GroupRoleMap.Values.Any(role => DdtRoleNames.Canonical(role) == DdtRoleNames.Administrator))
        {
            warnings.Add(new(nameof(LdapOptions.GroupRoleMap), ServerMessages.SettingsLdapNoAdministrator.With(), SettingWarningCodes.LdapNoAdministrator));
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
