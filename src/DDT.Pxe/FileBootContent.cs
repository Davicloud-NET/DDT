// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Win32.SafeHandles;

namespace DDT.Pxe;

// A file of the boot directory or of the Linux boot environment's folder, which a resolver found.
public sealed class FileBootContent(FileInfo file) : BootContent
{
    public FileInfo File { get; } = file ?? throw new ArgumentNullException(nameof(file));

    public override string Description => File.FullName;

    // FileShare.Delete: a new boot image can move this one aside while it's being sent. Windows refuses otherwise.
    public override BootContentReader OpenRead() =>
        new Reader(System.IO.File.OpenHandle(File.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete));

    private sealed class Reader(SafeFileHandle handle) : BootContentReader
    {
        public override long Length { get; } = RandomAccess.GetLength(handle);

        // RandomAccess may return fewer bytes than asked for, and a short DATA block ends a TFTP transfer.
        public override async ValueTask ReadAsync(Memory<byte> destination, long offset, CancellationToken cancellationToken)
        {
            int total = 0;

            while (total < destination.Length)
            {
                int read = await RandomAccess.ReadAsync(handle, destination[total..], offset + total, cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    throw new IOException("The boot file became shorter during the transfer.");
                }

                total += read;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                handle.Dispose();
            }
        }
    }
}
