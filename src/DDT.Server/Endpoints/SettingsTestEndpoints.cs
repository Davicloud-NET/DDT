// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Messages;
using DDT.Contracts.Settings;
using DDT.Server.Authentication;
using DDT.Server.Security;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

// Tries a section's values before they are saved.
public static class SettingsTestEndpoints
{
    public static RouteGroupBuilder MapSettingsTestEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // Sends a password to the directory, so it is limited like a sign-in.
        group.MapPost("/ldap/test", TestLdapAsync).RequireAuthorization(DdtPolicies.Administrator).RequireRateLimiting(RateLimitPolicies.SignIn);
        group.MapPost("/oidc/test", TestOidcAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<IResult> TestLdapAsync(
        LdapTestRequest request,
        ClaimsPrincipal principal,
        LdapSettingsTest test,
        CancellationToken cancellationToken)
    {
        if (request?.Values is null)
        {
            return ServerProblems.Validation("values", ServerMessages.SettingsLdapTestSendValues.With());
        }

        return await test.TestAsync(request, principal, cancellationToken).ConfigureAwait(false) is { } result
            ? TypedResults.Ok(result)
            : ServerProblems.Validation(LdapSettingsTest.BindPasswordField, ServerMessages.SettingsSecretForNewServer.With());
    }

    private static async Task<IResult> TestOidcAsync(
        OidcTestRequest request,
        HttpContext context,
        OidcSettingsTest test,
        CancellationToken cancellationToken)
    {
        string redirectUri = $"{context.Request.Scheme}://{context.Request.Host}{OidcSchemeOptions.CallbackPath}";

        if (!Uri.TryCreate(request?.Authority?.Trim(), UriKind.Absolute, out Uri? authority) || authority.Scheme != Uri.UriSchemeHttps)
        {
            return ServerProblems.Validation("authority", ServerMessages.SettingsOidcTestAuthorityInvalid.With());
        }

        return TypedResults.Ok(await test.TestAsync(authority, redirectUri, cancellationToken).ConfigureAwait(false));
    }
}
