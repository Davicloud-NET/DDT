using System.Text;

namespace DDT.Protocols.Tftp;

// TFTP request fields are NUL terminated ASCII run together with no length prefix, so a reader that
// stops at the end of the datagram is the only thing standing between a short packet and a read
// past the buffer.
public ref struct TftpStringReader(ReadOnlySpan<byte> source)
{
    private ReadOnlySpan<byte> _remaining = source;

    public readonly bool IsEmpty => _remaining.IsEmpty;

    public bool TryRead(out string value)
    {
        int terminator = _remaining.IndexOf((byte)0);

        if (terminator < 0)
        {
            value = string.Empty;
            _remaining = default;

            return false;
        }

        value = Encoding.ASCII.GetString(_remaining[..terminator]);
        _remaining = _remaining[(terminator + 1)..];

        return true;
    }
}
