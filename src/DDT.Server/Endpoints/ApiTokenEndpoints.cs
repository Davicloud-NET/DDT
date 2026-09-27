// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Security.Claims;
using DDT.Contracts.Messages;
using DDT.Contracts.Tokens;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Security;
using DDT.Server.Tokens;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

// Every signed-in user may make tokens for themselves, with no more rights than they have. The fallback policy lets any
// signed-in user in; what a token then does is up to the policies of the endpoints it calls.
public static class ApiTokenEndpoints
{
    public static RouteGroupBuilder MapApiTokenEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListOwnAsync);
        group.MapGet("/all", ListAllAsync).RequireAuthorization(DdtPolicies.Administrator);
        // A role policy, so an account that still has to change a password it was shown cannot make a token that would
        // get it past that.
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Viewer).RequireSession();

        // A token may revoke itself, or another of its user's: that only ever takes rights away.
        group.MapDelete("/{id:guid}", RevokeAsync);

        return group;
    }

    private static async Task<Results<Ok<IReadOnlyList<ApiTokenView>>, UnauthorizedHttpResult>> ListOwnAsync(
        ClaimsPrincipal user,
        DdtDbContext database,
        CancellationToken cancellationToken)
    {
        if (Principals.UserId(user) is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(await ViewsAsync(database, database.ApiTokens.Where(t => t.UserId == userId), cancellationToken).ConfigureAwait(false));
    }

    private static async Task<Ok<IReadOnlyList<ApiTokenView>>> ListAllAsync(DdtDbContext database, CancellationToken cancellationToken) =>
        TypedResults.Ok(await ViewsAsync(database, database.ApiTokens, cancellationToken).ConfigureAwait(false));

    // A user has a few tokens and a server a few dozen, so they are ordered here: SQLite cannot order by DateTimeOffset.
    private static async Task<IReadOnlyList<ApiTokenView>> ViewsAsync(DdtDbContext database, IQueryable<ApiToken> tokens, CancellationToken cancellationToken)
    {
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

    private static async Task<Results<Created<CreatedApiToken>, ValidationProblem, UnauthorizedHttpResult>> CreateAsync(
        CreateApiTokenRequest request,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (Principals.UserId(user) is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        FieldProblems problems = new();
        string name = request.Name?.Trim() ?? string.Empty;
        string? role = ApiTokenRoles.Known(request.Role);
        int days = request.ExpiresInDays ?? ApiTokenLimits.DefaultDays;
        DateTimeOffset now = timeProvider.GetUtcNow();

        // The cookie's roles are those of the sign-in; the database has them as they are now.
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

        if (problems.Count > 0)
        {
            return problems.ToResult();
        }

        string secret = ApiTokenSecrets.Create();
        ApiToken token = new()
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Name = name,
            Role = role!,
            SecretHash = ApiTokenSecrets.Hash(secret),
            Hint = ApiTokenSecrets.Hint(secret),
            CreatedUtc = now,
            ExpiresUtc = now.AddDays(days),
        };

        database.ApiTokens.Add(token);
        database.AuditEvents.Add(Audit(
            AuditActions.TokenCreated,
            token,
            user,
            context,
            now,
            $"{token.Name}, {token.Role}, ending in {token.Hint}, expires {token.ExpiresUtc.ToString("u", CultureInfo.InvariantCulture)}."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ApiTokenView view = ApiTokenViews.From(token, user.Identity?.Name ?? string.Empty);
        live.TokenChanged(view);

        return TypedResults.Created($"/api/tokens/{token.Id:D}", new CreatedApiToken(view, secret));
    }

    // Revoking keeps the row, so the list still shows what the token was and who ended it. Revoking again changes nothing.
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> RevokeAsync(
        Guid id,
        ClaimsPrincipal user,
        HttpContext context,
        DdtDbContext database,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (Principals.UserId(user) is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        ApiToken? token = await database.ApiTokens.FirstOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);

        if (token is null)
        {
            return TypedResults.NotFound();
        }

        if (token.UserId != userId && !user.IsInRole(DdtRoleNames.Administrator))
        {
            return ServerProblems.Problem(ServerMessages.TokenOnlyOwnerRevokes.With(), StatusCodes.Status403Forbidden);
        }

        if (token.RevokedUtc is not null)
        {
            return TypedResults.NoContent();
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
        database.AuditEvents.Add(Audit(
            AuditActions.TokenRevoked,
            token,
            user,
            context,
            now,
            token.UserId == userId ? $"{token.Name}, ending in {token.Hint}." : $"{token.Name} of {owner}, ending in {token.Hint}."));
        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        live.TokenChanged(ApiTokenViews.From(token, owner ?? string.Empty));

        return TypedResults.NoContent();
    }

    // The role within the sentence, with the article English gives it.
    private static object WithArticle(string role) => role switch
    {
        DdtRoleNames.Administrator => ServerMessages.RoleAnAdministrator.With(),
        DdtRoleNames.Operator => ServerMessages.RoleAnOperator.With(),
        DdtRoleNames.Viewer => ServerMessages.RoleAViewer.With(),
        _ => $"a {role}",
    };

    private static AuditEvent Audit(
        string action,
        ApiToken token,
        ClaimsPrincipal user,
        HttpContext context,
        DateTimeOffset now,
        string detail) => new()
        {
            OccurredUtc = now,
            Action = action,
            ActorUserId = Principals.UserId(user),
            ActorName = user.Identity?.Name,
            SubjectId = token.Id.ToString("D"),
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
        };
}
