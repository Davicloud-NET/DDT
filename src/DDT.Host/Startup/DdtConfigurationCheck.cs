// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.Configuration;
using DDT.Server.Deployments;
using DDT.Server.Ldap;
using DDT.Server.Machines;
using DDT.Server.Security;

namespace DDT.Host.Startup;

// A key nothing reads binds nothing, so a misspelled DDT:Machines:RequireWebApproval would leave web approval off
// without a word. Every key under DDT has to be one DDT reads, whichever roles this process runs.
public static class DdtConfigurationCheck
{
    // Listed rather than bound with ErrorOnUnknownConfiguration, which on DdtOptions would refuse every section.
    private static readonly string[] s_rootKeys =
        ["Roles", "StorePath", "RequireHttps", "Https", "Deployment", "Machines", "Ldap", "Oidc", "ForwardedHeaders", "Pxe", "Agent"];

    public static void Validate(IConfiguration configuration)
    {
        IReadOnlyList<string> problems = FindUnknownKeys(configuration);

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "The configuration is not valid:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
        }
    }

    public static IReadOnlyList<string> FindUnknownKeys(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

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

        Check(configuration, HttpsOptions.SectionName, problems, section => section.Get<HttpsOptions>(Strict));
        Check(configuration, DeploymentOptions.SectionName, problems, section => section.Get<DeploymentOptions>(Strict));
        Check(configuration, MachineOptions.SectionName, problems, section => section.Get<MachineOptions>(Strict));
        Check(configuration, LdapOptions.SectionName, problems, section => section.Get<LdapOptions>(Strict));
        Check(configuration, OidcOptions.SectionName, problems, section => section.Get<OidcOptions>(Strict));
        Check(configuration, DdtForwardedHeadersOptions.SectionName, problems, section => section.Get<DdtForwardedHeadersOptions>(Strict));
        Check(configuration, PxeOptions.SectionName, problems, section => section.Get<PxeOptions>(Strict));
        Check(configuration, AgentReleaseOptions.SectionName, problems, section => section.Get<AgentReleaseOptions>(Strict));

        return problems;
    }

    private static void Strict(BinderOptions binder) => binder.ErrorOnUnknownConfiguration = true;

    // Bound with the concrete type at each call, because the binding generator cannot bind a type parameter.
    private static void Check(IConfiguration configuration, string sectionName, List<string> problems, Action<IConfigurationSection> bind)
    {
        try
        {
            bind(configuration.GetSection(sectionName));
        }
        catch (InvalidOperationException exception)
        {
            problems.Add($"{sectionName} could not be read. Correct or remove the setting this names: {exception.Message}");
        }
    }
}
