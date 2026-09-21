// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DDT.Server.Ldap;

public sealed partial class DirectorySignInService(
    ILdapAuthenticator authenticator,
    UserManager<DdtUser> userManager,
    SignInManager<DdtUser> signInManager,
    IOptions<LdapOptions> options,
    ILogger<DirectorySignInService> logger)
{
    private readonly LdapOptions _options = options.Value;

    public bool Enabled => _options.Enabled;

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
    // on success.
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

        DdtUser? user = await ReconcileAsync(identity, existing).ConfigureAwait(false);

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
        if (!Saved(user, await userManager.ResetAccessFailedCountAsync(user).ConfigureAwait(false))
            || !await ApplyRolesAsync(user, identity).ConfigureAwait(false))
        {
            return (SignInResult.Failed, null);
        }

        LogDirectorySignIn(user.UserName ?? string.Empty, identity.ImmutableId);

        return (SignInResult.Success, user);
    }

    // Keyed on the directory's immutable identifier, never on the user name or the distinguished
    // name: both of those change when someone is renamed or moved between organisational units.
    private async Task<DdtUser?> ReconcileAsync(LdapIdentity identity, DdtUser? matchedByName)
    {
        DdtUser? user = await userManager.Users
            .FirstOrDefaultAsync(u => u.DirectoryObjectId == identity.ImmutableId)
            .ConfigureAwait(false)
            ?? matchedByName;

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
                return null;
            }

            LogDirectoryUserCreated(user.UserName ?? string.Empty, identity.ImmutableId);

            return user;
        }

        user.UserName = identity.UserName;
        user.Email = identity.Email;
        user.DisplayName = identity.DisplayName;
        user.DirectoryObjectId = identity.ImmutableId;
        user.LastSignInUtc = DateTimeOffset.UtcNow;

        return Saved(user, await userManager.UpdateAsync(user).ConfigureAwait(false)) ? user : null;
    }

    private async Task<bool> ApplyRolesAsync(DdtUser user, LdapIdentity identity)
    {
        if (_options.GroupRoleMap.Count == 0)
        {
            return true;
        }

        HashSet<string> mapped = new(StringComparer.OrdinalIgnoreCase);

        foreach (string groupDn in identity.GroupDns)
        {
            if (_options.GroupRoleMap.TryGetValue(groupDn, out string? role))
            {
                mapped.Add(role);
            }
        }

        IList<string> current = await userManager.GetRolesAsync(user).ConfigureAwait(false);

        // The directory is authoritative, so a role removed there is removed here on next sign in.
        string[] toRemove = [.. current.Where(role => !mapped.Contains(role))];
        string[] toAdd = [.. mapped.Where(role => !current.Contains(role, StringComparer.OrdinalIgnoreCase))];

        if (toRemove.Length > 0 && !Saved(user, await userManager.RemoveFromRolesAsync(user, toRemove).ConfigureAwait(false)))
        {
            return false;
        }

        return toAdd.Length == 0 || Saved(user, await userManager.AddToRolesAsync(user, toAdd).ConfigureAwait(false));
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
}
