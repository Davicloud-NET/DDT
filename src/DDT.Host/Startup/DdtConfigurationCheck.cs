// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Configuration;
using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.Configuration;
using DDT.Server.Deployments;
using DDT.Server.Ldap;
using DDT.Server.Machines;
using DDT.Server.Security;
using DDT.Server.Settings;

namespace DDT.Host.Startup;

// Every configuration mistake DDT can recognise stops the server here, all of them in one message, before any
// section is used. A key nothing reads binds nothing, so a misspelled DDT:Machines:RequireWebApproval would
// otherwise leave web approval off without a word: every key under DDT has to be one DDT reads, whichever roles
// this process runs.
public static class DdtConfigurationCheck
{
    // Listed rather than bound with ErrorOnUnknownConfiguration, which on DdtOptions would refuse every section.
    private static readonly string[] s_rootKeys =
        ["Roles", "StorePath", "RequireHttps", "Https", "Deployment", "Machines", "Ldap", "Oidc", "ForwardedHeaders", "Pxe", "Agent"];

    public static void Validate(IConfiguration configuration, DdtOptions options, IReadOnlySet<DeploymentRole> roles)
    {
        IReadOnlyList<string> problems = FindProblems(configuration, options, roles);

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The configuration is not valid:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }
    }

    public static IReadOnlyList<string> FindProblems(IConfiguration configuration, DdtOptions options, IReadOnlySet<DeploymentRole> roles)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(roles);

        List<string> problems = [];

        foreach (IConfigurationSection child in configuration.GetSection(DdtOptions.SectionName).GetChildren())
        {
            if (!s_rootKeys.Contains(child.Key, StringComparer.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"{child.Path} is not a setting DDT reads. The keys under {DdtOptions.SectionName} are " +
                    $"{string.Join(", ", s_rootKeys)}.");
            }
        }

        // The binder stops at the first object with an unknown key, so the nested objects are read on their own, and
        // first: a section then reports the failure of its nested object again, which Report leaves out.
        _ = Read(
            configuration,
            $"{DeploymentOptions.SectionName}:{nameof(DeploymentOptions.LocalAdministrator)}",
            problems,
            (section, binder) => section.Get<LocalAdministratorOptions>(binder));
        _ = Read(
            configuration,
            $"{DeploymentOptions.SectionName}:{nameof(DeploymentOptions.Domain)}",
            problems,
            (section, binder) => section.Get<DomainOptions>(binder));

        foreach (IConfigurationSection target in configuration.GetSection($"{PxeOptions.SectionName}:{nameof(PxeOptions.BootTargets)}").GetChildren())
        {
            _ = Read(configuration, target.Path, problems, (section, binder) => section.Get<BootTargetOptions>(binder));
        }

        _ = Read(configuration, HttpsOptions.SectionName, problems, (section, binder) => section.Get<HttpsOptions>(binder));
        _ = Read(configuration, AgentReleaseOptions.SectionName, problems, (section, binder) => section.Get<AgentReleaseOptions>(binder));

        _ = Read(configuration, LdapOptions.SectionName, problems, (section, binder) => section.Get<LdapOptions>(binder) ?? new());
        _ = Read(configuration, OidcOptions.SectionName, problems, (section, binder) => section.Get<OidcOptions>(binder) ?? new());
        _ = Read(configuration, DeploymentOptions.SectionName, problems, (section, binder) => section.Get<DeploymentOptions>(binder) ?? new());
        _ = Read(configuration, MachineOptions.SectionName, problems, (section, binder) => section.Get<MachineOptions>(binder) ?? new());
        _ = Read(
            configuration,
            DdtForwardedHeadersOptions.SectionName,
            problems,
            (section, binder) => section.Get<DdtForwardedHeadersOptions>(binder) ?? new());
        PxeOptions? pxe = Read(
            configuration, PxeOptions.SectionName, problems, (section, binder) => section.Get<PxeOptions>(binder) ?? new());

        // The values of every settings section, as far as configuration sets them. The rest is on the settings page.
        problems.AddRange(ConfiguredSettings.FindProblems(configuration, roles.Contains(DeploymentRole.Pxe)));

        // What configuration alone decides for netboot, checked only where it is served.
        if (pxe is not null && roles.Contains(DeploymentRole.Pxe))
        {
            Add(problems, PxeOptions.SectionName, PxeSetup.FindBootDirectoryProblems(pxe, options.StorePath, configuration));
        }

        return problems;
    }

    private static void Strict(BinderOptions binder) => binder.ErrorOnUnknownConfiguration = true;

    // Bound with the concrete type at each call, because the binding generator cannot bind a type parameter. A section
    // with an unknown key is read again without the key check, so that the key does not hide its values. A section that
    // cannot be read at all has no values to check.
    private static T? Read<T>(
        IConfiguration configuration,
        string sectionName,
        List<string> problems,
        Func<IConfigurationSection, Action<BinderOptions>?, T?> bind)
        where T : class
    {
        IConfigurationSection section = configuration.GetSection(sectionName);

        try
        {
            return bind(section, Strict);
        }
        catch (InvalidOperationException exception)
        {
            Report(problems, section, exception);
        }

        try
        {
            return bind(section, null);
        }
        catch (InvalidOperationException exception)
        {
            Report(problems, section, exception);

            return null;
        }
    }

    // A value that cannot be converted fails both reads, and the failure of a nested object fails its section's read.
    private static void Report(List<string> problems, IConfigurationSection section, InvalidOperationException exception)
    {
        if (!problems.Exists(problem => problem.EndsWith(exception.Message, StringComparison.Ordinal)))
        {
            problems.Add($"{section.Path} could not be read. Correct or remove the setting this names: {exception.Message}");
        }
    }

    private static void Add(List<string> problems, string sectionName, IReadOnlyList<SettingProblem> found) =>
        problems.AddRange(found.Select(problem => problem.Describe(sectionName)));
}
