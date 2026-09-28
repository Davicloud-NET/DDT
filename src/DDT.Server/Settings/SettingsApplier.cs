// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Server.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace DDT.Server.Settings;

// Applies the oidc and proxies sections in the running server when their version changes, and records each result for
// this host. Only oidc needs a rebuild: the proxies middleware reads each request's snapshot, and PxeHost rebuilds the
// pxe listeners itself.
public sealed partial class SettingsApplier(
    DdtSettings settings,
    IAuthenticationSchemeProvider schemes,
    IOptionsMonitor<OpenIdConnectOptions> openIdOptions,
    IOptionsMonitorCache<OpenIdConnectOptions> openIdCache,
    SettingsHostStates hostStates,
    ILogger<SettingsApplier> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _oidcVersion = -1;
    private long _proxiesVersion = -1;
    private IDisposable? _subscription;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await ApplyAsync(cancellationToken).ConfigureAwait(false);
        _subscription = ChangeToken.OnChange(settings.GetChangeToken, () => _ = ApplyInBackgroundAsync());
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            SettingsSnapshot snapshot = settings.Current;
            SettingsSectionState oidc = snapshot[SettingsSectionNames.Oidc];
            SettingsSectionState proxies = snapshot[SettingsSectionNames.Proxies];

            if (oidc.Version != _oidcVersion)
            {
                await ApplyOidcAsync(snapshot).ConfigureAwait(false);
                _oidcVersion = oidc.Version;
            }

            if (proxies.Version != _proxiesVersion)
            {
                Record(
                    proxies,
                    proxies.Closed ? SettingsApplyResult.Failed : SettingsApplyResult.Applied,
                    proxies.Closed ? Closed(proxies, ServerMessages.SettingsApplyProxiesClosed) : null);
                _proxiesVersion = proxies.Version;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _gate.Dispose();
    }

    private async Task ApplyInBackgroundAsync()
    {
        try
        {
            await ApplyAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogApplyFailed(exception);
        }
    }

    // While single sign-on is off the scheme is not registered: the authentication middleware builds the options of
    // every remote scheme on every request, and empty ones fail validation. A scheme another registration added under
    // the name, such as a test's stand-in, is left alone.
    private async Task ApplyOidcAsync(SettingsSnapshot snapshot)
    {
        SettingsSectionState state = snapshot[SettingsSectionNames.Oidc];
        AuthenticationScheme? existing = await schemes.GetSchemeAsync(OidcOptions.SchemeName).ConfigureAwait(false);

        if (existing is not null && existing.HandlerType != typeof(OpenIdConnectHandler))
        {
            return;
        }

        if (existing is not null)
        {
            schemes.RemoveScheme(OidcOptions.SchemeName);
        }

        // The change token drops the cached options too, but the order of the two callbacks is not the framework's
        // promise, so this does not rely on it.
        openIdCache.TryRemove(OidcOptions.SchemeName);

        if (!snapshot.Oidc.Enabled)
        {
            Record(
                state,
                state.Closed ? SettingsApplyResult.Failed : SettingsApplyResult.Applied,
                state.Closed ? Closed(state, ServerMessages.SettingsApplyOidcClosed) : null);

            return;
        }

        try
        {
            OpenIdConnectOptions options = openIdOptions.Get(OidcOptions.SchemeName);

            if (!string.Equals(options.SignInScheme, IdentityConstants.ExternalScheme, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The handler would sign into '{options.SignInScheme}', which bypasses local account linking.");
            }

            schemes.TryAddScheme(new AuthenticationScheme(OidcOptions.SchemeName, snapshot.Oidc.DisplayName, typeof(OpenIdConnectHandler)));
            hostStates.Record(new SettingsApplyReport(SettingsSectionNames.Oidc, state.Version, SettingsApplyResult.Applied, null));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or OptionsValidationException)
        {
            // Local sign-in keeps working without the scheme.
            LogOidcFailed(exception);
            Record(state, SettingsApplyResult.Failed, ServerMessages.SettingsApplyOidcFailed.With("error", exception.Message));
        }
    }

    private void Record(SettingsSectionState state, SettingsApplyResult result, ServerMessage? message) =>
        hostStates.Record(new SettingsApplyReport(state.Name, state.Version, result, message?.Text) { Text = message });

    // What is off, and then the section's problems, one after another.
    private static ServerMessage Closed(SettingsSectionState state, MessageTemplate consequence) =>
        consequence.With("problems", ServerMessages.Sentences([.. state.Problems.Select(problem => problem.Text)]));

    [LoggerMessage(EventId = 970, Level = LogLevel.Error, Message = "Could not apply the settings")]
    private partial void LogApplyFailed(Exception exception);

    [LoggerMessage(EventId = 971, Level = LogLevel.Error, Message = "Single sign-on is off: its settings do not start the handler")]
    private partial void LogOidcFailed(Exception exception);
}
