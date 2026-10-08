// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Microsoft.Win32.SafeHandles;

namespace DDT.Pxe;

// A file of the boot directory, which a resolver found, or of the Linux boot environment's folder.
public sealed class FileBootContent(FileInfo file) : BootContent
{
    public FileInfo File { get; } = file ?? throw new ArgumentNullException(nameof(file));

    // What the file was when the server checked its content. It is served only while it is still that. Null for a file
    // nobody checked, which is served as it is.
    public BootFileStamp? Checked { get; init; }

    // Called when a checked file turns out gone or changed.
    public Action? Changed { get; init; }

    public override string Description => File.FullName;

    // FileShare.Delete: a new boot image can move this one aside while it's being sent. Windows refuses otherwise.
    public override BootContentReader OpenRead()
    {
        SafeFileHandle handle;

        try
        {
            handle = System.IO.File.OpenHandle(File.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Unreadable();

            throw;
        }

        if (!Fits(handle))
        {
            handle.Dispose();

            throw new IOException($"{File.Name} is no longer the file the server checked.");
        }

        return new Reader(handle);
    }

    // Whether the opened file is still the one that was checked, and no link to another. Says so when it is not.
    public bool Fits(SafeFileHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (Checked is not { } expected)
        {
            return true;
        }

        bool fits = RandomAccess.GetLength(handle) == expected.Length
            && System.IO.File.GetLastWriteTimeUtc(handle) == expected.WriteTimeUtc
            && !System.IO.File.GetAttributes(File.FullName).HasFlag(FileAttributes.ReparsePoint);

        if (!fits)
        {
            Changed?.Invoke();
        }

        return fits;
    }

    // A checked file that cannot be opened is as good as gone.
    public void Unreadable()
    {
        if (Checked is not null)
        {
            Changed?.Invoke();
        }
    }

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
