// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using DDT.Core.Disks;
using Microsoft.Extensions.Logging;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace DDT.Server.Images;

// Turns an uploaded disk image into what the library stores: the raw disk, inspected, then compressed with zstd into a
// file the caller names by its SHA-256. DDT reads raw, gzip and zstd itself, and converts xz and qcow2 with the
// ConversionTools. The raw disk and the compressed copy sit next to the upload's part file while this runs, and an
// import that stops leaves them for the next attempt or the sweeper.
public sealed partial class RawImageImporter(ImageStore store, ConversionTools tools, ILogger<RawImageImporter> logger)
{
    public const string NotAnImageMessage =
        "This file is neither a WIM image nor a disk image with a GUID partition table. Upload a WIM or ESD file, or a disk " +
        "image such as a distribution's cloud image.";

    public const string OutOfSpaceMessage =
        "The store volume ran out of space while the image was converted. Free some space and complete the upload again.";

    private const int BufferBytes = 4 * 1024 * 1024;
    private const int HeadBytes = 512;

    public static DiskImageFormat Sniff(ReadOnlySpan<byte> head)
    {
        // "QFI" and 0xFB.
        if (head.StartsWith((ReadOnlySpan<byte>)[0x51, 0x46, 0x49, 0xFB]))
        {
            return DiskImageFormat.Qcow2;
        }

        if (head.StartsWith((ReadOnlySpan<byte>)[0x1F, 0x8B]))
        {
            return DiskImageFormat.Gzip;
        }

        if (head.StartsWith((ReadOnlySpan<byte>)[0x28, 0xB5, 0x2F, 0xFD]))
        {
            return DiskImageFormat.Zstd;
        }

        if (head.StartsWith((ReadOnlySpan<byte>)[0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00]))
        {
            return DiskImageFormat.Xz;
        }

        if (head.StartsWith("vhdxfile"u8))
        {
            return DiskImageFormat.Vhdx;
        }

        if (head.StartsWith("KDMV"u8) || head.StartsWith("# Disk DescriptorFile"u8))
        {
            return DiskImageFormat.Vmdk;
        }

        return head.StartsWith("<<< "u8) && head.IndexOf("VirtualBox"u8) is >= 0 and < 64 ? DiskImageFormat.Vdi : DiskImageFormat.Raw;
    }

