// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers;
using System.Security.Cryptography;
using DDT.Core.Disks;
using ZstdSharp;

namespace DDT.Agent.Deployment;

// A raw disk image compressed with zstd, unpacked as it downloads and written onto the disk by a RawDiskWriter. It keeps
// nothing when the agent stops, which fails the step as interrupted, so it holds nothing from an earlier attempt.
public sealed class RawImageSink(RawDiskWriter writer) : IDownloadSink, IDisposable
{
    public const string DamagedMessage = "The image's zstd data is damaged. Upload the image again.";

    private const int OutputBytes = 1024 * 1024;

    private readonly Decompressor _zstd = new();
    private readonly byte[] _output = new byte[OutputBytes];
    private OperationStatus _status = OperationStatus.NeedMoreData;

    public long Length { get; private set; }

    public Task HashExistingAsync(IncrementalHash hash, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> input = data.Span;

        // A full output buffer may hold back more of the frame, so it is emptied until zstd has nothing left.
        do
        {
            _status = _zstd.UnwrapStream(input, _output, out int consumed, out int written);

            if (_status == OperationStatus.InvalidData)
            {
                throw new DeploymentStepException(DamagedMessage);
            }

            writer.Write(_output.AsSpan(0, written));
            input = input[consumed..];
        }
        while (!input.IsEmpty || _status == OperationStatus.DestinationTooSmall);

        Length += data.Length;

        return Task.CompletedTask;
    }

    public void Restart()
    {
        _zstd.ResetStream();
        writer.Restart();
        Length = 0;
        _status = OperationStatus.NeedMoreData;
    }

    // Once every byte arrived and matched: the rest of the disk and its partition table.
    public GptLayout Finish() =>
        _status == OperationStatus.Done
            ? writer.Finish()
            : throw new DeploymentStepException("The image's zstd data ends before its last frame does. Upload the image again.");

    public void Dispose() => _zstd.Dispose();
}
