namespace DDT.Protocols.Dhcp;

// Walks a DHCP option block. Never reads past the end of the span it was given, because the
// datagrams it is fed come from anyone on the segment.
public ref struct DhcpOptionReader
{
    private ReadOnlySpan<byte> _remaining;

    public DhcpOptionReader(ReadOnlySpan<byte> options)
    {
        _remaining = options;
        Code = 0;
        Value = default;
        Truncated = false;
    }

    public byte Code { get; private set; }

    public ReadOnlySpan<byte> Value { get; private set; }

    // True when the block ended inside a length prefixed option rather than at End or the buffer
    // edge. Without this the parser cannot tell "options ended" from "declared length 200 with two
    // bytes left" and would hand a half parsed message to the responder.
    public bool Truncated { get; private set; }

    public bool MoveNext()
    {
        while (!_remaining.IsEmpty)
        {
            byte code = _remaining[0];

            if (code == DhcpOption.Pad)
            {
                _remaining = _remaining[1..];
                continue;
            }

            if (code == DhcpOption.End)
            {
                _remaining = default;
                return false;
            }

            if (_remaining.Length < 2 || _remaining.Length < 2 + _remaining[1])
            {
                _remaining = default;
                Truncated = true;
                return false;
            }

            int length = _remaining[1];
            Code = code;
            Value = _remaining.Slice(2, length);
            _remaining = _remaining[(2 + length)..];

            return true;
        }

        return false;
    }
}
