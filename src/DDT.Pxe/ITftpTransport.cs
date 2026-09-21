// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// How one transfer exchanges datagrams with its client: over its own connected socket, or over the
// listener's port 69 socket when single port mode is on.
internal interface ITftpTransport : IDisposable
{
    ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken);

    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);
}
