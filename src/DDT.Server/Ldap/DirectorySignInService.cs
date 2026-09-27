// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Users;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Settings;
using DDT.Server.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Ldap;

public sealed partial class DirectorySignInService(
    ILdapAuthenticator authenticator,
    UserManager<DdtUser> userManager,
    SignInManager<DdtUser> signInManager,
    UserActivity activity,
    DdtSettings settings,
    ILogger<DirectorySignInService> logger)
{
    // Once per scope, as the authenticator takes it: a change applies at the next sign-in.
    private readonly LdapOptions _options = settings.Current.Ldap;

    public bool Enabled => _options.Enabled;

    // Enabled, and with the server and the part of the directory to search.
    public bool Configured => _options.Enabled && !string.IsNullOrWhiteSpace(_options.Host) && !string.IsNullOrWhiteSpace(_options.BaseDn);

    public async Task<SignInResult> SignInAsync(string userName, string password, CancellationToken cancellationToken)
    {
        (SignInResult result, DdtUser? user) = await AuthenticateAsync(userName, password, cancellationToken).ConfigureAwait(false);

        if (result.Succeeded && user is not null)
        {
            await signInManager.SignInAsync(user, isPersistent: false).ConfigureAwait(false);
        }

        return result;
    }

    // Everything a directory sign in checks and updates, without issuing a cookie. The user is returned only
    // on success. A user whose groups give no role while the map decides roles gets NoRoleSignInResult.
    public async Task<(SignInResult Result, DdtUser? User)> AuthenticateAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return (SignInResult.Failed, null);
        }

        DdtUser? existing = await userManager.FindByNameAsync(userName).ConfigureAwait(false);

        if (existing is not null)
        {
            if (existing.Source != AccountSource.Directory)
            {
                return (SignInResult.Failed, null);
            }

            if (existing.IsDisabled)
            {
                return (SignInResult.NotAllowed, null);
            }

            // DDT applies its own lockout before forwarding anything to the directory. Without
            // this brake, DDT is a convenient way to lock out arbitrary domain accounts.
            if (await userManager.IsLockedOutAsync(existing).ConfigureAwait(false))
            {
                return (SignInResult.LockedOut, null);
            }
        }

        LdapIdentity? identity = await authenticator
            .AuthenticateAsync(userName, password, cancellationToken)
            .ConfigureAwait(false);

        if (identity is null)
        {
            if (existing is not null)
            {
                await userManager.AccessFailedAsync(existing).ConfigureAwait(false);
            }

            return (SignInResult.Failed, null);
        }

        GroupRoles mapped = GroupRoles.From(identity.GroupDns, _options.GroupRoleMap);

        // The directory is authoritative: an account whose groups no longer give it a role loses the one it had, and no
        // account is made for a user whose groups never gave one.
        if (mapped.Decides && mapped.Role is null)
        {
            if (await KnownAsync(identity.ImmutableId, existing).ConfigureAwait(false) is { } known
                && (await ApplyRoleAsync(known, null).ConfigureAwait(false)).Changed)
            {
                await activity.ChangedAsync(known, cancellationToken).ConfigureAwait(false);
            }

            LogNoRole(identity.UserName, identity.ImmutableId);

            return (NoRoleSignInResult.Instance, null);
        }

        (DdtUser? user, bool created) = await ReconcileAsync(identity, existing).ConfigureAwait(false);

        if (user is null)
        {
            return (SignInResult.Failed, null);
        }

        if (user.IsDisabled)
        {
            return (SignInResult.NotAllowed, null);
        }

        // A user Identity refused to save has no row, so handing it back would sign in an account that does not
        // exist, with roles that were never stored.
        if (!Saved(user, await userManager.ResetAccessFailedCountAsync(user).ConfigureAwait(false)))
        {
            return (SignInResult.Failed, null);
        }

        (bool saved, bool changed) = mapped.Decides ? await ApplyRoleAsync(user, mapped.Role).ConfigureAwait(false) : (true, false);

        if (!saved)
        {
            return (SignInResult.Failed, null);
        }

        if (created || changed)
        {
            await activity.ChangedAsync(user, cancellationToken).ConfigureAwait(false);
        }

        LogDirectorySignIn(user.UserName ?? string.Empty, identity.ImmutableId);

        return (SignInResult.Success, user);
    }

    // What a sign-in with this user name would give and why, read with the bind account: no password, no account made,
    // no cookie. It follows the sign-in's own order, so the first reason that would stop a sign-in is the one given.
    // Throws LdapUnavailableException when the directory cannot be asked.
    public async Task<DirectoryCheck> CheckAsync(string userName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        LdapLookup lookup = await authenticator.LookUpAsync(userName, cancellationToken).ConfigureAwait(false);

        switch (lookup.Status)
        {
            case LdapLookupStatus.NotFound:
                return NotFound(ServerMessages.DirectoryNoEntry.With("baseDn", _options.BaseDn, "name", userName));
            case LdapLookupStatus.Ambiguous:
                return NotFound(ServerMessages.DirectoryManyEntries.With("baseDn", _options.BaseDn, "name", userName));
        }

        GroupRoles mapped = GroupRoles.From(lookup.GroupDns, _options.GroupRoleMap);
        DirectoryCheck Found(string? role, ServerMessage message) => new(
            true,
            lookup.DistinguishedName,
            lookup.DisplayName,
            lookup.GroupDns,
            [.. mapped.Matches.Select(match => new DirectoryGroupMatch(match.Group, match.Role))],
            role,
            message.Text,
            message.Code,
            message.Args);

        DdtUser? account = await userManager.FindByNameAsync(userName).ConfigureAwait(false);

        if (account?.Source == AccountSource.Local)
        {
            return Found(null, ServerMessages.DirectoryLocalAccount.With("name", userName));
        }

        if (account?.Source == AccountSource.External)
        {
            return Found(null, ServerMessages.DirectorySingleSignOnAccount.With("name", userName));
        }

        if (lookup.ImmutableId is null)
        {
            return Found(null, ServerMessages.DirectoryNoImmutableId.With("attribute", _options.ImmutableIdAttribute));
        }

        DdtUser? known = await KnownAsync(lookup.ImmutableId, account).ConfigureAwait(false);

        if (known?.IsDisabled == true)
        {
            return Found(null, ServerMessages.DirectoryAccountDisabled.With("name", known.UserName ?? ""));
        }

        if (!mapped.Decides)
        {
            string? held = known is null ? null : DdtRoleNames.Highest(await userManager.GetRolesAsync(known).ConfigureAwait(false));

            return Found(held, (known, held) switch
            {
                (null, _) => ServerMessages.DirectoryNoMapNewAccount.With(),
                (_, null) => ServerMessages.DirectoryNoMapNoRole.With(),
                _ => ServerMessages.DirectoryNoMapRole.With("role", RoleName(held)),
            });
        }

        if (mapped.Role is null)
        {
            return Found(null, ServerMessages.DirectoryNoMappedGroup.With("name", userName));
        }

        GroupRole best = mapped.Matches.First(match => match.Role == mapped.Role);
        string group = DistinguishedNames.FirstValue(best.Group);
        ServerMessage reason = mapped.Matches.Count == 1
            ? ServerMessages.DirectoryRoleFromGroup.With("name", userName, "role", RoleName(mapped.Role), "group", group)
            : ServerMessages.DirectoryRoleFromGroups.With("name", userName, "count", mapped.Matches.Count, "role", RoleName(mapped.Role), "group", group);

        return Found(
            mapped.Role,
            known is not null && await userManager.IsLockedOutAsync(known).ConfigureAwait(false)
                ? ServerMessages.DirectoryLockedOut.With("reason", reason)
                : reason);
    }

    // A role DDT knows as a message, so the web names it as its role pages do; any other as it is.
    private static object RoleName(string role) => role switch
    {
        DdtRoleNames.Administrator => ServerMessages.RoleAdministrator.With(),
        DdtRoleNames.Operator => ServerMessages.RoleOperator.With(),
        DdtRoleNames.Viewer => ServerMessages.RoleViewer.With(),
        _ => role,
    };

    private static DirectoryCheck NotFound(ServerMessage message) =>
        new(false, null, null, [], [], null, message.Text, message.Code, message.Args);

    // Keyed on the directory's immutable identifier, never on the user name or the distinguished
    // name: both of those change when someone is renamed or moved between organisational units.
    private async Task<DdtUser?> KnownAsync(string immutableId, DdtUser? matchedByName) =>
        await userManager.Users
            .FirstOrDefaultAsync(u => u.DirectoryObjectId == immutableId)
            .ConfigureAwait(false)
        ?? (matchedByName?.Source == AccountSource.Directory ? matchedByName : null);

    private async Task<(DdtUser? User, bool Created)> ReconcileAsync(LdapIdentity identity, DdtUser? matchedByName)
    {
        DdtUser? user = await KnownAsync(identity.ImmutableId, matchedByName).ConfigureAwait(false);

        if (user is null)
        {
            user = new DdtUser
            {
                UserName = identity.UserName,
                Email = identity.Email,
                DisplayName = identity.DisplayName,
                Source = AccountSource.Directory,
                DirectoryObjectId = identity.ImmutableId,
                CreatedUtc = DateTimeOffset.UtcNow,
            };

            if (!Saved(user, await userManager.CreateAsync(user).ConfigureAwait(false)))
            {
                return (null, false);
            }

            LogDirectoryUserCreated(user.UserName ?? string.Empty, identity.ImmutableId);

            return (user, true);
        }

        user.UserName = identity.UserName;
        user.Email = identity.Email;
        user.DisplayName = identity.DisplayName;
        user.DirectoryObjectId = identity.ImmutableId;

        return (Saved(user, await userManager.UpdateAsync(user).ConfigureAwait(false)) ? user : null, false);
    }

    private async Task<(bool Saved, bool Changed)> ApplyRoleAsync(DdtUser user, string? role)
    {
        (IdentityResult result, bool changed) = await activity.ApplyGroupRoleAsync(user, role).ConfigureAwait(false);

        return (Saved(user, result), changed);
    }

    private bool Saved(DdtUser user, IdentityResult result)
    {
        if (!result.Succeeded)
        {
            LogDirectoryUserNotSaved(user.UserName ?? string.Empty, string.Join(" ", result.Errors.Select(error => error.Description)));
        }

        return result.Succeeded;
    }

    [LoggerMessage(EventId = 400, Level = LogLevel.Information, Message = "Directory sign in for {UserName} ({ImmutableId})")]
    private partial void LogDirectorySignIn(string userName, string immutableId);

    [LoggerMessage(EventId = 401, Level = LogLevel.Information, Message = "Created directory backed account {UserName} ({ImmutableId})")]
    private partial void LogDirectoryUserCreated(string userName, string immutableId);

    [LoggerMessage(EventId = 402, Level = LogLevel.Warning, Message = "Could not save directory backed account {UserName}: {Errors}")]
    private partial void LogDirectoryUserNotSaved(string userName, string errors);

    [LoggerMessage(EventId = 403, Level = LogLevel.Warning, Message = "Directory sign in refused for {UserName} ({ImmutableId}): in none of the groups DDT:Ldap:GroupRoleMap maps to a role")]
    private partial void LogNoRole(string userName, string immutableId);
}
