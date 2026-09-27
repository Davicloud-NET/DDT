// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Users;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Live;
using DDT.Server.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Ldap;

public sealed partial class DirectorySignInService(
    ILdapAuthenticator authenticator,
    UserManager<DdtUser> userManager,
    SignInManager<DdtUser> signInManager,
    UserActivity activity,
    LiveConnections connections,
    IOptions<LdapOptions> options,
    ILogger<DirectorySignInService> logger)
{
    private readonly LdapOptions _options = options.Value;

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
                return new DirectoryCheck(false, null, null, [], [], null,
                    $"No entry under {_options.BaseDn} matches {userName} through DDT:Ldap:UserFilter, so a sign-in with it is refused.");
            case LdapLookupStatus.Ambiguous:
                return new DirectoryCheck(false, null, null, [], [], null,
                    $"More than one entry under {_options.BaseDn} matches {userName} through DDT:Ldap:UserFilter, so a sign-in with it is refused.");
        }

        GroupRoles mapped = GroupRoles.From(lookup.GroupDns, _options.GroupRoleMap);
        DirectoryCheck Found(string? role, string message) => new(
            true,
            lookup.DistinguishedName,
            lookup.DisplayName,
            lookup.GroupDns,
            [.. mapped.Matches.Select(match => new DirectoryGroupMatch(match.Group, match.Role))],
            role,
            message);

        DdtUser? account = await userManager.FindByNameAsync(userName).ConfigureAwait(false);

        if (account?.Source == AccountSource.Local)
        {
            return Found(null, $"{userName} is a local account in DDT, so a sign-in with this name checks its DDT password and never asks the directory.");
        }

        if (account?.Source == AccountSource.External)
        {
            return Found(null, $"{userName} is a single sign-on account in DDT, so a sign-in with this name and a password is refused.");
        }

        if (lookup.ImmutableId is null)
        {
            return Found(null, $"The entry has no {_options.ImmutableIdAttribute} value to key the account on, so a sign-in is refused.");
        }

        DdtUser? known = await KnownAsync(lookup.ImmutableId, account).ConfigureAwait(false);

        if (known?.IsDisabled == true)
        {
            return Found(null, $"The DDT account {known.UserName} is disabled, so a sign-in is refused.");
        }

        if (!mapped.Decides)
        {
            string? held = known is null ? null : DdtRoleNames.Highest(await userManager.GetRolesAsync(known).ConfigureAwait(false));

            return Found(held, (known, held) switch
            {
                (null, _) => "DDT:Ldap:GroupRoleMap is empty, so administrators set roles. A first sign-in makes an account without one, which reaches nothing until an administrator gives it a role.",
                (_, null) => "DDT:Ldap:GroupRoleMap is empty, so administrators set roles. The account has none yet, so it reaches nothing.",
                _ => $"DDT:Ldap:GroupRoleMap is empty, so administrators set roles. The account has {held}.",
            });
        }

        if (mapped.Role is null)
        {
            return Found(null, $"{userName} is in none of the groups DDT:Ldap:GroupRoleMap maps to a role, so a sign-in is refused.");
        }

        GroupRole best = mapped.Matches.First(match => match.Role == mapped.Role);
        string reason = mapped.Matches.Count == 1
            ? $"{userName} gets {mapped.Role} from {DistinguishedNames.FirstValue(best.Group)}."
            : $"{userName} is in {mapped.Matches.Count} mapped groups and gets the highest role they give, {mapped.Role} from {DistinguishedNames.FirstValue(best.Group)}.";

        return Found(
            mapped.Role,
            known is not null && await userManager.IsLockedOutAsync(known).ConfigureAwait(false)
                ? $"{reason} The account is locked out for now, so a sign-in waits until the lockout ends."
                : reason);
    }

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

    // The one role the groups give, or none. A change closes the account's live connections, which connect again with
    // the role it holds now.
    private async Task<(bool Saved, bool Changed)> ApplyRoleAsync(DdtUser user, string? role)
    {
        IList<string> current = await userManager.GetRolesAsync(user).ConfigureAwait(false);
        string[] toRemove = [.. current.Where(held => !string.Equals(held, role, StringComparison.OrdinalIgnoreCase))];
        bool toAdd = role is not null && !current.Contains(role, StringComparer.OrdinalIgnoreCase);

        if (toRemove.Length == 0 && !toAdd)
        {
            return (true, false);
        }

        bool saved = (toRemove.Length == 0 || Saved(user, await userManager.RemoveFromRolesAsync(user, toRemove).ConfigureAwait(false)))
            && (!toAdd || Saved(user, await userManager.AddToRoleAsync(user, role!).ConfigureAwait(false)));

        connections.Close(user.Id);

        return (saved, true);
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
