// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers;
using System.Globalization;
using DDT.Contracts.Messages;
using DDT.Core.Configuration;
using DDT.Core.Unattend;

namespace DDT.Server.Deployments;

// A mistake here surfaces only at the first start of a deployed machine, long after DDT reported it done, so
// every one is refused at startup instead, all of them at once.
public static class DeploymentOptionsValidation
{
    private const int MaxAdministratorNameLength = 20;

    private static readonly SearchValues<char> s_forbiddenInAccountName = SearchValues.Create("\"/\\[]:;|=,+*?<>");

    private static readonly bool s_culturesKnown = KnowsCultures();

    public static IReadOnlyList<SettingProblem> FindProblems(DeploymentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<SettingProblem> problems = [];

        if (!string.IsNullOrWhiteSpace(options.TimeZone) && !WindowsTimeZones.IsValidId(options.TimeZone))
        {
            problems.Add(new("TimeZone", ServerMessages.SettingsDeploymentTimeZoneUnknown.With("value", options.TimeZone)));
        }

        if (!string.IsNullOrWhiteSpace(options.Locale) && !IsCulture(options.Locale.Trim()))
        {
            problems.Add(new("Locale", ServerMessages.SettingsDeploymentLocaleUnknown.With("value", options.Locale)));
        }

        if (!string.IsNullOrEmpty(options.LocalAdministrator.Password) && !IsAccountName(options.LocalAdministrator.Name))
        {
            problems.Add(new(
                "LocalAdministrator:Name",
                ServerMessages.SettingsDeploymentAdministratorNameInvalid.With(
                    "value",
                    options.LocalAdministrator.Name ?? string.Empty,
                    "max",
                    MaxAdministratorNameLength)));
        }

        if (string.IsNullOrWhiteSpace(options.Domain.Name))
        {
            return problems;
        }

        DomainOptions domain = options.Domain;

        if (string.IsNullOrWhiteSpace(domain.UserName))
        {
            problems.Add(new("Domain:UserName", ServerMessages.SettingsDeploymentDomainUserNameRequired.With()));
        }
        else if (!IsQualifiedUserName(domain.UserName))
        {
            problems.Add(new("Domain:UserName", ServerMessages.SettingsDeploymentDomainUserNameForm.With("value", domain.UserName)));
        }

        if (string.IsNullOrEmpty(domain.Password))
        {
            problems.Add(new("Domain:Password", ServerMessages.SettingsDeploymentRequiredWithDomain.With()));
        }

        if (string.IsNullOrEmpty(options.LocalAdministrator.Password))
        {
            problems.Add(new("LocalAdministrator:Password", ServerMessages.SettingsDeploymentAdministratorPasswordRequired.With()));
        }

        if (!string.IsNullOrWhiteSpace(domain.OrganizationalUnit) && OrganizationalUnitMessage(domain.OrganizationalUnit) is { } problem)
        {
            problems.Add(new("Domain:OrganizationalUnit", problem));
        }

        if (!string.IsNullOrWhiteSpace(domain.Controller) && Uri.CheckHostName(domain.Controller.Trim()) == UriHostNameType.Unknown)
        {
            problems.Add(new("Domain:Controller", ServerMessages.SettingsDeploymentControllerInvalid.With("value", domain.Controller)));
        }

        return problems;
    }

    // Without the culture data of the operating system, as in a globalization invariant build, no name can be checked,
    // and every one is let through.
    private static bool IsCulture(string name)
    {
        try
        {
            _ = CultureInfo.GetCultureInfo(name, predefinedOnly: true);

            return true;
        }
        catch (CultureNotFoundException)
        {
            return !s_culturesKnown;
        }
    }

    private static bool KnowsCultures()
    {
        try
        {
            _ = CultureInfo.GetCultureInfo("de-DE", predefinedOnly: true);

            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static bool IsAccountName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Length <= MaxAdministratorNameLength
        && name.AsSpan().IndexOfAny(s_forbiddenInAccountName) < 0;

    // Setup takes the name whole in Credentials/Username, which only works in a qualified form.
    private static bool IsQualifiedUserName(string userName)
    {
        string[] down = userName.Split('\\');
        string[] upn = userName.Split('@');

        return (down.Length == 2 && !string.IsNullOrWhiteSpace(down[0]) && !string.IsNullOrWhiteSpace(down[1]) && upn.Length == 1)
            || (upn.Length == 2 && !string.IsNullOrWhiteSpace(upn[0]) && !string.IsNullOrWhiteSpace(upn[1]) && down.Length == 1);
    }

    internal static ServerMessage? OrganizationalUnitMessage(string organizationalUnit)
    {
        string value = organizationalUnit.Trim();

        if (value.StartsWith("LDAP://", StringComparison.OrdinalIgnoreCase))
        {
            return ServerMessages.OrganizationalUnitWithPrefix.With();
        }

        // The join can only name an organizational unit, and new computers land in the Computers container anyway.
        if (value.StartsWith("CN=Computers,", StringComparison.OrdinalIgnoreCase))
        {
            return ServerMessages.OrganizationalUnitIsComputers.With();
        }

        bool distinguishedName =
            (value.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) || value.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            && value.Contains("DC=", StringComparison.OrdinalIgnoreCase);

        return distinguishedName ? null : ServerMessages.OrganizationalUnitNotDistinguished.With("value", value);
    }
}
