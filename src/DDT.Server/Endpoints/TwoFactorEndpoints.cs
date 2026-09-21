// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using DDT.Contracts.Authentication;
using DDT.Server.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Endpoints;

public static class TwoFactorEndpoints
{
    private const int RecoveryCodeCount = 10;

    public static RouteGroupBuilder MapTwoFactorEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/enroll", StartEnrollmentAsync);
        group.MapPost("/enable", EnableAsync);
        group.MapPost("/disable", DisableAsync);
        group.MapPost("/recovery-codes", RegenerateRecoveryCodesAsync);

        return group;
    }

    private static async Task<Results<Ok<TwoFactorEnrollment>, UnauthorizedHttpResult>> StartEnrollmentAsync(
        ClaimsPrincipal principal,
        UserManager<DdtUser> userManager)
    {
        DdtUser? user = await userManager.GetUserAsync(principal).ConfigureAwait(false);

        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        string? key = await userManager.GetAuthenticatorKeyAsync(user).ConfigureAwait(false);

        if (string.IsNullOrEmpty(key))
        {
            await userManager.ResetAuthenticatorKeyAsync(user).ConfigureAwait(false);
            key = await userManager.GetAuthenticatorKeyAsync(user).ConfigureAwait(false) ?? string.Empty;
        }

        return TypedResults.Ok(new TwoFactorEnrollment(
            AuthEndpoints.FormatSharedKey(key),
            AuthEndpoints.BuildAuthenticatorUri(user.UserName ?? string.Empty, key)));
    }

    private static async Task<Results<Ok<RecoveryCodes>, ValidationProblem, UnauthorizedHttpResult>> EnableAsync(
        TwoFactorVerifyRequest request,
        ClaimsPrincipal principal,
        UserManager<DdtUser> userManager,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(request);

        DdtUser? user = await userManager.GetUserAsync(principal).ConfigureAwait(false);

        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        bool valid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            userManager.Options.Tokens.AuthenticatorTokenProvider,
            request.Code.Replace(" ", string.Empty, StringComparison.Ordinal)).ConfigureAwait(false);

        if (!valid)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["code"] = ["That code is not valid. Check the time on the device generating it."],
            });
        }

        await userManager.SetTwoFactorEnabledAsync(user, enabled: true).ConfigureAwait(false);

        ILogger logger = loggerFactory.CreateLogger(typeof(TwoFactorEndpoints));
        string userName = user.UserName ?? string.Empty;
        AuthLog.TwoFactorEnabled(logger, userName);

        IEnumerable<string> codes =
            await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount).ConfigureAwait(false)
            ?? [];

        return TypedResults.Ok(new RecoveryCodes([.. codes]));
    }

    private static async Task<Results<Ok, ValidationProblem, UnauthorizedHttpResult>> DisableAsync(
        TwoFactorVerifyRequest request,
        ClaimsPrincipal principal,
        UserManager<DdtUser> userManager,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(request);

        DdtUser? user = await userManager.GetUserAsync(principal).ConfigureAwait(false);

        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        // Disabling a second factor is a credential change, so it needs a current code rather
        // than only a live session.
        bool valid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            userManager.Options.Tokens.AuthenticatorTokenProvider,
            request.Code.Replace(" ", string.Empty, StringComparison.Ordinal)).ConfigureAwait(false);

        if (!valid)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["code"] = ["That code is not valid."],
            });
        }

        await userManager.SetTwoFactorEnabledAsync(user, enabled: false).ConfigureAwait(false);
        await userManager.ResetAuthenticatorKeyAsync(user).ConfigureAwait(false);

        ILogger logger = loggerFactory.CreateLogger(typeof(TwoFactorEndpoints));
        string userName = user.UserName ?? string.Empty;
        AuthLog.TwoFactorDisabled(logger, userName);

        return TypedResults.Ok();
    }

    private static async Task<Results<Ok<RecoveryCodes>, UnauthorizedHttpResult>> RegenerateRecoveryCodesAsync(
        ClaimsPrincipal principal,
        UserManager<DdtUser> userManager)
    {
        DdtUser? user = await userManager.GetUserAsync(principal).ConfigureAwait(false);

        if (user is null || !user.TwoFactorEnabled)
        {
            return TypedResults.Unauthorized();
        }

        IEnumerable<string> codes =
            await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount).ConfigureAwait(false)
            ?? [];

        return TypedResults.Ok(new RecoveryCodes([.. codes]));
    }
}
