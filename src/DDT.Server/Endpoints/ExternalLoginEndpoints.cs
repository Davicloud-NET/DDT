// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Endpoints;

public static class ExternalLoginEndpoints
{
    private const string CompletePath = "/api/auth/external/complete";

    public static RouteGroupBuilder MapExternalLoginEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/start", Start).AllowAnonymous();
        group.MapGet("/complete", CompleteAsync).AllowAnonymous();
        group.MapPost("/link", LinkAsync).RequireSession();

        return group;
    }

    private static ChallengeHttpResult Start(SignInManager<DdtUser> signInManager)
    {
        AuthenticationProperties properties =
            signInManager.ConfigureExternalAuthenticationProperties(OidcOptions.SchemeName, CompletePath);

        return TypedResults.Challenge(properties, [OidcOptions.SchemeName]);
    }

    private static async Task<RedirectHttpResult> CompleteAsync(
        SignInManager<DdtUser> signInManager,
        UserManager<DdtUser> userManager,
        IOptions<OidcOptions> options,
        TimeProvider time,
        ILoggerFactory loggerFactory)
    {
        ExternalLoginInfo? info = await signInManager.GetExternalLoginInfoAsync().ConfigureAwait(false);

        if (info is null)
        {
            return TypedResults.Redirect("/sign-in?error=external");
        }

        // Identity does not know DDT's disabled flag, so this checks it as the password sign-in does.
        if (await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).ConfigureAwait(false) is { IsDisabled: true })
        {
            return TypedResults.Redirect("/sign-in?error=not-allowed");
        }

        SignInResult result = await signInManager
            .ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            return TypedResults.Redirect("/");
        }

        // As after a correct password, Identity keeps the account in its two-factor cookie, and the sign-in page's
        // code step finishes the sign-in through the login endpoint.
        if (result.RequiresTwoFactor)
        {
            return TypedResults.Redirect("/sign-in?step=two-factor");
        }

        if (result.IsLockedOut)
        {
            return TypedResults.Redirect("/sign-in?error=locked");
        }

        if (result.IsNotAllowed)
        {
            return TypedResults.Redirect("/sign-in?error=not-allowed");
        }

        if (!options.Value.AutoProvision)
        {
            // The identity is unknown and DDT will not guess which local account it belongs to.
            // Matching on the asserted email address would let any issuer that does not verify
            // addresses take over an account by claiming one.
            return TypedResults.Redirect("/sign-in?error=unlinked");
        }

        DdtUser user = new()
        {
            UserName = info.Principal.Identity?.Name ?? info.ProviderKey,
            Email = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            DisplayName = info.Principal.Identity?.Name,
            Source = AccountSource.Directory,
            CreatedUtc = time.GetUtcNow(),
        };

        IdentityResult provisioned = await ExternalAccounts
            .ProvisionAsync(userManager, user, info, options.Value.AutoProvisionRole)
            .ConfigureAwait(false);

        if (!provisioned.Succeeded)
        {
            AuthLog.ProvisionFailed(
                loggerFactory.CreateLogger(typeof(ExternalLoginEndpoints)),
                info.LoginProvider,
                user.UserName,
                string.Join("; ", provisioned.Errors.Select(error => error.Description)));

            return TypedResults.Redirect("/sign-in?error=provision");
        }

        await signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);

        return TypedResults.Redirect("/");
    }

    // Linking happens only from an already authenticated session, so the account being linked to
    // is proven rather than inferred.
    private static async Task<Results<Ok, ValidationProblem, UnauthorizedHttpResult>> LinkAsync(
        System.Security.Claims.ClaimsPrincipal principal,
        SignInManager<DdtUser> signInManager,
        UserManager<DdtUser> userManager)
    {
        DdtUser? user = await userManager.GetUserAsync(principal).ConfigureAwait(false);

        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        ExternalLoginInfo? info = await signInManager.GetExternalLoginInfoAsync(user.Id.ToString()).ConfigureAwait(false);

        if (info is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["external"] = ["No external sign in is in progress."],
            });
        }

        IdentityResult result = await userManager.AddLoginAsync(user, info).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(result.ToProblemDictionary());
        }

        return TypedResults.Ok();
    }
}
