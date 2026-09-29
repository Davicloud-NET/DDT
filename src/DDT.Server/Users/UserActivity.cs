// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Live;
using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Users;

// When an account's own sign-in changes something about it, the Users page gets the account's current state too.
public sealed class UserActivity(
    UserManager<DdtUser> userManager,
    UserViews views,
    LiveNotifier live,
    LiveConnections connections,
    TimeProvider timeProvider)
{
    // Records the last web sign-in. A sign-in at a machine is kept with the machine and in the audit table. It's only a
    // date, so a failed save doesn't undo the sign-in.
    public async Task SignedInAsync(DdtUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        user.LastSignInUtc = timeProvider.GetUtcNow();

        if ((await userManager.UpdateAsync(user).ConfigureAwait(false)).Succeeded)
        {
            await ChangedAsync(user, cancellationToken).ConfigureAwait(false);
        }
    }

    // Sets the one role the account's groups give it, or none, based on what the directory or provider says at sign-in.
    // A change closes the account's live connections, and they reconnect with the new role. The caller updates the
    // Users page once for the whole sign-in.
    public async Task<(IdentityResult Result, bool Changed)> ApplyGroupRoleAsync(DdtUser user, string? role)
    {
        ArgumentNullException.ThrowIfNull(user);

        IList<string> current = await userManager.GetRolesAsync(user).ConfigureAwait(false);
        string[] toRemove = [.. current.Where(held => !string.Equals(held, role, StringComparison.OrdinalIgnoreCase))];
        bool toAdd = role is not null && !current.Contains(role, StringComparer.OrdinalIgnoreCase);

        if (toRemove.Length == 0 && !toAdd)
        {
            return (IdentityResult.Success, false);
        }

        IdentityResult result = toRemove.Length > 0
            ? await userManager.RemoveFromRolesAsync(user, toRemove).ConfigureAwait(false)
            : IdentityResult.Success;

        if (result.Succeeded && toAdd)
        {
            result = await userManager.AddToRoleAsync(user, role!).ConfigureAwait(false);
        }

        connections.Close(user.Id);

        return (result, true);
    }

    public async Task ChangedAsync(DdtUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        live.UserChanged(await views.ViewAsync(user, cancellationToken).ConfigureAwait(false));
    }
}
