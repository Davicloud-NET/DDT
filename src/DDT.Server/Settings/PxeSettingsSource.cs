// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts.Messages;
using DDT.Pxe;
using DDT.Server.Data;
using Microsoft.Extensions.DependencyInjection;

namespace DDT.Server.Settings;

// Connects the PXE listeners to the settings. It passes what the snapshot says to serve, the snapshot's change token,
// and where each result goes. PxeHost only applies again when the PXE section's version changes.
public static class PxeSettingsSource
{
    public static PxeHostSource Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        DdtSettings settings = services.GetRequiredService<DdtSettings>();
        SettingsHostStates hostStates = services.GetRequiredService<SettingsHostStates>();

        return new PxeHostSource(
            () => Desired(settings.Current),
            settings.GetChangeToken,
            result =>
            {
                hostStates.Record(new SettingsApplyReport(
                    SettingsSectionNames.Pxe,
                    result.Version,
                    result.Succeeded ? SettingsApplyResult.Applied : SettingsApplyResult.Failed,
                    result.Message)
                {
                    Detail = Detail(result.Interfaces),
                    Text = result.Text,
                });

                return Task.CompletedTask;
            });
    }

    // While the section has problems nothing is served. When configuration names the interfaces, a bind failure at
    // startup stops the host.
    public static PxeDesiredSetup Desired(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        SettingsSectionState state = snapshot[SettingsSectionNames.Pxe];

        return new PxeDesiredSetup(
            state.Version,
            snapshot.Pxe,
            state.Closed
                ? ServerMessages.SettingsApplyPxeClosed.With(
                    "problems",
                    ServerMessages.Sentences([.. state.Problems.Select(problem => ServerMessages.SettingsFieldProblem.With(
                        "field",
                        $"{PxeOptions.SectionName}:{problem.Field}",
                        "problem",
                        problem.Text))]))
                : null,
            state.IsLocked(SettingsDefinitions.Pxe.Field("interfaces")!));
    }

    private static string? Detail(NetworkInterfaceMap? interfaces) =>
        interfaces is null
            ? null
            : JsonSerializer.Serialize(
                new PxeHostDetail(
                    [.. interfaces.Candidates.Select(candidate => new PxeHostCandidate(
                        candidate.Name,
                        [.. candidate.Addresses.Select(address => address.ToString())],
                        interfaces.Served.Any(served => served.Index == candidate.Index)))],
                    interfaces.Unmatched),
                SettingsJsonContext.Default.PxeHostDetail);
}