    public async Task<RawImport> ImportAsync(Guid uploadId, CancellationToken cancellationToken)
    {
        string part = store.PartPath(uploadId);
        string raw = store.RawPath(uploadId);
        string compressed = store.CompressedPath(uploadId);
        bool imported = false;

        File.Delete(raw);
        File.Delete(compressed);

        try
        {
            byte[] head = await ReadHeadAsync(part, cancellationToken).ConfigureAwait(false);
            DiskImageFormat format = Sniff(head);

            if (Unsupported(format) is { } unsupported)
            {
                return new RawImport(unsupported);
            }

            string source = part;

            if (format != DiskImageFormat.Raw)
            {
                if (await ExpandAsync(uploadId, format, head, part, raw, cancellationToken).ConfigureAwait(false) is { } refusal)
                {
                    return new RawImport(refusal);
                }

                source = raw;
            }

            RawImageInfo info;

            await using (FileStream disk = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, FileOptions.None))
            {
                try
                {
                    info = RawImageInspector.Inspect(disk);
                }
                catch (InvalidGptException exception)
                {
                    return new RawImport(
                        format == DiskImageFormat.Raw && exception.Message == GptLayout.NoTableMessage ? NotAnImageMessage : exception.Message);
                }
            }

            long started = Stopwatch.GetTimestamp();
            (string sourceSha256, string sha256, long size) = await CompressAsync(source, compressed, cancellationToken).ConfigureAwait(false);
            LogCompressed(uploadId, info.SizeBytes, size, Stopwatch.GetElapsedTime(started).TotalSeconds);
            imported = true;

            return new RawImport(null, compressed, sha256, size, sourceSha256, info);
        }
        catch (Exception exception) when (IsDiskFull(exception))
        {
            return new RawImport(OutOfSpaceMessage);
        }
        catch (ConversionFailedException exception)
        {
            return new RawImport($"The image could not be converted. {exception.Message}");
        }
        catch (Exception exception) when (exception is InvalidDataException or ZstdException or EndOfStreamException)
        {
            return new RawImport($"The compressed file is damaged: {exception.Message}");
        }
        finally
        {
            File.Delete(raw);

            if (!imported)
            {
                File.Delete(compressed);
            }
        }
    }

    private static string? Unsupported(DiskImageFormat format) => format switch
    {
        DiskImageFormat.Vhdx or DiskImageFormat.Vmdk or DiskImageFormat.Vdi =>
            $"This is a {format.ToString().ToUpperInvariant()} disk image, which DDT does not read. Convert it with " +
            "qemu-img convert -O raw <file> disk.raw and upload disk.raw, or upload the distribution's raw or qcow2 image.",
        _ => null,
    };

    // Writes the raw disk to raw, or says why it cannot.
    private async Task<string?> ExpandAsync(
        Guid uploadId,
        DiskImageFormat format,
        byte[] head,
        string part,
        string raw,
        CancellationToken cancellationToken)
    {
        switch (format)
        {
            case DiskImageFormat.Gzip:
            case DiskImageFormat.Zstd:
                await using (FileStream input = new(part, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, useAsync: true))
                await using (Stream expanded = format == DiskImageFormat.Gzip
                    ? new GZipStream(input, CompressionMode.Decompress)
                    : new DecompressionStream(input, BufferBytes))
                await using (FileStream output = new(raw, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, useAsync: true))
                {
                    await expanded.CopyToAsync(output, BufferBytes, cancellationToken).ConfigureAwait(false);
                }

                return null;

            case DiskImageFormat.Xz:
                if (tools.Find(ConversionTools.Xz) is not { } xz)
                {
                    return "This file is compressed with xz, which is not installed on the server. Install xz there, or unpack " +
                        "the file with xz -d and upload the disk image it holds.";
                }

                LogConverting(uploadId, format, xz);
                await tools.RunAsync(xz, ["--decompress", "--stdout", "--", part], raw, cancellationToken).ConfigureAwait(false);

                return null;

            case DiskImageFormat.Qcow2:
                if (Qcow2Problem(head) is { } problem)
                {
                    return problem;
                }

                if (tools.Find(ConversionTools.QemuImg) is not { } qemuImg)
                {
                    return "This is a qcow2 image, and qemu-img is not installed on the server. Install qemu-img there, or " +
                        "convert the file with qemu-img convert -O raw <file> disk.raw and upload disk.raw.";
                }

                LogConverting(uploadId, format, qemuImg);
                await tools.RunAsync(qemuImg, ["convert", "-f", "qcow2", "-O", "raw", part, raw], null, cancellationToken)
                    .ConfigureAwait(false);

                return null;

            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, null);
        }
    }

    // qemu-img would read a backing file or an external data file from the server's own disk, wherever the upload's
    // header points, into the image.
    private static string? Qcow2Problem(ReadOnlySpan<byte> head)
    {
        if (head.Length < 104)
        {
            return "The qcow2 image is too short to be one.";
        }

        uint version = BinaryPrimitives.ReadUInt32BigEndian(head[4..]);
        ulong backingFile = BinaryPrimitives.ReadUInt64BigEndian(head[8..]);
        uint encryption = BinaryPrimitives.ReadUInt32BigEndian(head[32..]);
        ulong incompatible = version >= 3 ? BinaryPrimitives.ReadUInt64BigEndian(head[72..]) : 0;

        if (backingFile != 0)
        {
            return "The qcow2 image depends on a backing file. Make a standalone image with qemu-img convert -O qcow2 <file> " +
                "standalone.qcow2 and upload that.";
        }

        if (encryption != 0)
        {
            return "The qcow2 image is encrypted. Upload an image that is not.";
        }

        // Bit 2: the data lives in an external file.
        return (incompatible & 0x4) != 0
            ? "The qcow2 image keeps its data in an external file. Convert it with qemu-img convert -O raw <file> disk.raw and upload disk.raw."
            : null;
    }

    // One pass: the raw disk is hashed as it is read, and the compressed copy as it is written.
    private static async Task<(string SourceSha256, string Sha256, long Size)> CompressAsync(
        string source,
        string compressed,
        CancellationToken cancellationToken)
    {
        await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream output = new(compressed, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, useAsync: true);
        await using HashingWriteStream hashed = new(output);
        using IncrementalHash sourceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferBytes);

        try
        {
            await using (CompressionStream zstd = new(hashed, RawImageLimits.CompressionLevel, leaveOpen: true))
            {
                zstd.SetParameter(ZSTD_cParameter.ZSTD_c_nbWorkers, RawImageLimits.CompressionWorkers);

                // The agent's decompressor then checks the content as it unpacks it, besides the file's SHA-256.
                zstd.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
                int read;

                while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferBytes), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    sourceHash.AppendData(buffer, 0, read);
                    await zstd.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            await hashed.FlushAsync(cancellationToken).ConfigureAwait(false);

            return (Convert.ToHexStringLower(sourceHash.GetHashAndReset()), hashed.Sha256(), hashed.Written);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<byte[]> ReadHeadAsync(string part, CancellationToken cancellationToken)
    {
        await using FileStream file = new(part, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 0, useAsync: true);
        byte[] head = new byte[HeadBytes];
        int read = await file.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

        return head[..read];
    }

    // ERROR_HANDLE_DISK_FULL and ERROR_DISK_FULL on Windows, ENOSPC elsewhere.
    private static bool IsDiskFull(Exception exception) =>
        exception is IOException io && ((io.HResult & 0xFFFF) is 0x27 or 0x70 || io.HResult == 28);

    [LoggerMessage(EventId = 915, Level = LogLevel.Information, Message = "Converting upload {UploadId} from {Format} with {Tool}")]
    private partial void LogConverting(Guid uploadId, DiskImageFormat format, string tool);

    [LoggerMessage(
        EventId = 916,
        Level = LogLevel.Information,
        Message = "Compressed the disk of upload {UploadId}, {RawBytes} bytes, to {CompressedBytes} bytes in {Seconds:0.0} s")]
    private partial void LogCompressed(Guid uploadId, long rawBytes, long compressedBytes, double seconds);
}
