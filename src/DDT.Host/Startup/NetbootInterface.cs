// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Settings;
using DDT.Server.Data;
using DDT.Server.Settings;

namespace DDT.Host.Startup;

// Setup's netboot page. A setting, not ddt.ini, which would lock the field.
internal static class NetbootInterface
{
    public static async Task<string> SetAsync(IServiceProvider services, string name)
    {
        using IServiceScope scope = services.CreateScope();
        SettingsStore store = scope.ServiceProvider.GetRequiredService<SettingsStore>();
        DdtSettings settings = scope.ServiceProvider.GetRequiredService<DdtSettings>();
        settings.Publish(await store.LoadAsync(CancellationToken.None).ConfigureAwait(false));

        SettingsSectionApi<PxeSettings> api = SettingsApi.Pxe;
        SettingsSectionState state = settings.Current[api.Name];
        PxeSettings current = api.Values(state.Options);

        if (current.Interfaces.Count > 0)
        {
            return $"PXE already serves {string.Join(", ", current.Interfaces)}, so that stays.";
        }

        SettingsSaveResult result = await store.SaveAsync(
            api.Definition,
            new SettingsUpdate { Version = state.Version, Values = api.Document(current with { Interfaces = [name] }) },
            Actor.Console,
            CancellationToken.None).ConfigureAwait(false);

        return result.Outcome == SettingsSaveOutcome.Saved
            ? $"PXE serves {name} now. Running servers apply it within 15 seconds."
            : $"PXE was left as it is: {result.Outcome}.";
    }
}
