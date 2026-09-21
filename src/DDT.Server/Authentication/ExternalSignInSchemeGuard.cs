// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Authentication;

// Guards the single most damaging misconfiguration in the external sign in path. If SignInScheme
// resolves to the application cookie rather than the external one, every identity the configured
// provider will authenticate gets a DDT session with no local user, no roles, no lockout and no
// second factor. It is silent when it happens, so it is checked at startup instead.
public sealed partial class ExternalSignInSchemeGuard(
    IOptionsMonitor<OpenIdConnectOptions> options,
    IOptions<OidcOptions> oidc,
    ILogger<ExternalSignInSchemeGuard> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        string? signInScheme = options.Get(OidcOptions.SchemeName).SignInScheme;

        if (!string.Equals(signInScheme, IdentityConstants.ExternalScheme, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The OpenID Connect handler signs into '{signInScheme}' rather than " +
                $"'{IdentityConstants.ExternalScheme}'. That bypasses local account linking entirely.");
        }

        // Allowed for now, and said out loud, because nothing else would.
        if (oidc.Value.AutoProvision
            && string.Equals(oidc.Value.AutoProvisionRole, DdtRoleNames.Administrator, StringComparison.OrdinalIgnoreCase))
        {
            LogAdministratorsProvisioned();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 880,
        Level = LogLevel.Warning,
        Message = "DDT:Oidc:AutoProvisionRole is Administrator: every identity the provider signs in that DDT has not seen becomes an administrator")]
    private partial void LogAdministratorsProvisioned();
}
