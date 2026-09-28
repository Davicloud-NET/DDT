// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Settings;
using DDT.Server.Data;
using DDT.Server.Live;

namespace DDT.Server.Settings;

// A save of a section from the settings page. It answers with the section as it applies after the save, and every other
// browser receives the same view through the hub.
internal sealed class SettingsSaves(
    SettingsStore store,
    DdtSettings settings,
    SettingsViews views,
    SettingsHostStates hostStates,
    LiveNotifier live)
{
    // A subsystem this process rebuilds is quick, so a save waits this long for it before it answers Pending.
    private static readonly TimeSpan s_applyWait = TimeSpan.FromSeconds(5);

    public async Task<SettingsSaved<TValues>> SaveAsync<TValues>(
        SettingsSectionApi<TValues> api,
        SettingsUpdate change,
        Actor actor,
        CancellationToken cancellationToken)
        where TValues : class
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(change);

        SettingsSaveResult result = await store.SaveAsync(api.Definition, change, actor, cancellationToken).ConfigureAwait(false);

        return new(result, await SavedAsync(api, change.Version, result, cancellationToken).ConfigureAwait(false));
    }

    // The section as it is, with a new version, so every pxe host applies it again and scans its interfaces anew.
    public async Task<SettingsSaved<PxeSettings>> RescanAsync(Actor actor, CancellationToken cancellationToken)
    {
        SettingsSectionApi<PxeSettings> api = SettingsApi.Pxe;
        long loaded = settings.Current[api.Name].Version;
        SettingsSaveResult result = await store
            .TouchAsync(api.Definition, actor, "Asked every pxe host to scan its interfaces and apply pxe again.", cancellationToken)
            .ConfigureAwait(false);

        return new(result, await SavedAsync(api, loaded, result, cancellationToken).ConfigureAwait(false));
    }

    // A save that changed the section waits a moment for this host to apply it, then pushes the view it answers with.
    private async Task<SettingsSectionView<TValues>?> SavedAsync<TValues>(
        SettingsSectionApi<TValues> api,
        long loadedVersion,
        SettingsSaveResult result,
        CancellationToken cancellationToken)
        where TValues : class
    {
        if (result.Outcome != SettingsSaveOutcome.Saved)
        {
            return null;
        }

        long version = (result.Snapshot ?? settings.Current)[api.Name].Version;

        if (version == loadedVersion)
        {
            return await views.ViewAsync(api, settings.Current, cancellationToken).ConfigureAwait(false);
        }

        if (api.Definition.Kind == SettingsSectionKind.Restart && hostStates.Local(api.Name) is not null)
        {
            await hostStates.WaitAsync(api.Name, version, s_applyWait, cancellationToken).ConfigureAwait(false);
        }

        SettingsSectionView<TValues> view = await views.ViewAsync(api, settings.Current, cancellationToken).ConfigureAwait(false);
        live.SettingsChanged(view, api.OperatorsMayRead);

        return view;
    }
}
