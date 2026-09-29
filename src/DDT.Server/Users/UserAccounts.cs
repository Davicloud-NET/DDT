// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Users;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Endpoints;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Users;

// Changes accounts and their roles for administrators. A change stages its audit row and its role and claim rows before
// the Identity call that saves the account. So one save stores all of them or none.
internal sealed class UserAccounts(
    DdtDbContext database,
    UserManager<DdtUser> userManager,
    UserViews views,
    UserChangePublisher publisher,
    LastAdministratorGuard guard,
    TimeProvider timeProvider)
{
    // DDT generates the password and the administrator hands it over. The account must replace it at its first
    // sign-in, so only its owner knows the password it uses after that.
    public async Task<UserChange> CreateAsync(CreateUserRequest request, Actor actor, CancellationToken cancellationToken)
    {
        (NewUser? account, FieldProblems problems) = NewUser.Read(request);

        if (account is null)
        {
            return UserChange.Invalid(problems);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        DdtRole stored = await StoredRoleAsync(account.Role, cancellationToken).ConfigureAwait(false);
        string password = IdentityBootstrap.GeneratePassword();
        DdtUser user = new()
        {
            Id = Guid.CreateVersion7(now),
            UserName = account.UserName,
            DisplayName = account.DisplayName,
            Email = account.Email,
            Source = AccountSource.Local,
            CreatedUtc = now,
        };

        database.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = stored.Id });
        database.UserClaims.Add(MustChangePassword(user));
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.UserCreated,
            user.Id.ToString("D"),
            actor,
            now,
            $"Local account {account.UserName} with the role {account.Role}."));

        if (await NotCreatedAsync(user, account.UserName, password).ConfigureAwait(false) is { } refused)
        {
            return refused;
        }

        UserView view = await publisher.ChangedAsync(user, closeConnections: false, cancellationToken).ConfigureAwait(false);

        return UserChange.Done(view) with { Password = password };
    }

    public async Task<UserChange> UpdateAsync(Guid id, UpdateUserRequest request, Actor actor, CancellationToken cancellationToken)
    {
        if (await FindAsync(id, cancellationToken).ConfigureAwait(false) is not { } user)
        {
            return UserChange.NotFound;
        }

        (UserUpdate update, FieldProblems problems) = UserUpdate.Read(request, user);

        if (problems.Count > 0)
        {
            return UserChange.Invalid(problems);
        }

        List<string> changes = update.Changes(user);

        // The directory writes the display name and email again at each sign-in.
        if (changes.Count > 0 && user.Source == AccountSource.Directory)
        {
            return UserChange.Refused(ServerMessages.UserDirectoryProvidesNames.With("name", user.UserName ?? ""));
        }

        string? current = await views.RoleAsync(user.Id, cancellationToken).ConfigureAwait(false);
        List<IdentityUserRole<Guid>> held = await database.UserRoles.Where(ur => ur.UserId == user.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        RoleChange? role = update.Role is { } target && (target != current || held.Count != 1)
            ? new RoleChange(current, target, held, current == DdtRoleNames.Administrator && target != DdtRoleNames.Administrator)
            : null;

        if (role is not null && views.ManagedBy(user) is { } managedBy)
        {
            return UserChange.Refused(UserViews.ManagedMessage(user, managedBy));
        }

        if (role is { Demotes: true } && actor.UserId == id)
        {
            return UserChange.Refused(ServerMessages.UserOwnAdministratorRole.With());
        }

        if (changes.Count == 0 && role is null)
        {
            return UserChange.Done(await views.ViewAsync(user, cancellationToken).ConfigureAwait(false));
        }

        return await SaveUpdateAsync(user, update, role, actor, cancellationToken).ConfigureAwait(false);
    }

    // The new security stamp ends every session of the account at its next check, within a minute. Its live
    // connections close at once.
    public async Task<UserChange> DisableAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        if (await FindAsync(id, cancellationToken).ConfigureAwait(false) is not { } user)
        {
            return UserChange.NotFound;
        }

        if (actor.UserId == id)
        {
            return UserChange.Refused(ServerMessages.UserOwnDisable.With());
        }

        if (user.IsDisabled)
        {
            return UserChange.Done(await views.ViewAsync(user, cancellationToken).ConfigureAwait(false));
        }

        await using (await guard.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await views.RoleAsync(id, cancellationToken).ConfigureAwait(false) == DdtRoleNames.Administrator
                && !await OtherEnabledAdministratorsAsync(id, cancellationToken).ConfigureAwait(false))
            {
                return UserChange.Refused(LastAdministrator(user));
            }

            user.IsDisabled = true;
            Audit(AuditActions.UserDisabled, user, actor, $"Disabled {user.UserName}.");

            if (NotSaved(await userManager.UpdateSecurityStampAsync(user).ConfigureAwait(false)) is { } failure)
            {
                return failure;
            }
        }

        return UserChange.Done(await publisher.ChangedAsync(user, closeConnections: true, cancellationToken).ConfigureAwait(false));
    }

    public async Task<UserChange> EnableAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        if (await FindAsync(id, cancellationToken).ConfigureAwait(false) is not { } user)
        {
            return UserChange.NotFound;
        }

        if (!user.IsDisabled)
        {
            return UserChange.Done(await views.ViewAsync(user, cancellationToken).ConfigureAwait(false));
        }

        user.IsDisabled = false;
        Audit(AuditActions.UserEnabled, user, actor, $"Enabled {user.UserName}.");

        if (NotSaved(await userManager.UpdateAsync(user).ConfigureAwait(false)) is { } failure)
        {
            return failure;
        }

        return UserChange.Done(await publisher.ChangedAsync(user, closeConnections: false, cancellationToken).ConfigureAwait(false));
    }

    // A reset also ends a lockout, so the new password works at once. An administrator's own password is changed on
    // the Account page, which asks for the current one.
    public async Task<UserChange> ResetPasswordAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        if (await FindAsync(id, cancellationToken).ConfigureAwait(false) is not { } user)
        {
            return UserChange.NotFound;
        }

        ServerMessage? refusal = user.Source switch
        {
            _ when actor.UserId == id => ServerMessages.UserOwnPassword.With(),
            AccountSource.Directory => ServerMessages.UserDirectoryPassword.With("name", user.UserName ?? ""),
            AccountSource.External => ServerMessages.UserSingleSignOnPassword.With("name", user.UserName ?? ""),
            _ => null,
        };

        if (refusal is not null)
        {
            return UserChange.Refused(refusal);
        }

        string password = IdentityBootstrap.GeneratePassword();

        if (!await database.UserClaims.AnyAsync(c => c.UserId == id && c.ClaimType == DdtClaimTypes.MustChangePassword, cancellationToken).ConfigureAwait(false))
        {
            database.UserClaims.Add(MustChangePassword(user));
        }

        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        Audit(AuditActions.UserPasswordReset, user, actor, $"Gave {user.UserName} a new password, which it must change at its next sign-in.");

        string token = await userManager.GeneratePasswordResetTokenAsync(user).ConfigureAwait(false);

        if (NotSaved(await userManager.ResetPasswordAsync(user, token, password).ConfigureAwait(false)) is { } failure)
        {
            return failure;
        }

        UserView view = await publisher.ChangedAsync(user, closeConnections: true, cancellationToken).ConfigureAwait(false);

        return UserChange.Done(view) with { Password = password };
    }

    // For an account that lost its authenticator. The new key makes the old one worthless, and the account enrolls
    // again from the Account page. An administrator's own second factor is turned off there, with a current code.
    public async Task<UserChange> ResetTwoFactorAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        if (await FindAsync(id, cancellationToken).ConfigureAwait(false) is not { } user)
        {
            return UserChange.NotFound;
        }

        if (actor.UserId == id)
        {
            return UserChange.Refused(ServerMessages.UserOwnSecondFactor.With());
        }

        if (!user.TwoFactorEnabled)
        {
            return UserChange.Done(await views.ViewAsync(user, cancellationToken).ConfigureAwait(false));
        }

        user.TwoFactorEnabled = false;
        Audit(AuditActions.UserTwoFactorReset, user, actor, $"Turned off the second factor of {user.UserName}.");

        if (NotSaved(await userManager.ResetAuthenticatorKeyAsync(user).ConfigureAwait(false)) is { } failure)
        {
            return failure;
        }

        return UserChange.Done(await publisher.ChangedAsync(user, closeConnections: true, cancellationToken).ConfigureAwait(false));
    }

    // A deleted directory or single sign-on account comes back at its next sign-in, with the roles its groups give.
    // Only disabling keeps it out.
    public async Task<UserChange> DeleteAsync(Guid id, Actor actor, CancellationToken cancellationToken)
    {
        if (await FindAsync(id, cancellationToken).ConfigureAwait(false) is not { } user)
        {
            return UserChange.NotFound;
        }

        if (actor.UserId == id)
        {
            return UserChange.Refused(ServerMessages.UserOwnDelete.With());
        }

        await using (await guard.EnterAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!user.IsDisabled
                && await views.RoleAsync(id, cancellationToken).ConfigureAwait(false) == DdtRoleNames.Administrator
                && !await OtherEnabledAdministratorsAsync(id, cancellationToken).ConfigureAwait(false))
            {
                return UserChange.Refused(LastAdministrator(user));
            }

            Audit(AuditActions.UserDeleted, user, actor, $"Deleted the {user.Source.ToString().ToLowerInvariant()} account {user.UserName}.");

            if (NotSaved(await userManager.DeleteAsync(user).ConfigureAwait(false)) is { } failure)
            {
                return failure;
            }
        }

        publisher.Removed(id);

        return new UserChange(UserChangeStatus.Done);
    }

    // Demoting an enabled administrator holds the guard, so it can't remove the last one.
    private async Task<UserChange> SaveUpdateAsync(
        DdtUser user,
        UserUpdate update,
        RoleChange? role,
        Actor actor,
        CancellationToken cancellationToken)
    {
        List<string> changes = update.Changes(user);
        bool guarded = role is { Demotes: true } && !user.IsDisabled;

        await using (guarded ? await guard.EnterAsync(cancellationToken).ConfigureAwait(false) : null)
        {
            if (guarded && !await OtherEnabledAdministratorsAsync(user.Id, cancellationToken).ConfigureAwait(false))
            {
                return UserChange.Refused(LastAdministrator(user));
            }

            user.DisplayName = update.DisplayName;
            user.Email = update.Email;

            if (role is not null)
            {
                await ReplaceRoleAsync(user, role, cancellationToken).ConfigureAwait(false);
                changes.Add(UserUpdate.Change("role", role.Current, role.Target));
            }

            Audit(AuditActions.UserChanged, user, actor, string.Join("; ", changes));

            if (NotSaved(await userManager.UpdateAsync(user).ConfigureAwait(false)) is { } failure)
            {
                return failure;
            }
        }

        return UserChange.Done(await publisher.ChangedAsync(user, closeConnections: role is not null, cancellationToken).ConfigureAwait(false));
    }

    private async Task ReplaceRoleAsync(DdtUser user, RoleChange role, CancellationToken cancellationToken)
    {
        DdtRole target = await StoredRoleAsync(role.Target, cancellationToken).ConfigureAwait(false);

        database.UserRoles.RemoveRange(role.Held.Where(ur => ur.RoleId != target.Id));

        if (!role.Held.Exists(ur => ur.RoleId == target.Id))
        {
            database.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = target.Id });
        }

        // An administrator chose this role, so it no longer counts as a role DDT gave.
        database.UserTokens.RemoveRange(await database.UserTokens
            .Where(t => t.UserId == user.Id && t.LoginProvider == UserViews.MarkerProvider && t.Name == UserViews.ProvisionedMarker)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));
    }

    private async Task<UserChange?> NotCreatedAsync(DdtUser user, string userName, string password)
    {
        IdentityResult created;

        try
        {
            created = await userManager.CreateAsync(user, password).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another administrator took the name a moment earlier, and the unique index kept only theirs.
            database.ChangeTracker.Clear();
            FieldProblems taken = new();
            taken.Add("userName", ServerMessages.UserNameTaken.With("name", userName));

            return UserChange.Invalid(taken);
        }

        return NotSaved(created);
    }

    private Task<DdtUser?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        database.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    private void Audit(string action, DdtUser user, Actor actor, string detail) =>
        database.AuditEvents.Add(AuditEvents.Create(action, user.Id.ToString("D"), actor, timeProvider.GetUtcNow(), detail));

    // On failure, everything this request staged for the save is thrown away.
    private UserChange? NotSaved(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return null;
        }

        database.ChangeTracker.Clear();

        return UserChange.NotSaved(result);
    }

    private static ServerMessage LastAdministrator(DdtUser user) => ServerMessages.UserLastAdministrator.With("name", user.UserName ?? "");

    private async Task<bool> OtherEnabledAdministratorsAsync(Guid id, CancellationToken cancellationToken) =>
        (await views.EnabledAdministratorsAsync(cancellationToken).ConfigureAwait(false)).Exists(administrator => administrator != id);

    private Task<DdtRole> StoredRoleAsync(string role, CancellationToken cancellationToken)
    {
        string normalized = role.ToUpperInvariant();

        return database.Roles.SingleAsync(r => r.NormalizedName == normalized, cancellationToken);
    }

    private static IdentityUserClaim<Guid> MustChangePassword(DdtUser user) =>
        new() { UserId = user.Id, ClaimType = DdtClaimTypes.MustChangePassword, ClaimValue = "true" };

    // Held is the account's role rows. The target role replaces them.
    private sealed record RoleChange(string? Current, string Target, List<IdentityUserRole<Guid>> Held, bool Demotes);
}
