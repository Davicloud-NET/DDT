// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using DDT.Contracts.Authentication;
using DDT.Server.Data;
using DDT.Server.Ldap;
using DDT.Server.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/session", IssueAntiforgeryToken).AllowAnonymous();
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicies.SignIn);
        group.MapPost("/logout", LogoutAsync);
        group.MapGet("/me", GetCurrentUserAsync);
        group.MapPost("/password", ChangePasswordAsync);

        return group;
    }

    private static Ok IssueAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        RefreshAntiforgeryToken(context, antiforgery);

        return TypedResults.Ok();
    }

    // Antiforgery tokens are bound to the authenticated identity, so a token minted while
    // anonymous stops validating the moment sign in succeeds. Every endpoint that changes the
    // identity hands back a fresh one.
    private static void RefreshAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Headers[CsrfHeaderNames.RequestToken] = tokens.RequestToken;
    }

    private static async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>> LoginAsync(
        LoginRequest request,
        SignInManager<DdtUser> signInManager,
        UserManager<DdtUser> userManager,
        DirectorySignInService directory,
        HttpContext context,
        IAntiforgery antiforgery,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(request);

        ILogger logger = loggerFactory.CreateLogger(typeof(AuthEndpoints));
        string address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        SignInResult result = request switch
        {
            { RecoveryCode.Length: > 0 } =>
                await signInManager.TwoFactorRecoveryCodeSignInAsync(request.RecoveryCode!).ConfigureAwait(false),
            { TwoFactorCode.Length: > 0 } =>
                await signInManager
                    .TwoFactorAuthenticatorSignInAsync(request.TwoFactorCode!, isPersistent: false, rememberClient: false)
                    .ConfigureAwait(false),
            _ => await CredentialSignInAsync(request, signInManager, userManager, directory, context.RequestAborted)
                .ConfigureAwait(false),
        };

        if (result.RequiresTwoFactor)
        {
            return TypedResults.Ok(new LoginResponse(LoginStatus.RequiresTwoFactor));
        }

        if (result.IsLockedOut)
        {
            AuthLog.LockedOut(logger, request.UserName);

            return TypedResults.Ok(new LoginResponse(LoginStatus.LockedOut));
        }

        if (!result.Succeeded)
        {
            AuthLog.SignInFailed(logger, request.UserName, address);

            return TypedResults.Unauthorized();
        }

        AuthLog.SignedIn(logger, request.UserName);
        RefreshAntiforgeryToken(context, antiforgery);

        return TypedResults.Ok(new LoginResponse(LoginStatus.Succeeded));
    }

    private static async Task<SignInResult> CredentialSignInAsync(
        LoginRequest request,
        SignInManager<DdtUser> signInManager,
        UserManager<DdtUser> userManager,
        DirectorySignInService directory,
        CancellationToken cancellationToken)
    {
        DdtUser? user = await userManager.FindByNameAsync(request.UserName).ConfigureAwait(false);

        if (user?.Source == AccountSource.Directory || (user is null && directory.Enabled))
        {
            return await directory.SignInAsync(request.UserName, request.Password, cancellationToken)
                .ConfigureAwait(false);
        }

        // An unknown user and a wrong password must be indistinguishable in both body and timing,
        // so the password is hashed anyway rather than returning early.
        if (user is null)
        {
            _ = userManager.PasswordHasher.HashPassword(new DdtUser { UserName = request.UserName }, request.Password);

            return SignInResult.Failed;
        }

        if (user.IsDisabled)
        {
            return SignInResult.NotAllowed;
        }

        return await signInManager
            .PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true)
            .ConfigureAwait(false);
    }

    private static async Task<Ok> LogoutAsync(
        SignInManager<DdtUser> signInManager,
        HttpContext context,
        IAntiforgery antiforgery)
    {
        await signInManager.SignOutAsync().ConfigureAwait(false);

        // SignOutAsync clears the response cookie but leaves HttpContext.User set for the rest of
        // this request. Minting the replacement token before resetting it would bind the token to
        // the identity being signed out, and the next request would fail validation.
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        RefreshAntiforgeryToken(context, antiforgery);

        return TypedResults.Ok();
    }

    private static async Task<Results<Ok<CurrentUser>, UnauthorizedHttpResult>> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        UserManager<DdtUser> userManager)
    {
        DdtUser? user = await userManager.GetUserAsync(principal).ConfigureAwait(false);

        if (user is null || user.IsDisabled)
        {
            return TypedResults.Unauthorized();
        }

        IList<string> roles = await userManager.GetRolesAsync(user).ConfigureAwait(false);

        return TypedResults.Ok(new CurrentUser(
            user.Id,
            user.UserName ?? string.Empty,
            user.DisplayName,
            user.Source.ToString(),
            user.TwoFactorEnabled,
            [.. roles]));
    }

    private static async Task<Results<Ok, ValidationProblem, UnauthorizedHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<DdtUser> userManager,
        SignInManager<DdtUser> signInManager)
    {
        ArgumentNullException.ThrowIfNull(request);

        DdtUser? user = await userManager.GetUserAsync(principal).ConfigureAwait(false);

        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (user.Source == AccountSource.Directory)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["source"] = ["This account is managed by the directory. Change the password there."],
            });
        }

        IdentityResult result = await userManager
            .ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(result.ToProblemDictionary());
        }

        await signInManager.RefreshSignInAsync(user).ConfigureAwait(false);

        return TypedResults.Ok();
    }

    internal static Dictionary<string, string[]> ToProblemDictionary(this IdentityResult result) =>
        result.Errors
            .GroupBy(error => error.Code, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray(),
                StringComparer.Ordinal);

    internal static string BuildAuthenticatorUri(string userName, string sharedKey) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
            UrlEncoder.Default.Encode("DDT"),
            UrlEncoder.Default.Encode(userName),
            sharedKey);

    internal static string FormatSharedKey(string unformattedKey)
    {
        StringBuilder result = new();

        for (int position = 0; position < unformattedKey.Length; position += 4)
        {
            int length = Math.Min(4, unformattedKey.Length - position);
            result.Append(unformattedKey.AsSpan(position, length)).Append(' ');
        }

        return result.ToString().Trim();
    }
}
