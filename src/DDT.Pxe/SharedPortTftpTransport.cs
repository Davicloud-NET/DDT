// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace DDT.Pxe;

// Single port mode: replies leave from port 69, which a stateful firewall on a site tunnel let the request through to.
// The kernel no longer filters by client, so the listener hands this transfer its client's datagrams.
internal sealed class SharedPortTftpTransport : ITftpTransport
{
    // A client sends one acknowledgement per window, so a short queue is plenty. A datagram that does
    // not fit is dropped, which TFTP already tolerates as loss.
    private const int QueueLength = 16;

    private readonly Socket _socket;
    private readonly IPEndPoint _client;
    private readonly Channel<byte[]> _inbound = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(QueueLength)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = true,
        });

    public SharedPortTftpTransport(Socket socket, IPEndPoint client)
    {
        _socket = socket;
        _client = client;
    }

    public void Deliver(ReadOnlySpan<byte> datagram) => _inbound.Writer.TryWrite(datagram.ToArray());

    public async ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken) =>
        await _socket.SendToAsync(datagram, SocketFlags.None, _client, cancellationToken).ConfigureAwait(false);

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        byte[] datagram = await _inbound.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        int length = Math.Min(datagram.Length, buffer.Length);

        datagram.AsMemory(0, length).CopyTo(buffer);

        return length;
    }

    // The socket belongs to the listener.
    public void Dispose() => _inbound.Writer.TryComplete();
}
