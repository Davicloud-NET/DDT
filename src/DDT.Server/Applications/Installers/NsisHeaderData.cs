// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.IO.Compression;

namespace DDT.Server.Applications.Installers;

// The header of an NSIS installer, unpacked (fileform.h, fileform.c). It follows the firstheader, which sits at a
// multiple of 512 bytes. Without /SOLID it is a block of its own: a length, its top bit set when compressed, then the
// data. With /SOLID the data that follows is one compressed stream, and the header is its first block. The method is
// zlib's deflate without a zlib header, or LZMA; NSIS's own bzip2 lacks the standard framing, so it is not read.
internal static class NsisHeaderData
{
    public const int FirstHeaderLength = 28;
    public const int MaxHeaderLength = 16 * 1024 * 1024;

    private const int Alignment = 512;
    private const int SearchWindow = 1024 * 1024;
    private const uint Signature = 0xDEADBEEF;
    private const int FlagsMask = 15;
    private const uint CompressedFlag = 0x80000000;

    // The firstheader's offset, at or after the overlay, or null.
    public static long? FindFirstHeader(Stream file, long overlayOffset)
    {
        ArgumentNullException.ThrowIfNull(file);

        long start = overlayOffset / Alignment * Alignment;
        long end = Math.Min(file.Length - FirstHeaderLength, overlayOffset + SearchWindow);

        for (long at = start; at <= end; at += Alignment)
        {
            if (IsFirstHeader(file.ReadAtMost(at, FirstHeaderLength)))
            {
                return at;
            }
        }

        return null;
    }

    // The header's bytes, or null when they cannot be read or are not as long as the firstheader says.
    public static byte[]? Read(Stream file, long firstHeader)
    {
        ArgumentNullException.ThrowIfNull(file);

        byte[] first = file.ReadAtMost(firstHeader, FirstHeaderLength);

        if (!IsFirstHeader(first))
        {
            return null;
        }

        int headerLength = BinaryPrimitives.ReadInt32LittleEndian(first.AsSpan(20));
        long following = BinaryPrimitives.ReadUInt32LittleEndian(first.AsSpan(24));
        long data = firstHeader + FirstHeaderLength;
        long available = Math.Min(following, file.Length - firstHeader) - FirstHeaderLength;

        if (headerLength is <= 0 or > MaxHeaderLength || available < 4)
        {
            return null;
        }

        return Block(file, data, available, headerLength) ?? Solid(file, data, available, headerLength);
    }

    private static bool IsFirstHeader(ReadOnlySpan<byte> first) =>
        first.Length == FirstHeaderLength
        && (BinaryPrimitives.ReadInt32LittleEndian(first) & ~FlagsMask) == 0
        && BinaryPrimitives.ReadUInt32LittleEndian(first[4..]) == Signature
        && first.Slice(8, 4).SequenceEqual("Null"u8)
        && first.Slice(13, 7).SequenceEqual("oftInst"u8);

    private static byte[]? Block(Stream file, long data, long available, int headerLength)
    {
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(file.ReadAtMost(data, 4));
        long stored = length & ~CompressedFlag;

        if (stored <= 0 || stored > available - 4)
        {
            return null;
        }

        if ((length & CompressedFlag) == 0)
        {
            return stored == headerLength ? file.ReadAtMost(data + 4, headerLength) : null;
        }

        using Stream input = new FileWindowStream(file, data + 4, stored);

        return Unpack(input, headerLength);
    }

    // The first block of the stream is the header's length, then the header.
    private static byte[]? Solid(Stream file, long data, long available, int headerLength)
    {
        using Stream input = new FileWindowStream(file, data, available);
        byte[]? unpacked = Unpack(input, 4 + headerLength);

        return unpacked is not null && BinaryPrimitives.ReadUInt32LittleEndian(unpacked) == headerLength ? unpacked[4..] : null;
    }

    // LZMA when the first byte can be its properties, else deflate. Exactly length bytes or null.
    private static byte[]? Unpack(Stream input, int length)
    {
        long start = input.Position;
        int first = input.ReadByte();
        input.Position = start;

        if (first is >= 0 and <= 9 * 5 * 5 && TryUnpack(() => InstallerLzma.Open(input, length), length) is { } lzma)
        {
            return lzma;
        }

        input.Position = start;

        return TryUnpack(() => new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true), length);
    }

    private static byte[]? TryUnpack(Func<Stream?> open, int length)
    {
        try
        {
            using Stream? unpacking = open();

            if (unpacking is null)
            {
                return null;
            }

            byte[] buffer = new byte[length];

            return unpacking.ReadAtLeast(buffer, length, throwOnEndOfStream: false) == length ? buffer : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }
}
