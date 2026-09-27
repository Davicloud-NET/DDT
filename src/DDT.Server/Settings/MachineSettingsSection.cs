// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json.Serialization.Metadata;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Core.Configuration;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Microsoft.Extensions.Configuration;

namespace DDT.Server.Settings;

public sealed class MachineSettingsSection() : SettingsSectionDefinition<MachineOptions>(
    SettingsSectionNames.Machines,
    MachineOptions.SectionName,
    SettingsSectionKind.Live,
    [
        new("RequireWebApproval"),
        new("MaxWaitingPerAddress"),
        new("MaxWaiting"),
        new("ZeroTouchNetworks", reauthenticate: true),
    ])
{
    protected override JsonTypeInfo<MachineOptions> TypeInfo => SettingsJsonContext.Default.MachineOptions;

    protected override MachineOptions? Bind(IConfigurationSection section) => section.Get<MachineOptions>();

    // The overlap with the proxies belongs to this section at load, so that it turns zero touch off rather than trust.
    protected override IReadOnlyList<SettingProblem> FindProblems(MachineOptions options, SettingsContext context)
    {
        List<SettingProblem> problems = [];

        if (options.MaxWaiting < 1)
        {
            problems.Add(new("MaxWaiting", ServerMessages.SettingsAtLeastOne.With()));
        }

        if (options.MaxWaitingPerAddress < 1 || options.MaxWaitingPerAddress > Math.Max(options.MaxWaiting, 1))
        {
            problems.Add(new("MaxWaitingPerAddress", ServerMessages.SettingsMachinesPerAddressRange.With("max", options.MaxWaiting)));
        }

        IReadOnlyList<SettingProblem> networks = ZeroTouchNetworks.FindProblems(options.ZeroTouchNetworks);
        problems.AddRange(networks);

        if (networks.Count == 0)
        {
            problems.AddRange(SettingsNetworks.Overlaps(options.ZeroTouchNetworks, context.Proxies, "ZeroTouchNetworks"));
        }

        return problems;
    }

    protected override IReadOnlyList<SettingWarning> FindWarnings(MachineOptions options, MachineOptions? current, SettingsContext context) =>
        SettingsNetworks.Wide(ZeroTouchNetworks.Networks(options.ZeroTouchNetworks), "ZeroTouchNetworks", ServerMessages.SettingsMachinesNetworkWide);
}
