// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;

namespace DDT.Pxe;

// Kestrel connection middleware for the boot port: BootConnections learns of a connection once it carries Linux content.
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

            IPAddress address = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;

            using BootClient client = new(address, () => connections.Opened(address, () => Reset(context)));
            context.Features.Set(client);
            await next(context).ConfigureAwait(false);
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
