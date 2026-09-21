// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers;
using DDT.Core.Unattend;

namespace DDT.Server.Deployments;

// A mistake here surfaces only at the first start of a deployed machine, long after DDT reported it done, so
// every one is refused at startup instead, all of them at once.
public static class DeploymentOptionsValidation
{
    private const int MaxAdministratorNameLength = 20;

    private static readonly SearchValues<char> s_forbiddenInAccountName = SearchValues.Create("\"/\\[]:;|=,+*?<>");

    public static void Validate(DeploymentOptions options)
    {
        IReadOnlyList<string> problems = FindProblems(options);

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"{DeploymentOptions.SectionName} is not valid:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
        }
    }

    public static IReadOnlyList<string> FindProblems(DeploymentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        const string section = DeploymentOptions.SectionName;
        List<string> problems = [];

        if (!string.IsNullOrWhiteSpace(options.TimeZone) && !WindowsTimeZones.IsValidId(options.TimeZone))
        {
            problems.Add(
                $"{section}:TimeZone '{options.TimeZone}' is not a Windows time zone id. Use a name that tzutil /l lists, " +
                "such as W. Europe Standard Time, or leave it empty so that Windows picks the zone of the locale.");
        }

        if (!string.IsNullOrEmpty(options.LocalAdministrator.Password) && !IsAccountName(options.LocalAdministrator.Name))
        {
            problems.Add(
                $"{section}:LocalAdministrator:Name '{options.LocalAdministrator.Name}' is not a valid account name. Use 1 to " +
                $"{MaxAdministratorNameLength} characters and none of \" / \\ [ ] : ; | = , + * ? < >.");
        }

        if (string.IsNullOrWhiteSpace(options.Domain.Name))
        {
            return problems;
        }

        DomainOptions domain = options.Domain;

        if (string.IsNullOrWhiteSpace(domain.UserName))
        {
            problems.Add(
                $"{section}:Domain:UserName must be set when {section}:Domain:Name is. Name the account that joins " +
                "the machines, as DOMAIN\\user or user@domain.example.");
        }
        else if (!IsQualifiedUserName(domain.UserName))
        {
            problems.Add($"{section}:Domain:UserName '{domain.UserName}' must be written as DOMAIN\\user or user@domain.example.");
        }

        if (string.IsNullOrEmpty(domain.Password))
        {
            problems.Add($"{section}:Domain:Password must be set when {section}:Domain:Name is.");
        }

        if (string.IsNullOrEmpty(options.LocalAdministrator.Password))
        {
            problems.Add(
                $"{section}:LocalAdministrator:Password must be set when {section}:Domain:Name is. Without a local " +
                "administrator, a domain machine stops at the account page of its first start.");
        }

        if (!string.IsNullOrWhiteSpace(domain.OrganizationalUnit) && OrganizationalUnitProblem(domain.OrganizationalUnit) is { } problem)
        {
            problems.Add(problem);
        }

        return problems;
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

    private static string? OrganizationalUnitProblem(string organizationalUnit)
    {
        string value = organizationalUnit.Trim();
        const string example = "OU=Workstations,DC=example,DC=com";

        if (value.StartsWith("LDAP://", StringComparison.OrdinalIgnoreCase))
        {
            return $"{DeploymentOptions.SectionName}:Domain:OrganizationalUnit must be a distinguished name without the LDAP:// prefix, such as {example}.";
        }

        bool distinguishedName =
            (value.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) || value.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            && value.Contains("DC=", StringComparison.OrdinalIgnoreCase);

        return distinguishedName
            ? null
            : $"{DeploymentOptions.SectionName}:Domain:OrganizationalUnit '{value}' is not a distinguished name. Write it like {example}.";
    }
}
