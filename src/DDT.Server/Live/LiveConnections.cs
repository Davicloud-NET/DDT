// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace DDT.Server.Live;

// The open live connections of every account. A connection is only authorized when it opens. Closing them makes the
// browser reconnect, so the hub checks the account as it is now, for example after a demotion.
public sealed class LiveConnections
{
    private readonly ConcurrentDictionary<string, (Guid UserId, HubCallerContext Context)> _open = new(StringComparer.Ordinal);

    public void Opened(Guid userId, HubCallerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _open[context.ConnectionId] = (userId, context);
    }

    public void Closed(string connectionId) => _open.TryRemove(connectionId, out _);

    public void Close(Guid userId)
    {
        foreach ((string connectionId, (Guid owner, HubCallerContext context)) in _open)
        {
            if (owner == userId && _open.TryRemove(connectionId, out _))
            {
                context.Abort();
            }
        }
    }
}
