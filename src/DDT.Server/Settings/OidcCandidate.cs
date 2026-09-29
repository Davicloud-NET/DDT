// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Core.Configuration;
using DDT.Server.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Settings;

// Builds the scheme's options for values that aren't saved yet, apart from the live ones. It runs the same steps as
// the framework. Those are DDT's configure code, the handler's post-configure step and Validate, and the sign-in scheme
// check of ExternalSignInSchemeGuard.
internal static class OidcCandidate
{
    public static IReadOnlyList<SettingProblem> FindProblems(OidcOptions oidc, IDataProtectionProvider dataProtection)
    {
        OpenIdConnectOptions openId = new();

        try
        {
            OidcSchemeOptions.Configure(openId, oidc);
            new OpenIdConnectPostConfigureOptions(dataProtection).PostConfigure(OidcOptions.SchemeName, openId);
            openId.Validate(OidcOptions.SchemeName);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return [new("Authority", ServerMessages.SettingsOidcCannotStart.With("error", exception.Message))];
        }
        finally
        {
            openId.Backchannel?.Dispose();
        }

        return string.Equals(openId.SignInScheme, IdentityConstants.ExternalScheme, StringComparison.Ordinal)
            ? []
            : [new(string.Empty, ServerMessages.SettingsOidcSignInScheme.With("scheme", openId.SignInScheme ?? string.Empty))];
    }
}
