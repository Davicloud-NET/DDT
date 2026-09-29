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

// Checks the token on every request, so it stops working at once when it's revoked or expires, or when its user is
// disabled or locked out. The token gets the lower of its own role and its user's current highest role.
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

        if (await RefusalAsync(token, user, now).ConfigureAwait(false) is { } refusal)
        {
            return AuthenticateResult.Fail(refusal);
        }

        AuthenticationTicket ticket = await TicketAsync(token, user).ConfigureAwait(false);
        await RecordUseAsync(token, user, now).ConfigureAwait(false);

        return AuthenticateResult.Success(ticket);
    }

    private async Task<string?> RefusalAsync(ApiToken token, DdtUser user, DateTimeOffset now)
    {
        if (token.RevokedUtc is not null)
        {
            return $"The API token {token.Name} was revoked.";
        }

        if (token.ExpiresUtc <= now)
        {
            return $"The API token {token.Name} expired.";
        }

        if (user.IsDisabled || (user.LockoutEnabled && user.LockoutEnd > now))
        {
            return $"The account of the API token {token.Name} is disabled or locked out.";
        }

        // An account that still has to replace a password an administrator has seen can only reach its Account page
        // with its session. So its tokens can't reach anything either.
        bool mustChangePassword = await database.UserClaims
            .AnyAsync(claim => claim.UserId == user.Id && claim.ClaimType == DdtClaimTypes.MustChangePassword, Context.RequestAborted)
            .ConfigureAwait(false);

        return mustChangePassword ? $"The account of the API token {token.Name} has to change its password first." : null;
    }

    private async Task<AuthenticationTicket> TicketAsync(ApiToken token, DdtUser user)
    {
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

        return new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, DdtAuthenticationSchemes.ApiToken)),
            DdtAuthenticationSchemes.ApiToken);
    }

    // A script may call many times a second, so the last use is only recorded once a minute. It's written directly
    // instead of through SaveChanges, which would run the audit interceptor for nothing.
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
