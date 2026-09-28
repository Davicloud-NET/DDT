// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Authentication;
using DDT.Contracts.Messages;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Security;
using DDT.Server.Settings;
using DDT.Server.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace DDT.Server.Endpoints;

public static class ExternalLoginEndpoints
{
    private const string CompletePath = "/api/auth/external/complete";

    public static RouteGroupBuilder MapExternalLoginEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/providers", ListProvidersAsync).AllowAnonymous();
        group.MapGet("/start", StartAsync).AllowAnonymous();
        group.MapGet("/complete", CompleteAsync).AllowAnonymous();
        group.MapPost("/link", LinkAsync).RequireSession();

        return group;
    }

    // Not found while single sign-on is off or its values do not start the handler: the scheme is then not registered,
    // and a challenge to it would fail on the server.
    private static async Task<Results<ChallengeHttpResult, NotFound>> StartAsync(SignInManager<DdtUser> signInManager, IAuthenticationSchemeProvider schemes)
    {
        if (await schemes.GetSchemeAsync(OidcOptions.SchemeName).ConfigureAwait(false) is null)
        {
            return TypedResults.NotFound();
        }

        AuthenticationProperties properties =
            signInManager.ConfigureExternalAuthenticationProperties(OidcOptions.SchemeName, CompletePath);

        return TypedResults.Challenge(properties, [OidcOptions.SchemeName]);
    }

    private static async Task<RedirectHttpResult> CompleteAsync(ExternalSignIn signIn, CancellationToken cancellationToken) =>
        TypedResults.Redirect(await signIn.CompleteAsync(cancellationToken).ConfigureAwait(false) switch
        {
            ExternalSignInOutcome.SignedIn => "/",
            ExternalSignInOutcome.TwoFactor => "/sign-in?step=two-factor",
            ExternalSignInOutcome.NotAllowed => "/sign-in?error=not-allowed",
            ExternalSignInOutcome.NoRole => "/sign-in?error=no-role",
            ExternalSignInOutcome.LockedOut => "/sign-in?error=locked",
            ExternalSignInOutcome.Unlinked => "/sign-in?error=unlinked",
            ExternalSignInOutcome.NotProvisioned => "/sign-in?error=provision",
            _ => "/sign-in?error=external",
        });

    // For the sign-in page, which offers a button for each. Only a provider whose scheme this server registered, so a
    // button never leads to a sign-in that cannot start.
    private static async Task<Ok<IReadOnlyList<ExternalProvider>>> ListProvidersAsync(DdtSettings settings, IAuthenticationSchemeProvider schemes)
    {
        OidcOptions oidc = settings.Current.Oidc;

        return TypedResults.Ok<IReadOnlyList<ExternalProvider>>(
            oidc.Enabled && await schemes.GetSchemeAsync(OidcOptions.SchemeName).ConfigureAwait(false) is not null
                ? [new ExternalProvider(OidcOptions.SchemeName, oidc.DisplayName)]
                : []);
    }

    // Linking happens only from a signed-in session, so the account linked to is proven rather than inferred.
    private static async Task<Results<Ok, ValidationProblem, UnauthorizedHttpResult>> LinkAsync(
        System.Security.Claims.ClaimsPrincipal principal,
        SignInManager<DdtUser> signInManager,
        UserManager<DdtUser> userManager,
        UserActivity activity,
        CancellationToken cancellationToken)
    {
        DdtUser? user = await userManager.GetUserAsync(principal).ConfigureAwait(false);

        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        ExternalLoginInfo? info = await signInManager.GetExternalLoginInfoAsync(user.Id.ToString()).ConfigureAwait(false);

        if (info is null)
        {
            return ServerProblems.Validation("external", ServerMessages.AccountNoExternalSignIn.With());
        }

        IdentityResult result = await userManager.AddLoginAsync(user, info).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return result.ToValidationProblem();
        }

        await activity.ChangedAsync(user, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok();
    }
}
