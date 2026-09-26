// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Mail;
using System.Security.Claims;
using DDT.Contracts.Users;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Users;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Endpoints;

// Accounts and their roles, for administrators. A change stages its audit row, and the rows of roles and claims it
// makes, before the Identity call that saves the account, so that one save stores all of them or none. It reaches the
// other administrators' pages with the account as it is now, and when it takes something from the account it closes the
// account's live connections, which then connect again with what the account may see now.
public static class UserEndpoints
{
    private const int MaxTextLength = 256;

    // Changes that can leave an enabled account without the Administrator role are made one at a time. Two
    // administrators who disable each other at the same moment would otherwise both succeed and leave none.
    private static readonly SemaphoreSlim s_administratorChanges = new(1, 1);

    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/", ListAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/", CreateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPatch("/{id:guid}", UpdateAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/{id:guid}/disable", DisableAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/{id:guid}/enable", EnableAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/{id:guid}/reset-password", ResetPasswordAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapPost("/{id:guid}/reset-two-factor", ResetTwoFactorAsync).RequireAuthorization(DdtPolicies.Administrator);
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(DdtPolicies.Administrator);

        return group;
    }

    private static async Task<Ok<IReadOnlyList<UserView>>> ListAsync(UserViews views, CancellationToken cancellationToken) =>
        TypedResults.Ok(await views.ListAsync(cancellationToken).ConfigureAwait(false));

