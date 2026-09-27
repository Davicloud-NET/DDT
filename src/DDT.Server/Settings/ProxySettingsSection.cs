// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization.Metadata;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using DDT.Server.Deployments;
using DDT.Server.Security;
using Microsoft.Extensions.Configuration;

namespace DDT.Server.Settings;

public sealed class ProxySettingsSection() : SettingsSectionDefinition<DdtForwardedHeadersOptions>(
    SettingsSectionNames.Proxies,
    DdtForwardedHeadersOptions.SectionName,
    SettingsSectionKind.Restart,
    [
        new("KnownProxies", reauthenticate: true),
        new("KnownNetworks", reauthenticate: true),
    ])
{
    protected override JsonTypeInfo<DdtForwardedHeadersOptions> TypeInfo => SettingsJsonContext.Default.DdtForwardedHeadersOptions;

    protected override DdtForwardedHeadersOptions? Bind(IConfigurationSection section) => section.Get<DdtForwardedHeadersOptions>();

    // A save of this section is refused when it overlaps the zero touch networks. At load the overlap is the machines
    // section's problem, which turns zero touch off instead of trusting nothing at all.
    protected override IReadOnlyList<SettingProblem> FindProblems(DdtForwardedHeadersOptions options, SettingsContext context)
    {
        IReadOnlyList<SettingProblem> problems = DdtForwardedHeadersExtensions.FindProblems(options);

        if (problems.Count > 0 || context.Saving != SettingsSectionNames.Proxies || ZeroTouchNetworks.FindProblems(context.Machines.ZeroTouchNetworks).Count > 0)
        {
            return problems;
        }

        return SettingsNetworks.Overlaps(context.Machines.ZeroTouchNetworks, options, null);
    }

    protected override IReadOnlyList<SettingWarning> FindWarnings(
        DdtForwardedHeadersOptions options,
        DdtForwardedHeadersOptions? current,
        SettingsContext context) =>
        SettingsNetworks.Wide(DdtForwardedHeadersExtensions.Networks(options.KnownNetworks), "KnownNetworks", "trusted proxy");
}
