// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DDT.Server.Authentication;

// If SignInScheme pointed at the application cookie instead of the external one, every identity the provider
// authenticates would silently get a session with no local user, roles, lockout or second factor. So this checks the
// registered scheme at startup. A settings save checks its own candidate.
public sealed class ExternalSignInSchemeGuard(
    IOptionsMonitor<OpenIdConnectOptions> options,
    IAuthenticationSchemeProvider schemes,
    DdtSettings settings) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // If the settings can't start the handler, the scheme stays unregistered, and the host reports that as failed.
        if (!settings.Current.Oidc.Enabled
            || await schemes.GetSchemeAsync(OidcOptions.SchemeName).ConfigureAwait(false) is not { } scheme
            || scheme.HandlerType != typeof(OpenIdConnectHandler))
        {
            return;
        }

        string? signInScheme = options.Get(OidcOptions.SchemeName).SignInScheme;

        if (!string.Equals(signInScheme, IdentityConstants.ExternalScheme, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The OpenID Connect handler signs into '{signInScheme}' rather than " +
                $"'{IdentityConstants.ExternalScheme}'. That bypasses local account linking entirely.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
