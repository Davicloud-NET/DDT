// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Claims;
using System.Text.Encodings.Web;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Authentication;

// Everything is checked on every request, as the session cookie's security stamp is checked every minute: a token stops
// working the moment it is revoked or expires, or its user is disabled or locked out. Its role is the lower of its own
// and the user's highest now, so demoting a user demotes their tokens.
public sealed class ApiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    DdtDbContext database,
    LiveNotifier live,
    TimeProvider timeProvider) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;

        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        string secret = header[BearerPrefix.Length..].Trim();

        if (!ApiTokenSecrets.LooksLikeOne(secret))
        {
            return AuthenticateResult.NoResult();
        }

        string hash = ApiTokenSecrets.Hash(secret);
        var found = await database.ApiTokens
            .AsNoTracking()
            .Where(t => t.SecretHash == hash)
            .Join(database.Users, token => token.UserId, user => user.Id, (token, user) => new { Token = token, User = user })
            .FirstOrDefaultAsync(Context.RequestAborted)
            .ConfigureAwait(false);

        if (found is null)
        {
            return AuthenticateResult.Fail("The API token is unknown.");
        }

        (ApiToken token, DdtUser user) = (found.Token, found.User);
        DateTimeOffset now = timeProvider.GetUtcNow();

        if (token.RevokedUtc is not null)
        {
            return AuthenticateResult.Fail($"The API token {token.Name} was revoked.");
        }

        if (token.ExpiresUtc <= now)
        {
            return AuthenticateResult.Fail($"The API token {token.Name} expired.");
        }

        if (user.IsDisabled || (user.LockoutEnabled && user.LockoutEnd > now))
        {
            return AuthenticateResult.Fail($"The account of the API token {token.Name} is disabled or locked out.");
        }

        List<string?> roles = await database.UserRoles
            .Where(membership => membership.UserId == user.Id)
            .Join(database.Roles, membership => membership.RoleId, role => role.Id, (_, role) => role.Name)
            .ToListAsync(Context.RequestAborted)
            .ConfigureAwait(false);

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new(DdtClaimTypes.ApiTokenId, token.Id.ToString("D")),
            new(DdtClaimTypes.ApiTokenName, token.Name),
        ];

        if (ApiTokenRoles.Lower(token.Role, ApiTokenRoles.Highest(roles)) is { } role)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        await RecordUseAsync(token, user, now).ConfigureAwait(false);

        return AuthenticateResult.Success(new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, DdtAuthenticationSchemes.ApiToken)),
            DdtAuthenticationSchemes.ApiToken));
    }

    // A script may call many times a second. Written directly rather than through a save, which would reach the audit
    // interceptor for nothing, and only once a minute.
    private async Task RecordUseAsync(ApiToken token, DdtUser user, DateTimeOffset now)
    {
        if (token.LastUsedUtc is { } last && now - last < ApiTokenLimits.LastUsedInterval)
        {
            return;
        }

        string? address = Context.Connection.RemoteIpAddress?.ToString();

        await database.ApiTokens
            .Where(t => t.Id == token.Id)
            .ExecuteUpdateAsync(
                update => update.SetProperty(t => t.LastUsedUtc, now).SetProperty(t => t.LastUsedAddress, address),
                Context.RequestAborted)
            .ConfigureAwait(false);

        token.LastUsedUtc = now;
        token.LastUsedAddress = address;
        live.TokenChanged(ApiTokenViews.From(token, user.UserName ?? string.Empty));
    }
}
