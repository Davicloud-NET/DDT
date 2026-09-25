// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// A whole disk, read and written by the sector. Offsets and lengths are whole sectors. Windows refuses writes to the
// sectors of a volume it has mounted, so a disk is written after diskpart cleaned it, and the partition table last.
public interface IRawDisk : IDisposable
{
    int SectorSize { get; }

    long Length { get; }

    void Read(long offset, Span<byte> buffer);

    void Write(long offset, ReadOnlySpan<byte> buffer);

    // Until the disk holds every write.
    void Flush();

    // Makes Windows read the partition table again, as it does after diskpart changed it.
    void UpdateProperties();
}
