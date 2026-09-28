// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Protocols.Tftp;

// Request fields are NUL terminated ASCII without a length prefix, so stopping at the datagram's end is all that keeps
// a short packet from a read past the buffer.
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
