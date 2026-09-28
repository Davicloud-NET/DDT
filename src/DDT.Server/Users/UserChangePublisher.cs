// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Users;
using DDT.Server.Data;
using DDT.Server.Live;

namespace DDT.Server.Users;

// Pushes an account as it is now to the administrators' pages. Closing its live connections first makes them connect
// again with what the account may see now.
internal sealed class UserChangePublisher(UserViews views, LiveNotifier live, LiveConnections connections)
{
    public async Task<UserView> ChangedAsync(DdtUser user, bool closeConnections, CancellationToken cancellationToken)
    {
        if (closeConnections)
        {
            connections.Close(user.Id);
        }

        UserView view = await views.ViewAsync(user, cancellationToken).ConfigureAwait(false);
        live.UserChanged(view);

        return view;
    }

    public void Removed(Guid userId)
    {
        connections.Close(userId);
        live.UsersRemoved([userId]);
    }
}