    // A local account with a password DDT makes up, which the administrator hands over and the account replaces at its
    // first sign-in, so that only its owner knows the password it then uses.
    private static async Task<Results<Created<CreatedUser>, ValidationProblem>> CreateAsync(
        CreateUserRequest request,
        ClaimsPrincipal actor,
        HttpContext context,
        DdtDbContext database,
        UserManager<DdtUser> userManager,
        UserViews views,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> problems = [];
        string? userName = Clean(request.UserName);
        string? displayName = Clean(request.DisplayName);
        (string? email, string? emailProblem) = Email(request.Email);
        string? role = DdtRoleNames.Canonical(request.Role);

        if (userName is null)
        {
            problems["userName"] = ["Enter a user name."];
        }
        else if (userName.Length > MaxTextLength)
        {
            problems["userName"] = [$"A user name can have at most {MaxTextLength} characters."];
        }

        if (displayName is null)
        {
            problems["displayName"] = ["Enter the name DDT shows for the account."];
        }
        else if (displayName.Length > MaxTextLength)
        {
            problems["displayName"] = [$"A display name can have at most {MaxTextLength} characters."];
        }

        if (emailProblem is not null)
        {
            problems["email"] = [emailProblem];
        }

        if (role is null)
        {
            problems["role"] = [RoleProblem];
        }

        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DdtRole stored = await StoredRoleAsync(database, role!, cancellationToken).ConfigureAwait(false);
        string password = IdentityBootstrap.GeneratePassword();
        DdtUser user = new()
        {
            Id = Guid.CreateVersion7(now),
            UserName = userName,
            DisplayName = displayName,
            Email = email,
            Source = AccountSource.Local,
            CreatedUtc = now,
        };

        database.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = stored.Id });
        database.UserClaims.Add(MustChangePassword(user));
        database.AuditEvents.Add(Audit(AuditActions.UserCreated, user, actor, context, now, $"Local account {userName} with the role {role}."));

        IdentityResult created;

        try
        {
            created = await userManager.CreateAsync(user, password).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another administrator took the name a moment earlier, and the unique index kept only theirs.
            database.ChangeTracker.Clear();

            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userName"] = [$"There is an account named {userName} already."],
            });
        }

        if (!created.Succeeded)
        {
            database.ChangeTracker.Clear();

            return TypedResults.ValidationProblem(Problems(created));
        }

        UserView view = await views.ViewAsync(user, cancellationToken).ConfigureAwait(false);
        live.UserChanged(view);

        return TypedResults.Created($"/api/users/{user.Id:D}", new CreatedUser(view, password));
    }

    private static async Task<Results<Ok<UserView>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        Guid id,
        UpdateUserRequest request,
        ClaimsPrincipal actor,
        HttpContext context,
        DdtDbContext database,
        UserManager<DdtUser> userManager,
        UserViews views,
        LiveNotifier live,
        LiveConnections connections,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DdtUser? user = await database.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return TypedResults.NotFound();
        }

        Dictionary<string, string[]> problems = [];
        string? displayName = request.DisplayName is null ? user.DisplayName : Clean(request.DisplayName);
        (string? email, string? emailProblem) = request.Email is null ? (user.Email, null) : Email(request.Email);
        string? role = request.Role is null ? null : DdtRoleNames.Canonical(request.Role);

        if (displayName?.Length > MaxTextLength)
        {
            problems["displayName"] = [$"A display name can have at most {MaxTextLength} characters."];
        }

        if (emailProblem is not null)
        {
            problems["email"] = [emailProblem];
        }

        if (request.Role is not null && role is null)
        {
            problems["role"] = [RoleProblem];
        }

        if (problems.Count > 0)
        {
            return TypedResults.ValidationProblem(problems);
        }

        List<string> changes = [];

        if (!string.Equals(displayName, user.DisplayName, StringComparison.Ordinal))
        {
            changes.Add(Change("displayName", user.DisplayName, displayName));
        }

        if (!string.Equals(email, user.Email, StringComparison.Ordinal))
        {
            changes.Add(Change("email", user.Email, email));
        }

        // The directory writes both again at each sign-in.
        if (changes.Count > 0 && user.Source == AccountSource.Directory)
        {
            return Conflict($"The directory provides the display name and email address of {user.UserName}. Change them there; DDT takes them over at its next sign-in.");
        }

        string? current = await views.RoleAsync(user.Id, cancellationToken).ConfigureAwait(false);
        List<IdentityUserRole<Guid>> held = await database.UserRoles.Where(ur => ur.UserId == user.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        bool roleChanges = role is not null && (role != current || held.Count != 1);

        if (roleChanges && views.ManagedBy(user) is { } managedBy)
        {
            return Conflict(UserViews.ManagedMessage(user, managedBy));
        }

        bool demotes = roleChanges && current == DdtRoleNames.Administrator && role != DdtRoleNames.Administrator;

        if (demotes && Principals.UserId(actor) == id)
        {
            return Conflict("You cannot take the Administrator role from your own account. Another administrator can.");
        }

        if (changes.Count == 0 && !roleChanges)
        {
            return TypedResults.Ok(await views.ViewAsync(user, cancellationToken).ConfigureAwait(false));
        }

        bool guarded = demotes && !user.IsDisabled;

        if (guarded)
        {
            await s_administratorChanges.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (guarded && !await OtherEnabledAdministratorsAsync(views, id, cancellationToken).ConfigureAwait(false))
            {
                return Conflict(LastAdministrator(user));
            }

            user.DisplayName = displayName;
            user.Email = email;

            if (roleChanges)
            {
                DdtRole target = await StoredRoleAsync(database, role!, cancellationToken).ConfigureAwait(false);

                database.UserRoles.RemoveRange(held.Where(ur => ur.RoleId != target.Id));

                if (!held.Exists(ur => ur.RoleId == target.Id))
                {
                    database.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = target.Id });
                }

                // An administrator chose this role, so it no longer counts as the one DDT gave.
                database.UserTokens.RemoveRange(await database.UserTokens
                    .Where(t => t.UserId == user.Id && t.LoginProvider == UserViews.MarkerProvider && t.Name == UserViews.ProvisionedMarker)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false));
                changes.Add(Change("role", current, role));
            }

            database.AuditEvents.Add(Audit(AuditActions.UserChanged, user, actor, context, timeProvider.GetUtcNow(), string.Join("; ", changes)));

            if (Failure(await userManager.UpdateAsync(user).ConfigureAwait(false), database) is { } failure)
            {
                return failure;
            }
        }
        finally
        {
            if (guarded)
            {
                s_administratorChanges.Release();
            }
        }

        if (roleChanges)
        {
            connections.Close(user.Id);
        }

        UserView view = await views.ViewAsync(user, cancellationToken).ConfigureAwait(false);
        live.UserChanged(view);

        return TypedResults.Ok(view);
    }

    // The new security stamp ends every session of the account at its next check, within a minute, and its live
    // connections at once.
    private static async Task<Results<Ok<UserView>, NotFound, ProblemHttpResult>> DisableAsync(
        Guid id,
        ClaimsPrincipal actor,
        HttpContext context,
        DdtDbContext database,
        UserManager<DdtUser> userManager,
        UserViews views,
        LiveNotifier live,
        LiveConnections connections,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DdtUser? user = await database.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (Principals.UserId(actor) == id)
        {
            return Conflict("You cannot disable your own account. Another administrator can.");
        }

        if (user.IsDisabled)
        {
            return TypedResults.Ok(await views.ViewAsync(user, cancellationToken).ConfigureAwait(false));
        }

        await s_administratorChanges.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (await views.RoleAsync(id, cancellationToken).ConfigureAwait(false) == DdtRoleNames.Administrator
                && !await OtherEnabledAdministratorsAsync(views, id, cancellationToken).ConfigureAwait(false))
            {
                return Conflict(LastAdministrator(user));
            }

            user.IsDisabled = true;
            database.AuditEvents.Add(Audit(AuditActions.UserDisabled, user, actor, context, timeProvider.GetUtcNow(), $"Disabled {user.UserName}."));

            if (Failure(await userManager.UpdateSecurityStampAsync(user).ConfigureAwait(false), database) is { } failure)
            {
                return failure;
            }
        }
        finally
        {
            s_administratorChanges.Release();
        }

        connections.Close(id);

        UserView view = await views.ViewAsync(user, cancellationToken).ConfigureAwait(false);
        live.UserChanged(view);

        return TypedResults.Ok(view);
    }

    private static async Task<Results<Ok<UserView>, NotFound, ProblemHttpResult>> EnableAsync(
        Guid id,
        ClaimsPrincipal actor,
        HttpContext context,
        DdtDbContext database,
        UserManager<DdtUser> userManager,
        UserViews views,
        LiveNotifier live,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DdtUser? user = await database.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (!user.IsDisabled)
        {
            return TypedResults.Ok(await views.ViewAsync(user, cancellationToken).ConfigureAwait(false));
        }

        user.IsDisabled = false;
        database.AuditEvents.Add(Audit(AuditActions.UserEnabled, user, actor, context, timeProvider.GetUtcNow(), $"Enabled {user.UserName}."));

        if (Failure(await userManager.UpdateAsync(user).ConfigureAwait(false), database) is { } failure)
        {
            return failure;
        }

        UserView view = await views.ViewAsync(user, cancellationToken).ConfigureAwait(false);
        live.UserChanged(view);

        return TypedResults.Ok(view);
    }

    // A reset also ends a lockout, so the new password works at once. An administrator's own password is changed on
    // the Account page, which asks for the current one.
    private static async Task<Results<Ok<OneTimePassword>, NotFound, ProblemHttpResult>> ResetPasswordAsync(
        Guid id,
        ClaimsPrincipal actor,
        HttpContext context,
        DdtDbContext database,
        UserManager<DdtUser> userManager,
        UserViews views,
        LiveNotifier live,
        LiveConnections connections,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DdtUser? user = await database.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (Principals.UserId(actor) == id)
        {
            return Conflict("Change your own password on the Account page.");
        }

        if (user.Source == AccountSource.Directory)
        {
            return Conflict($"{user.UserName} signs in with its directory password. Reset it in the directory.");
        }

        if (user.Source == AccountSource.External)
        {
            return Conflict($"{user.UserName} signs in through single sign-on and has no password in DDT.");
        }

        string password = IdentityBootstrap.GeneratePassword();

        if (!await database.UserClaims.AnyAsync(c => c.UserId == id && c.ClaimType == DdtClaimTypes.MustChangePassword, cancellationToken).ConfigureAwait(false))
        {
            database.UserClaims.Add(MustChangePassword(user));
        }

        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        database.AuditEvents.Add(Audit(
            AuditActions.UserPasswordReset,
            user,
            actor,
            context,
            timeProvider.GetUtcNow(),
            $"Gave {user.UserName} a new password, which it must change at its next sign-in."));

        string token = await userManager.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);

        if (Failure(await userManager.ResetPasswordAsync(user, token, password).ConfigureAwait(false), database) is { } failure)
        {
            return failure;
        }

        connections.Close(id);
        live.UserChanged(await views.ViewAsync(user, cancellationToken).ConfigureAwait(false));

        return TypedResults.Ok(new OneTimePassword(password));
    }

    // For an account that lost its authenticator. The new key makes the old one worthless, and the account enrolls
    // again from the Account page. An administrator's own second factor is turned off there, with a current code.
    private static async Task<Results<Ok<UserView>, NotFound, ProblemHttpResult>> ResetTwoFactorAsync(
        Guid id,
        ClaimsPrincipal actor,
        HttpContext context,
        DdtDbContext database,
        UserManager<DdtUser> userManager,
        UserViews views,
        LiveNotifier live,
        LiveConnections connections,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DdtUser? user = await database.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (Principals.UserId(actor) == id)
        {
            return Conflict("Turn off your own second factor on the Account page.");
        }

        if (!user.TwoFactorEnabled)
        {
            return TypedResults.Ok(await views.ViewAsync(user, cancellationToken).ConfigureAwait(false));
        }

        user.TwoFactorEnabled = false;
        database.AuditEvents.Add(Audit(
            AuditActions.UserTwoFactorReset,
            user,
            actor,
            context,
            timeProvider.GetUtcNow(),
            $"Turned off the second factor of {user.UserName}."));

        if (Failure(await userManager.ResetAuthenticatorKeyAsync(user).ConfigureAwait(false), database) is { } failure)
        {
            return failure;
        }

        connections.Close(id);

        UserView view = await views.ViewAsync(user, cancellationToken).ConfigureAwait(false);
        live.UserChanged(view);

        return TypedResults.Ok(view);
    }

    // A directory or single sign-on account comes back at its next sign-in, with the roles its groups give. Disabling
    // is what keeps it out.
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(
        Guid id,
        ClaimsPrincipal actor,
        HttpContext context,
        DdtDbContext database,
        UserManager<DdtUser> userManager,
        UserViews views,
        LiveNotifier live,
        LiveConnections connections,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        DdtUser? user = await database.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (Principals.UserId(actor) == id)
        {
            return Conflict("You cannot delete your own account. Another administrator can.");
        }

        await s_administratorChanges.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!user.IsDisabled
                && await views.RoleAsync(id, cancellationToken).ConfigureAwait(false) == DdtRoleNames.Administrator
                && !await OtherEnabledAdministratorsAsync(views, id, cancellationToken).ConfigureAwait(false))
            {
                return Conflict(LastAdministrator(user));
            }

            database.AuditEvents.Add(Audit(
                AuditActions.UserDeleted,
                user,
                actor,
                context,
                timeProvider.GetUtcNow(),
                $"Deleted the {user.Source.ToString().ToLowerInvariant()} account {user.UserName}."));

            if (Failure(await userManager.DeleteAsync(user).ConfigureAwait(false), database) is { } failure)
            {
                return failure;
            }
        }
        finally
        {
            s_administratorChanges.Release();
        }

        connections.Close(id);
        live.UsersRemoved([id]);

        return TypedResults.NoContent();
    }

    private const string RoleProblem = "Choose Administrator, Operator or Viewer.";

    private static string LastAdministrator(DdtUser user) =>
        $"{user.UserName} is the last enabled administrator. Make another account an administrator first.";

    private static async Task<bool> OtherEnabledAdministratorsAsync(UserViews views, Guid id, CancellationToken cancellationToken) =>
        (await views.EnabledAdministratorsAsync(cancellationToken).ConfigureAwait(false)).Exists(administrator => administrator != id);

    private static Task<DdtRole> StoredRoleAsync(DdtDbContext database, string role, CancellationToken cancellationToken)
    {
        string normalized = role.ToUpperInvariant();

        return database.Roles.SingleAsync(r => r.NormalizedName == normalized, cancellationToken);
    }

    private static IdentityUserClaim<Guid> MustChangePassword(DdtUser user) =>
        new() { UserId = user.Id, ClaimType = DdtClaimTypes.MustChangePassword, ClaimValue = "true" };

    private static ProblemHttpResult Conflict(string title) => TypedResults.Problem(title: title, statusCode: StatusCodes.Status409Conflict);

    // What an Identity call that saves refused, with what this request staged for that save thrown away. The calls
    // after validation only fail when the account changed meanwhile, or the store failed.
    private static ProblemHttpResult? Failure(IdentityResult result, DdtDbContext database)
    {
        if (result.Succeeded)
        {
            return null;
        }

        database.ChangeTracker.Clear();

        return result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure))
            ? Conflict("The account changed while this was saved. Look at it again.")
            : TypedResults.Problem(title: string.Join(" ", result.Errors.Select(error => error.Description)), statusCode: StatusCodes.Status400BadRequest);
    }

    private static Dictionary<string, string[]> Problems(IdentityResult result) =>
        result.Errors
            .GroupBy(
                error => error.Code is nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.InvalidUserName)
                    ? "userName"
                    : error.Code,
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray(), StringComparer.Ordinal);

    private static string? Clean(string? value) =>
        value?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim() is { Length: > 0 } text ? text : null;

    // An empty address clears it. A display name part, as in "Jane <jane@corp.example>", is refused rather than kept.
    private static (string? Email, string? Problem) Email(string? value)
    {
        string? email = Clean(value);

        if (email is null)
        {
            return (null, null);
        }

        return email.Length <= MaxTextLength && MailAddress.TryCreate(email, out MailAddress? parsed) && parsed.Address == email
            ? (email, null)
            : (null, "Enter an email address such as jane@corp.example, or leave it empty.");
    }

    private static string Change(string field, string? from, string? to) => $"{field}: '{from}' to '{to}'";

    private static AuditEvent Audit(
        string action,
        DdtUser user,
        ClaimsPrincipal actor,
        HttpContext context,
        DateTimeOffset now,
        string detail) => new()
        {
            OccurredUtc = now,
            Action = action,
            ActorUserId = Principals.UserId(actor),
            ActorName = actor.Identity?.Name,
            SubjectId = user.Id.ToString("D"),
            SourceAddress = context.Connection.RemoteIpAddress?.ToString(),
            Detail = StoredText.Bound(detail, AuditEvent.MaxDetailLength),
        };
}
