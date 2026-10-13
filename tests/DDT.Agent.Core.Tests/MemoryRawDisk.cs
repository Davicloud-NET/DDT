// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// A disk in memory that holds only the pages written to it, so a 256 GiB disk costs only as much as its image. Every
// write is recorded in order, which shows that the partition table was written last.
internal sealed class MemoryRawDisk(long length, int sectorSize = 512) : IRawDisk
{
    private const int PageBytes = 1024 * 1024;

    private readonly Dictionary<long, byte[]> _pages = [];

    public int SectorSize => sectorSize;

    public long Length => length;

    public List<(long Offset, int Length)> Writes { get; } = [];

    public int Flushes { get; private set; }

    public int PropertyUpdates { get; private set; }

    public bool Disposed { get; private set; }

    // The next write keeps this many of its bytes and then fails, as on a disk that stops answering half way.
    public int? FailNextWriteAfter { get; set; }

    // A read that reaches this byte or one after it fails, as on a disk whose sectors there no longer answer.
    public long? UnreadableFrom { get; set; }

    public void Read(long offset, Span<byte> buffer)
    {
        CheckWhole(offset, buffer.Length);

        if (UnreadableFrom is { } from && offset + buffer.Length > from)
        {
            throw new DeploymentStepException($"The disk cannot be read at byte {Math.Max(offset, from)} (Input/output error).");
        }

        for (int done = 0; done < buffer.Length;)
        {
            long at = offset + done;
            int inPage = (int)(at % PageBytes);
            int count = Math.Min(buffer.Length - done, PageBytes - inPage);

            if (_pages.TryGetValue(at / PageBytes, out byte[]? page))
            {
                page.AsSpan(inPage, count).CopyTo(buffer[done..]);
            }
            else
            {
                buffer.Slice(done, count).Clear();
            }

            done += count;
        }
    }

    public void Write(long offset, ReadOnlySpan<byte> buffer)
    {
        CheckWhole(offset, buffer.Length);
        Writes.Add((offset, buffer.Length));

        if (FailNextWriteAfter is { } kept)
        {
            FailNextWriteAfter = null;
            Keep(offset, buffer[..Math.Min(kept, buffer.Length)]);

            throw new IOException("The device is not ready.");
        }

        Keep(offset, buffer);
    }

    private void Keep(long offset, ReadOnlySpan<byte> buffer)
    {
        for (int done = 0; done < buffer.Length;)
        {
            long at = offset + done;
            int inPage = (int)(at % PageBytes);
            int count = Math.Min(buffer.Length - done, PageBytes - inPage);

            if (!_pages.TryGetValue(at / PageBytes, out byte[]? page))
            {
                page = new byte[PageBytes];
                _pages[at / PageBytes] = page;
            }

            buffer.Slice(done, count).CopyTo(page.AsSpan(inPage));
            done += count;
        }
    }

    public byte[] ReadAt(long offset, int count)
    {
        byte[] bytes = new byte[count];
        Read(offset, bytes);

        return bytes;
    }

    public void Flush() => Flushes++;

    public void UpdateProperties() => PropertyUpdates++;

    public void Dispose() => Disposed = true;

    private void CheckWhole(long offset, int count)
    {
        if (offset % sectorSize != 0 || count % sectorSize != 0 || offset < 0 || offset + count > length)
        {
            throw new ArgumentException($"{count} bytes at {offset} are not whole sectors of {sectorSize} bytes within {length}.");
        }
    }
}
