// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// Bytes the server made for one request, such as grub.cfg. Name is what the log calls them.
public sealed class MemoryBootContent(string name, ReadOnlyMemory<byte> bytes) : BootContent
{
    public ReadOnlyMemory<byte> Bytes { get; } = bytes;

    public override string Description { get; } = "generated " + name;

    public override BootContentReader OpenRead() => new Reader(Bytes);

    private sealed class Reader(ReadOnlyMemory<byte> bytes) : BootContentReader
    {
        public override long Length => bytes.Length;

        public override ValueTask ReadAsync(Memory<byte> destination, long offset, CancellationToken cancellationToken)
        {
            if (offset < 0 || offset + destination.Length > bytes.Length)
            {
                throw new IOException("The generated boot file is shorter than the transfer asked for.");
            }

            bytes.Slice((int)offset, destination.Length).CopyTo(destination);

            return ValueTask.CompletedTask;
        }
    }
}
