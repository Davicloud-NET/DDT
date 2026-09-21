// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace DDT.Protocols.Dhcp;

// Appends length prefixed options into a caller supplied buffer, accumulating an overflow flag
// rather than returning a result from every call, so a reply is written as a flat run of lines
// with one check at the end.
public ref struct DhcpOptionWriter(Span<byte> destination)
{
    private readonly Span<byte> _destination = destination;
    private int _written;
    private bool _overflowed;

    public readonly int BytesWritten => _written;

    public readonly bool Overflowed => _overflowed;

    public void WriteByte(byte code, byte value) => Write(code, [value]);

    public void WriteUInt16(byte code, ushort value)
    {
        Span<byte> scratch = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(scratch, value);
        Write(code, scratch);
    }

    public void WriteAddress(byte code, IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        Span<byte> scratch = stackalloc byte[4];

        if (!address.TryWriteBytes(scratch, out int length) || length != 4)
        {
            _overflowed = true;
            return;
        }

        Write(code, scratch);
    }

    public void WriteAscii(byte code, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        Span<byte> scratch = stackalloc byte[255];

        if (Ascii.FromUtf16(value, scratch, out int length) != OperationStatus.Done)
        {
            _overflowed = true;
            return;
        }

        Write(code, scratch[..length]);
    }

    public void Write(byte code, scoped ReadOnlySpan<byte> value)
    {
        if (value.Length > 255 || _written + 2 + value.Length > _destination.Length)
        {
            _overflowed = true;
            return;
        }

        _destination[_written] = code;
        _destination[_written + 1] = (byte)value.Length;
        value.CopyTo(_destination[(_written + 2)..]);
        _written += 2 + value.Length;
    }

    public void WriteEnd()
    {
        if (_written >= _destination.Length)
        {
            _overflowed = true;
            return;
        }

        _destination[_written] = DhcpOption.End;
        _written++;
    }
}
