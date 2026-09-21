// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net.Sockets;

namespace DDT.Pxe;

internal sealed class ConnectedTftpTransport(Socket socket) : ITftpTransport
{
    public async ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken) =>
        await socket.SendAsync(datagram, SocketFlags.None, cancellationToken).ConfigureAwait(false);

    public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);

    public void Dispose() => socket.Dispose();
}
