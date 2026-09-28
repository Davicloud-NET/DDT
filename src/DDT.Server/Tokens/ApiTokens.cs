// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Security.Claims;
using DDT.Contracts.Messages;
using DDT.Contracts.Tokens;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Tokens;

// API tokens as their users make and revoke them. A token has no more rights than its user.
internal sealed class ApiTokens(DdtDbContext database, LiveNotifier live, TimeProvider timeProvider)
{
    // A user has a few tokens and a server a few dozen, so they are ordered here: SQLite cannot order by DateTimeOffset.
    public async Task<IReadOnlyList<ApiTokenView>> ViewsAsync(Guid? userId, CancellationToken cancellationToken)
    {
        IQueryable<ApiToken> tokens = userId is { } owner ? database.ApiTokens.Where(t => t.UserId == owner) : database.ApiTokens;
        var listed = await tokens
            .AsNoTracking()
            .Join(database.Users, token => token.UserId, user => user.Id, (token, user) => new { Token = token, user.UserName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. listed
                .OrderByDescending(t => t.Token.CreatedUtc)
                .ThenByDescending(t => t.Token.Id)
                .Select(t => ApiTokenViews.From(t.Token, t.UserName ?? string.Empty)),
        ];
    }

    // Problems is set instead of the token when the request has any.
    public async Task<(CreatedApiToken? Created, FieldProblems? Problems)> CreateAsync(
        Guid userId,
        string? userName,
        CreateApiTokenRequest request,
        Actor actor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        TokenRequest asked = new(request.Name?.Trim() ?? string.Empty, ApiTokenRoles.Known(request.Role), request.ExpiresInDays ?? ApiTokenLimits.DefaultDays);
        DateTimeOffset now = timeProvider.GetUtcNow();

        FieldProblems problems = await ProblemsAsync(userId, asked, now, cancellationToken).ConfigureAwait(false);

        if (problems.Count > 0 || asked.Role is not { } role)
        {
            return (null, problems);
        }

        string secret = ApiTokenSecrets.Create();
        ApiToken token = new()
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Name = asked.Name,
            Role = role,
            SecretHash = ApiTokenSecrets.Hash(secret),
            Hint = ApiTokenSecrets.Hint(secret),
            CreatedUtc = now,
            ExpiresUtc = now.AddDays(asked.Days),
        };

        database.ApiTokens.Add(token);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.TokenCreated,
            token.Id.ToString("D"),
            actor,
            now,
            $"{token.Name}, {token.Role}, ending in {token.Hint}, expires {token.ExpiresUtc.ToString("u", CultureInfo.InvariantCulture)}."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ApiTokenView view = ApiTokenViews.From(token, userName ?? string.Empty);
        live.TokenChanged(view);

        return (new CreatedApiToken(view, secret), null);
    }

    // Revoking keeps the row, so the list still shows what the token was and who ended it. Revoking again changes nothing.
    public async Task<ApiTokenRevocation> RevokeAsync(Guid id, Guid userId, ClaimsPrincipal user, Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        ApiToken? token = await database.ApiTokens.FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);

        if (token is null)
        {
            return ApiTokenRevocation.NotFound;
        }

        if (token.UserId != userId && !user.IsInRole(DdtRoleNames.Administrator))
        {
            return ApiTokenRevocation.NotOwner;
        }

        if (token.RevokedUtc is not null)
        {
            return ApiTokenRevocation.Revoked;
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        string? owner = await database.Users
            .Where(u => u.Id == token.UserId)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        token.RevokedUtc = now;
        token.RevokedByUserId = userId;
        token.RevokedByName = StoredText.Bound(Principals.ActorName(user), 256);
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.TokenRevoked,
            token.Id.ToString("D"),
            actor,
            now,
            token.UserId == userId ? $"{token.Name}, ending in {token.Hint}." : $"{token.Name} of {owner}, ending in {token.Hint}."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        live.TokenChanged(ApiTokenViews.From(token, owner ?? string.Empty));

        return ApiTokenRevocation.Revoked;
    }

    // The role may not be higher than the highest the user holds in the database; the cookie's roles are those of the
    // sign-in.
    private async Task<FieldProblems> ProblemsAsync(Guid userId, TokenRequest asked, DateTimeOffset now, CancellationToken cancellationToken)
    {
        (string name, string? role, int days) = asked;
        FieldProblems problems = new();
        List<string?> userRoles = await database.UserRoles
            .Where(membership => membership.UserId == userId)
            .Join(database.Roles, membership => membership.RoleId, known => known.Id, (_, known) => known.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        string? highest = ApiTokenRoles.Highest(userRoles);

        if (name.Length is 0 or > ApiTokenLimits.MaxNameLength || name.Any(char.IsControl))
        {
            problems.Add("name", ServerMessages.NameLength.With("max", ApiTokenLimits.MaxNameLength));
        }
        else
        {
            List<ApiToken> active = await database.ApiTokens
                .AsNoTracking()
                .Where(t => t.UserId == userId && t.RevokedUtc == null && t.Name == name)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (active.Any(t => t.ExpiresUtc > now))
            {
                problems.Add("name", ServerMessages.TokenNameTaken.With());
            }
        }

        if (role is null)
        {
            problems.Add("role", ServerMessages.UserRole.With());
        }
        else if (ApiTokenRoles.Lower(role, highest) != role)
        {
            problems.Add("role", highest is null ? ServerMessages.TokenNoRole.With() : ServerMessages.TokenRoleTooHigh.With("role", WithArticle(highest)));
        }

        if (days is < 1 or > ApiTokenLimits.MaxDays)
        {
            problems.Add("expiresInDays", ServerMessages.TokenLifetime.With("max", ApiTokenLimits.MaxDays));
        }

        return problems;
    }

    // The role within the sentence, with the article English gives it.
    private static object WithArticle(string role) => role switch
    {
        DdtRoleNames.Administrator => ServerMessages.RoleAnAdministrator.With(),
        DdtRoleNames.Operator => ServerMessages.RoleAnOperator.With(),
        DdtRoleNames.Viewer => ServerMessages.RoleAViewer.With(),
        _ => $"a {role}",
    };

    // Role is null for one DDT does not know.
    private sealed record TokenRequest(string Name, string? Role, int Days);
}
