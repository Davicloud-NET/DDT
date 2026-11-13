// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using DDT.Core;

namespace DDT.Server.Applications.Installers;

// The bytes of an Inno Setup block (TCompressedBlockReader): chunks of up to 4096 bytes, each after the CRC32 of its
// own. A chunk whose CRC does not match throws InvalidDataException before any of it is read.
internal sealed class InnoBlockStream(Stream file, long start, long storedSize) : Stream
{
    private const int ChunkLength = 4096;

    private readonly byte[] _chunk = new byte[ChunkLength];
    private long _position = start;
    private long _left = storedSize;
    private int _next;
    private int _available;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int read = 0;

        while (read < buffer.Length)
        {
            if (_available == 0 && !NextChunk())
            {
                break;
            }

            int count = Math.Min(buffer.Length - read, _available);
            _chunk.AsSpan(_next, count).CopyTo(buffer[read..]);
            _next += count;
            _available -= count;
            read += count;
        }

        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private bool NextChunk()
    {
        if (_left == 0)
        {
            return false;
        }

        if (_left < 5)
        {
            throw new InvalidDataException("An Inno Setup block ends inside a chunk.");
        }

        Span<byte> crc = stackalloc byte[4];
        file.ReadExactlyAt(_position, crc);
        int length = (int)Math.Min(_left - 4, ChunkLength);
        file.ReadExactlyAt(_position + 4, _chunk.AsSpan(0, length));

        if (Crc32.Append(0, _chunk.AsSpan(0, length)) != BinaryPrimitives.ReadUInt32LittleEndian(crc))
        {
            throw new InvalidDataException("A chunk of an Inno Setup block has the wrong CRC.");
        }

        _position += 4 + length;
        _left -= 4 + length;
        _next = 0;
        _available = length;

        return true;
    }
}
