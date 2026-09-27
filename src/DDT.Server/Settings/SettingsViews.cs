// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Data;
using DDT.Server.Live;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Settings;

// The page's view of a section: the snapshot, and for a section applied by rebuilding a component, how every host
// applied it. A change reaches other browsers through the hub with the whole view, so they patch what they show.
public sealed class SettingsViews(DdtDbContext database, DdtSettings settings, SettingsHostStates hostStates, LiveNotifier live)
{
    public async Task<object> ViewAsync(SettingsSectionApi api, SettingsSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(snapshot);

        SettingsSectionState state = snapshot[api.Name];
        List<SettingsHostState> rows = await RowsAsync(api.Name, cancellationToken).ConfigureAwait(false);

        return api.View(state, Warnings(state, rows), Apply(state, rows));
    }

    public async Task<SettingsSectionView<TValues>> ViewAsync<TValues>(SettingsSectionApi<TValues> api, SettingsSnapshot snapshot, CancellationToken cancellationToken)
        where TValues : class
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(snapshot);

        SettingsSectionState state = snapshot[api.Name];
        List<SettingsHostState> rows = await RowsAsync(api.Name, cancellationToken).ConfigureAwait(false);

        return api.TypedView(state, Warnings(state, rows), Apply(state, rows));
    }

    public async Task<IReadOnlyList<SettingsSectionSummary>> SummariesAsync(SettingsSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        List<SettingsSectionSummary> summaries = [];

        foreach (SettingsSectionApi api in SettingsApi.All)
        {
            SettingsSectionState state = snapshot[api.Name];
            List<SettingsHostState> rows = await RowsAsync(api.Name, cancellationToken).ConfigureAwait(false);

            summaries.Add(new SettingsSectionSummary(
                api.Name,
                api.Definition.Kind,
                state.Version,
                state.UpdatedUtc,
                state.UpdatedBy,
                state.Locks.Count,
                state.Problems.Count,
                Apply(state, rows)));
        }

        return summaries;
    }

    public async Task<IReadOnlyList<PxeHostInterfaces>> PxeInterfacesAsync(CancellationToken cancellationToken)
    {
        List<SettingsHostState> rows = await RowsAsync(SettingsSectionNames.Pxe, cancellationToken).ConfigureAwait(false);

        List<PxeHostInterfaces> hosts = [];

        foreach (SettingsHostState row in rows)
        {
            if (Detail(row) is { } detail)
            {
                hosts.Add(new PxeHostInterfaces(
                    row.Host,
                    row.UpdatedUtc,
                    [.. detail.Candidates.Select(candidate => new PxeInterface(candidate.Name, candidate.Addresses, candidate.Served))],
                    detail.Unmatched));
            }
        }

        return hosts;
    }

    // Administrators receive every section; operators also the two they may read.
    public async Task PushAsync(string section, CancellationToken cancellationToken)
    {
        if (SettingsApi.Find(section) is not { } api)
        {
            return;
        }

        object view = await ViewAsync(api, settings.Current, cancellationToken).ConfigureAwait(false);
        live.SettingsChanged(view, api.OperatorsMayRead);

        // A host reports its interfaces with its apply result, so the list changes whenever the section's states do.
        if (section == SettingsSectionNames.Pxe)
        {
            live.PxeInterfacesChanged(await PxeInterfacesAsync(cancellationToken).ConfigureAwait(false));
        }
    }

    // This host's own state from memory, which may be newer than its row.
    private async Task<List<SettingsHostState>> RowsAsync(string section, CancellationToken cancellationToken)
    {
        List<SettingsHostState> rows = await database.SettingsHostStates
            .AsNoTracking()
            .Where(row => row.Section == section)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (hostStates.Local(section) is { } local)
        {
            rows.RemoveAll(row => row.Host == local.Host);
            rows.Add(local);
        }

        return [.. rows.OrderBy(row => row.Host, StringComparer.Ordinal)];
    }

    private static List<SettingApplyState>? Apply(SettingsSectionState state, List<SettingsHostState> rows) =>
        state.Definition.Kind == SettingsSectionKind.Live
            ? null
            : [.. rows.Select(row => new SettingApplyState(
                row.Host,
                row.AppliedVersion,
                row.AppliedVersion < state.Version
                    ? SettingApplyStatus.Pending
                    : row.State == SettingsApplyResult.Applied ? SettingApplyStatus.Applied : SettingApplyStatus.Failed,
                row.Message,
                row.UpdatedUtc,
                SettingsHostStates.Text(row)))];

    // An interface name that matches nothing is only logged by the host, so the page says which host did not find it.
    private static List<SettingMessage> Warnings(SettingsSectionState state, List<SettingsHostState> rows) =>
        state.Name != SettingsSectionNames.Pxe
            ? []
            : [.. rows
                .Select(row => (row.Host, Detail: Detail(row)))
                .Where(entry => entry.Detail is not null)
                .SelectMany(entry => entry.Detail!.Unmatched.Select(name => Message(
                    "interfaces",
                    ServerMessages.SettingsPxeInterfaceNotFound.With("name", name, "host", entry.Host),
                    SettingWarningCodes.PxeInterfaceNotFound)))];

    private static SettingMessage Message(string field, ServerMessage text, string? code) => new(field, text.Text, code, text);

    private static PxeHostDetail? Detail(SettingsHostState row)
    {
        if (row.Detail is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(row.Detail, SettingsJsonContext.Default.PxeHostDetail);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
