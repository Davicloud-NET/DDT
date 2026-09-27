// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Authentication;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Security;
using DDT.Server.Users;
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

        group.MapGet("/providers", ListProviders).AllowAnonymous();
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
        UserActivity activity,
        IOptions<OidcOptions> options,
        TimeProvider time,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ExternalLoginInfo? info = await signInManager.GetExternalLoginInfoAsync().ConfigureAwait(false);

        if (info is null)
        {
            return TypedResults.Redirect("/sign-in?error=external");
        }

        OidcOptions oidc = options.Value;
        ILogger logger = loggerFactory.CreateLogger(typeof(ExternalLoginEndpoints));
        GroupRoles mapped = GroupRoles.From(SingleSignOnGroups.Read(info.Principal, oidc.GroupsClaim), oidc.GroupRoleMap);
        DdtUser? existing = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey).ConfigureAwait(false);

        // Identity does not know DDT's disabled flag, so this checks it as the password sign-in does.
        if (existing is { IsDisabled: true })
        {
            return TypedResults.Redirect("/sign-in?error=not-allowed");
        }

        // As with the directory, the groups decide the role of an account single sign-on made, at each sign-in and before
        // the cookie is issued, and an account in none of them loses the role it had. A local account linked to the
        // identity keeps the role an administrator gave it.
        if (mapped.Decides && existing is { Source: AccountSource.External })
        {
            (IdentityResult applied, bool changed) = await activity.ApplyGroupRoleAsync(existing, mapped.Role).ConfigureAwait(false);

            if (changed)
            {
                await activity.ChangedAsync(existing, cancellationToken).ConfigureAwait(false);
            }

            if (!applied.Succeeded)
            {
                AuthLog.GroupRoleNotSaved(logger, info.LoginProvider, existing.UserName, Describe(applied));

                return TypedResults.Redirect("/sign-in?error=external");
            }

            if (mapped.Role is null)
            {
                AuthLog.NoMappedGroup(logger, info.LoginProvider, existing.UserName);

                return TypedResults.Redirect("/sign-in?error=no-role");
            }
        }

        SignInResult result = await signInManager
            .ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false, bypassTwoFactor: false)
            .ConfigureAwait(false);

        if (result.Succeeded)
        {
            if (existing is not null)
            {
                await activity.SignedInAsync(existing, cancellationToken).ConfigureAwait(false);
            }

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

        if (!oidc.AutoProvision)
        {
            // The identity is unknown and DDT will not guess which local account it belongs to.
            // Matching on the asserted email address would let any issuer that does not verify
            // addresses take over an account by claiming one.
            return TypedResults.Redirect("/sign-in?error=unlinked");
        }

        // The groups come before AutoProvisionRole, and no account is made for an identity they give no role.
        if (mapped.Decides && mapped.Role is null)
        {
            AuthLog.NoMappedGroup(logger, info.LoginProvider, info.Principal.Identity?.Name ?? info.ProviderKey);

            return TypedResults.Redirect("/sign-in?error=no-role");
        }

        DdtUser user = new()
        {
            UserName = info.Principal.Identity?.Name ?? info.ProviderKey,
            Email = info.Principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            DisplayName = info.Principal.Identity?.Name,
            Source = AccountSource.External,
            CreatedUtc = time.GetUtcNow(),
        };

        IdentityResult provisioned = await ExternalAccounts
            .ProvisionAsync(userManager, user, info, mapped.Decides ? mapped.Role! : oidc.AutoProvisionRole)
            .ConfigureAwait(false);

        if (!provisioned.Succeeded)
        {
            AuthLog.ProvisionFailed(logger, info.LoginProvider, user.UserName, Describe(provisioned));

            return TypedResults.Redirect("/sign-in?error=provision");
        }

        await signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);
        await activity.SignedInAsync(user, cancellationToken).ConfigureAwait(false);

        return TypedResults.Redirect("/");
    }

    // For the sign-in page, which offers a button for each. The display name is read here, so a change to it shows at
    // once; the provider itself is registered at startup.
    private static Ok<IReadOnlyList<ExternalProvider>> ListProviders(IOptions<OidcOptions> options) =>
        TypedResults.Ok<IReadOnlyList<ExternalProvider>>(
            options.Value.Enabled ? [new ExternalProvider(OidcOptions.SchemeName, options.Value.DisplayName)] : []);

    private static string Describe(IdentityResult result) => string.Join("; ", result.Errors.Select(error => error.Description));

    // Linking happens only from an already authenticated session, so the account being linked to
    // is proven rather than inferred.
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

        await activity.ChangedAsync(user, cancellationToken).ConfigureAwait(false);

        return TypedResults.Ok();
    }
}
