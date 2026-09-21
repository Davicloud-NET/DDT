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

        _ = Read(configuration, HttpsOptions.SectionName, problems, section => section.Get<HttpsOptions>(Strict));
        _ = Read(configuration, LdapOptions.SectionName, problems, section => section.Get<LdapOptions>(Strict));
        _ = Read(configuration, AgentReleaseOptions.SectionName, problems, section => section.Get<AgentReleaseOptions>(Strict));

        OidcOptions? oidc = Read(
            configuration, OidcOptions.SectionName, problems, section => section.Get<OidcOptions>(Strict) ?? new());
        DeploymentOptions? deployment = Read(
            configuration, DeploymentOptions.SectionName, problems, section => section.Get<DeploymentOptions>(Strict) ?? new());
        MachineOptions? machines = Read(
            configuration, MachineOptions.SectionName, problems, section => section.Get<MachineOptions>(Strict) ?? new());
        DdtForwardedHeadersOptions? forwardedHeaders = Read(
            configuration, DdtForwardedHeadersOptions.SectionName, problems, section => section.Get<DdtForwardedHeadersOptions>(Strict) ?? new());
        PxeOptions? pxe = Read(
            configuration, PxeOptions.SectionName, problems, section => section.Get<PxeOptions>(Strict) ?? new());

        if (oidc is not null)
        {
            Add(problems, OidcOptions.SectionName, OidcOptionsValidation.FindProblems(oidc));
        }

        if (deployment is not null)
        {
            Add(problems, DeploymentOptions.SectionName, DeploymentOptionsValidation.FindProblems(deployment));
        }

        if (machines is not null)
        {
            Add(problems, MachineOptions.SectionName, ZeroTouchNetworks.FindProblems(machines.ZeroTouchNetworks));
        }

        if (forwardedHeaders is not null)
        {
            Add(problems, DdtForwardedHeadersOptions.SectionName, DdtForwardedHeadersExtensions.FindProblems(forwardedHeaders));
        }

        // Its values matter only to a process that serves netboot. Its keys are checked in every process.
        if (pxe is not null && roles.Contains(DeploymentRole.Pxe))
        {
            Add(problems, PxeOptions.SectionName, PxeSetup.FindProblems(pxe));
            Add(problems, PxeOptions.SectionName, PxeSetup.FindBootDirectoryProblems(pxe, options.StorePath, configuration));
        }

        return problems;
    }

    private static void Strict(BinderOptions binder) => binder.ErrorOnUnknownConfiguration = true;

    // Bound with the concrete type at each call, because the binding generator cannot bind a type parameter. A
    // section that cannot be read has no values to check.
    private static T? Read<T>(IConfiguration configuration, string sectionName, List<string> problems, Func<IConfigurationSection, T?> bind)
        where T : class
    {
        try
        {
            return bind(configuration.GetSection(sectionName));
        }
        catch (InvalidOperationException exception)
        {
            problems.Add($"{sectionName} could not be read. Correct or remove the setting this names: {exception.Message}");

            return null;
        }
    }

    private static void Add(List<string> problems, string sectionName, IReadOnlyList<SettingProblem> found) =>
        problems.AddRange(found.Select(problem => problem.Describe(sectionName)));
}
