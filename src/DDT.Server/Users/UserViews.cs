// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Users;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Users;

// What the Users page shows of an account. Where a role comes from follows from the account's source and the group maps
// of this moment, so an emptied map hands its accounts back to administrators at once. That single sign-on gave the
// role, and nobody changed it since, is kept as a token row, where Identity keeps per-account values.
public sealed class UserViews(
    DdtDbContext database,
    DdtSettings settings,
    TimeProvider timeProvider)
{
    public const string MarkerProvider = "[DDT]";

    public const string ProvisionedMarker = "RoleProvisioned";

    // The groups that decide the account's role at each of its sign-ins, or null when administrators do.
    public RoleSource? ManagedBy(DdtUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.Source switch
        {
            AccountSource.Directory when settings.Current.Ldap.GroupRoleMap.Count > 0 => RoleSource.DirectoryGroups,
            AccountSource.External when settings.Current.Oidc.GroupRoleMap.Count > 0 => RoleSource.SingleSignOnGroups,
            _ => null,
        };
    }

    // Says where to change a role that DDT may not change, for the refusal.
    public static ServerMessage ManagedMessage(DdtUser user, RoleSource managedBy)
    {
        ArgumentNullException.ThrowIfNull(user);

        return (managedBy == RoleSource.DirectoryGroups ? ServerMessages.UserRoleFromDirectoryGroups : ServerMessages.UserRoleFromSingleSignOnGroups)
            .With("name", user.UserName ?? "");
    }

    public async Task<IReadOnlyList<UserView>> ListAsync(CancellationToken cancellationToken)
    {
        List<DdtUser> users = await database.Users.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        Facts facts = await ReadFactsAsync(null, cancellationToken).ConfigureAwait(false);

        return [.. users.OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase).Select(u => View(u, facts))];
    }

    public async Task<UserView> ViewAsync(DdtUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return View(user, await ReadFactsAsync(user.Id, cancellationToken).ConfigureAwait(false));
    }

    // Read from the store, not from what this request may have staged, so a view taken after a save shows what was saved.
    public async Task<string?> RoleAsync(Guid userId, CancellationToken cancellationToken) =>
        DdtRoleNames.Highest(await RoleNames(userId).ToListAsync(cancellationToken).ConfigureAwait(false));

    public async Task<List<Guid>> EnabledAdministratorsAsync(CancellationToken cancellationToken) =>
        await database.UserRoles
            .Join(database.Roles.Where(r => r.Name == DdtRoleNames.Administrator), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Join(database.Users.Where(u => !u.IsDisabled), id => id, u => u.Id, (id, _) => id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private IQueryable<string?> RoleNames(Guid userId) =>
        database.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(database.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name);

    private UserView View(DdtUser user, Facts facts)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        string? role = DdtRoleNames.Highest(facts.Roles[user.Id]);
        RoleSource? roleFrom = ManagedBy(user)
            ?? (role is null ? null : facts.Provisioned.Contains(user.Id) ? RoleSource.Provisioned : RoleSource.Manual);

        return new UserView(
            user.Id,
            user.UserName ?? string.Empty,
            user.DisplayName,
            user.Email,
            user.Source switch
            {
                AccountSource.Directory => UserSource.Directory,
                AccountSource.External => UserSource.External,
                _ => UserSource.Local,
            },
            role,
            roleFrom,
            user.IsDisabled,
            user.LockoutEnd > now ? user.LockoutEnd : null,
            user.TwoFactorEnabled,
            user.PasswordHash is not null,
            facts.MustChangePassword.Contains(user.Id),
            facts.Providers[user.Id].FirstOrDefault(),
            user.CreatedUtc,
            user.LastSignInUtc);
    }

    private async Task<Facts> ReadFactsAsync(Guid? only, CancellationToken cancellationToken)
    {
        IQueryable<IdentityUserRole<Guid>> userRoles = database.UserRoles;
        IQueryable<IdentityUserLogin<Guid>> logins = database.UserLogins;
        IQueryable<IdentityUserClaim<Guid>> mustChange = database.UserClaims.Where(c => c.ClaimType == DdtClaimTypes.MustChangePassword);
        IQueryable<IdentityUserToken<Guid>> provisioned = database.UserTokens.Where(t => t.LoginProvider == MarkerProvider && t.Name == ProvisionedMarker);

        if (only is { } id)
        {
            userRoles = userRoles.Where(ur => ur.UserId == id);
            logins = logins.Where(l => l.UserId == id);
            mustChange = mustChange.Where(c => c.UserId == id);
            provisioned = provisioned.Where(t => t.UserId == id);
        }

        var roles = await userRoles
            .Join(database.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var providers = await logins
            .Select(l => new { l.UserId, l.LoginProvider, l.ProviderDisplayName })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        OidcOptions single = settings.Current.Oidc;

        return new Facts(
            roles.ToLookup(r => r.UserId, r => r.Name),
            providers.ToLookup(
                l => l.UserId,
                l => l.LoginProvider == OidcOptions.SchemeName ? single.DisplayName : l.ProviderDisplayName ?? l.LoginProvider),
            [.. await mustChange.Select(c => c.UserId).ToListAsync(cancellationToken).ConfigureAwait(false)],
            [.. await provisioned.Select(t => t.UserId).ToListAsync(cancellationToken).ConfigureAwait(false)]);
    }

    private sealed record Facts(
        ILookup<Guid, string?> Roles,
        ILookup<Guid, string> Providers,
        HashSet<Guid> MustChangePassword,
        HashSet<Guid> Provisioned);
}
