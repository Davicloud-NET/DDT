// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Data;
using DDT.Server.Live;
using Microsoft.AspNetCore.Identity;

namespace DDT.Server.Users;

// What an account's own sign-in changes about it reaches the Users page too, with the account as it is now.
public sealed class UserActivity(
    UserManager<DdtUser> userManager,
    UserViews views,
    LiveNotifier live,
    TimeProvider timeProvider)
{
    // The last sign-in is that of a web session. A sign-in at a machine is kept with the machine and in the audit table.
    // Only a date, so a save that fails leaves the sign-in standing.
    public async Task SignedInAsync(DdtUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        user.LastSignInUtc = timeProvider.GetUtcNow();

        if ((await userManager.UpdateAsync(user).ConfigureAwait(false)).Succeeded)
        {
            await ChangedAsync(user, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task ChangedAsync(DdtUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        live.UserChanged(await views.ViewAsync(user, cancellationToken).ConfigureAwait(false));
    }
}
