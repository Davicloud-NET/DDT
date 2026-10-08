// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;

namespace DDT.Pxe;

// Kestrel connection middleware for the boot port: every connection is known to BootConnections while it is open.
public static class BootPortConnections
{
    public static ConnectionDelegate Track(ConnectionDelegate next, BootConnections connections)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(connections);

        return async context =>
        {
            if (context.RemoteEndPoint is not IPEndPoint remote)
            {
                await next(context).ConfigureAwait(false);

                return;
            }

            BootClient client = new(remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address);
            context.Features.Set(client);

            using (connections.Opened(client.Address, () => Reset(context)))
            {
                await next(context).ConfigureAwait(false);
            }
        };
    }

    // A linger time of zero makes the close a reset: the system forgets the connection at once instead of sending
    // what is left, so the machine's next connection from the same port meets nothing.
    private static void Reset(ConnectionContext context)
    {
        try
        {
            if (context.Features.Get<IConnectionSocketFeature>()?.Socket is { } socket)
            {
                socket.LingerState = new LingerOption(true, 0);
            }
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            // It closed by itself in this moment.
        }

        context.Abort(new ConnectionAbortedException("The machine starts from the network again."));
    }
}
