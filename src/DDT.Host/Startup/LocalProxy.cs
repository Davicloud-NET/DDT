// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Settings;
using DDT.Server.Data;
using DDT.Server.Settings;

namespace DDT.Host.Startup;

// Setup's IIS page: IIS forwards from loopback, so DDT takes the client's address from it. A setting, like the in the card
internal static class LocalProxy
{
    private static readonly string[] s_loopback = ["127.0.0.1", "::1"];

    public static async Task<string> TrustAsync(IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        SettingsStore store = scope.ServiceProvider.GetRequiredService<SettingsStore>();
        DdtSettings settings = scope.ServiceProvider.GetRequiredService<DdtSettings>();
        settings.Publish(await store.LoadAsync(CancellationToken.None).ConfigureAwait(false));

        SettingsSectionApi<ProxySettings> api = SettingsApi.Proxies;
        SettingsSectionState state = settings.Current[api.Name];
        ProxySettings current = api.Values(state.Options);
        string[] missing = [.. s_loopback.Where(address => !current.KnownProxies.Contains(address, StringComparer.OrdinalIgnoreCase))];

        if (missing.Length == 0)
        {
            return "DDT trusts a proxy on this server already.";
        }

        // Reauthenticated.. the console holds the database and the key ring, more than any account
        SettingsSaveResult result = await store.SaveAsync(
            api.Definition,
            new SettingsUpdate
            {
                Version = state.Version,
                Values = api.Document(current with { KnownProxies = [.. current.KnownProxies, .. missing] }),
                Reauthenticated = true,
            },
            Actor.Console,
            CancellationToken.None).ConfigureAwait(false);

        return result.Outcome == SettingsSaveOutcome.Saved
            ? "DDT trusts a proxy on this server now, such as IIS. Running servers apply it within 15 seconds."
            : $"The proxies were left as they are: {result.Outcome}.";
    }
}
