// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// Reads opened boot content at any offset, as a TFTP transfer does when it sends a block again.
public abstract class BootContentReader : IDisposable
{
    public abstract long Length { get; }

    // Fills the whole destination. Throws IOException when the content ends before that.
    public abstract ValueTask ReadAsync(Memory<byte> destination, long offset, CancellationToken cancellationToken);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
