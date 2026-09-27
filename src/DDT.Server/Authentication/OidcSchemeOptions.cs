// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace DDT.Server.Authentication;

// The OpenID Connect handler's options from the oidc section. The scheme DDT registers and the candidate a save checks
// both come from here, so the check tests what would run.
public static class OidcSchemeOptions
{
    public const string CallbackPath = "/api/auth/external/callback";

    public static void Configure(OpenIdConnectOptions openId, OidcOptions oidc)
    {
        ArgumentNullException.ThrowIfNull(openId);
        ArgumentNullException.ThrowIfNull(oidc);

        // Without this the external principal is signed straight into the application cookie:
        // no local user, no link row, no roles, no lockout and no second factor.
        openId.SignInScheme = IdentityConstants.ExternalScheme;

        openId.Authority = oidc.Authority;
        openId.ClientId = oidc.ClientId;
        openId.ClientSecret = oidc.ClientSecret;

        // The handler defaults to the implicit flow, which leaves PKCE inert.
        openId.ResponseType = OpenIdConnectResponseType.Code;
        openId.ResponseMode = OpenIdConnectResponseMode.Query;
        openId.UsePkce = true;

        openId.GetClaimsFromUserInfoEndpoint = true;
        openId.SaveTokens = false;
        openId.CallbackPath = CallbackPath;
        openId.TimeProvider ??= TimeProvider.System;

        openId.Scope.Clear();

        foreach (string scope in oidc.Scopes)
        {
            openId.Scope.Add(scope);
        }

        // The groups claim is read at the sign-in, from the settings of that moment.
        openId.Events.OnUserInformationReceived = context =>
        {
            if (context.Principal?.Identity is ClaimsIdentity identity)
            {
                SingleSignOnGroups.CopyFromUserInformation(
                    context.User.RootElement,
                    identity,
                    context.HttpContext.RequestServices.GetRequiredService<DdtSettings>().Current.Oidc.GroupsClaim,
                    context.Options.ClaimsIssuer ?? OidcOptions.SchemeName);
            }

            return Task.CompletedTask;
        };
    }
}
