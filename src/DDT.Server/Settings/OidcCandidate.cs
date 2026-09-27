// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Core.Configuration;
using DDT.Server.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Settings;

// The scheme's options for values not yet saved, built off to the side as the framework builds them: DDT's configure
// code, the handler's post-configure step, its Validate, and the sign-in scheme check that ExternalSignInSchemeGuard
// makes of the published options at startup.
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
            return [new("Authority", $"Single sign-on cannot start with these values: {exception.Message}")];
        }
        finally
        {
            openId.Backchannel?.Dispose();
        }

        return string.Equals(openId.SignInScheme, IdentityConstants.ExternalScheme, StringComparison.Ordinal)
            ? []
            : [new(string.Empty, $"The handler would sign into '{openId.SignInScheme}', which bypasses local account linking entirely.")];
    }
}
