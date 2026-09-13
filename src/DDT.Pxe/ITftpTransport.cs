namespace DDT.Pxe;

// How one transfer exchanges datagrams with its client: over its own connected socket, or over the
// listener's port 69 socket when single port mode is on.
internal interface ITftpTransport : IDisposable
{
    ValueTask SendAsync(ReadOnlyMemory<byte> datagram, CancellationToken cancellationToken);

    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);
}
