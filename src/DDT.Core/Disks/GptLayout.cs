// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace DDT.Core.Disks;

// A GUID partition table (UEFI 2.10, section 5.3) on a disk of 512-byte sectors, read from a disk image and written for
// a disk of another size. The partition entries are kept byte for byte as the image has them, so fields DDT does not
// know survive; only the headers and the protective MBR are written anew.
public sealed class GptLayout
{
    public const int SectorSize = 512;

    // Where partitioning tools start partitions, and where DDT starts the ones it adds.
    public const long AlignmentSectors = 1024 * 1024 / SectorSize;

    public const string NoTableMessage =
        "The file has no GUID partition table, so it is not a UEFI disk image. Upload a disk image such as a distribution's cloud image.";

    public const string FourKilobyteSectorsMessage =
        "The disk image is made for disks with 4 KiB sectors. DDT writes images for disks with 512-byte sectors, which is what " +
        "distributions publish.";

    public const string DamagedMessage = "The GUID partition table of the disk image is damaged.";

    private const ulong Signature = 0x5452415020494645;
    private const uint Revision = 0x00010000;
    private const int HeaderSize = 92;
    private const int MinEntrySize = 128;
    private const int MaxEntrySize = 4096;
    private const int MaxEntryCount = 4096;
    private const int NameOffset = 56;
    private const int NameLength = 72;

    // Real tables put their entries at LBA 2. The cap keeps a forged header from asking for gigabytes.
    private const long MaxEntriesLba = AlignmentSectors;

    private readonly byte[] _entries;

    private GptLayout(
        Guid diskId,
        long firstUsableLba,
        long lastUsableLba,
        long backupLba,
        long entriesLba,
        int entryCount,
        int entrySize,
        byte[] entries)
    {
        DiskId = diskId;
        FirstUsableLba = firstUsableLba;
        LastUsableLba = lastUsableLba;
        BackupLba = backupLba;
        EntriesLba = entriesLba;
        EntryCount = entryCount;
        EntrySize = entrySize;
        _entries = entries;
        Partitions = ReadPartitions(entries, entryCount, entrySize);
    }

    public Guid DiskId { get; }

    public long FirstUsableLba { get; }

    public long LastUsableLba { get; }

    // Where the backup header is, which is the disk's last sector once the table is written for the disk.
    public long BackupLba { get; }

    public long EntriesLba { get; }

    public int EntryCount { get; }

    public int EntrySize { get; }

    public IReadOnlyList<GptPartition> Partitions { get; }

    public int EntrySectors => SectorsFor((long)EntryCount * EntrySize);

    public long BackupEntriesLba => BackupLba - EntrySectors;

    // The disk the table was written for: through the backup header.
    public long DiskSectors => BackupLba + 1;

    public long LastUsedLba => Partitions.Count == 0 ? FirstUsableLba - 1 : Partitions.Max(partition => partition.LastLba);

    // The bytes from the start of the disk through the primary partition entries.
    public long HeadBytes => (EntriesLba + EntrySectors) * SectorSize;

    // A new, empty table with the usual layout: 128 entries of 128 bytes right after the primary header.
    public static GptLayout Create(long diskSectors, Guid diskId, int entryCount = 128)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(entryCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(entryCount, MaxEntryCount);

        int entrySectors = SectorsFor((long)entryCount * MinEntrySize);
        long firstUsable = 2 + entrySectors;
        long lastUsable = diskSectors - entrySectors - 2;

        if (lastUsable < firstUsable)
        {
            throw new ArgumentOutOfRangeException(nameof(diskSectors), diskSectors, "The disk is too small for a partition table.");
        }

        return new GptLayout(diskId, firstUsable, lastUsable, diskSectors - 1, 2, entryCount, MinEntrySize, new byte[entrySectors * SectorSize]);
    }

    // The sector size a disk image was made for, from where its primary header is: 512, 4096, or 0 when head, the
    // image's first bytes, holds no GUID partition table at either place.
    public static int SectorSizeOf(ReadOnlySpan<byte> head)
    {
        if (HasSignatureAt(head, SectorSize))
        {
            return SectorSize;
        }

        return HasSignatureAt(head, 4096) ? 4096 : 0;
    }

    // Checks the primary header in the first two sectors and says how many bytes from the start of the disk Read needs.
    public static long HeadBytesFor(ReadOnlySpan<byte> firstSectors)
    {
        if (firstSectors.Length < 2 * SectorSize || !HasSignatureAt(firstSectors, SectorSize))
        {
            throw new InvalidGptException(SectorSizeOf(firstSectors) == 4096 ? FourKilobyteSectorsMessage : NoTableMessage);
        }

        ReadOnlySpan<byte> header = firstSectors.Slice(SectorSize, SectorSize);
        CheckHeader(header);

        long entriesLba = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(header[72..]));
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[80..]);
        int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[84..]);

        return (entriesLba + SectorsFor((long)count * size)) * SectorSize;
    }

    // head starts at LBA 0 and holds at least what HeadBytesFor says.
    public static GptLayout Read(ReadOnlySpan<byte> head)
    {
        long needed = HeadBytesFor(head);

        if (head.Length < needed)
        {
            throw new ArgumentException($"The partition table needs the first {needed} bytes of the disk, but only {head.Length} were given.", nameof(head));
        }

        ReadOnlySpan<byte> header = head.Slice(SectorSize, SectorSize);
        long firstUsable = ReadLba(header, 40);
        long lastUsable = ReadLba(header, 48);
        long backup = ReadLba(header, 32);
        long entriesLba = ReadLba(header, 72);
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[80..]);
        int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[84..]);
        int entrySectors = SectorsFor((long)count * size);
        byte[] entries = head.Slice((int)(entriesLba * SectorSize), entrySectors * SectorSize).ToArray();

        if (Crc32.Append(0, entries.AsSpan(0, count * size)) != BinaryPrimitives.ReadUInt32LittleEndian(header[88..]))
        {
            throw new InvalidGptException(DamagedMessage);
        }

        if (entriesLba + entrySectors > firstUsable || firstUsable > lastUsable + 1 || lastUsable >= backup)
        {
            throw new InvalidGptException($"{DamagedMessage} Its usable range does not fit its own headers.");
        }

        GptLayout layout = new(new Guid(header.Slice(56, 16)), firstUsable, lastUsable, backup, entriesLba, count, size, entries);
        CheckPartitions(layout);

        return layout;
    }

    public static async Task<GptLayout> ReadAsync(Stream disk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(disk);

        byte[] first = new byte[2 * SectorSize];
        disk.Position = 0;
        int read = await disk.ReadAtLeastAsync(first, first.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

        if (read < first.Length)
        {
            throw new InvalidGptException(NoTableMessage);
        }

        byte[] head = new byte[HeadBytesFor(first)];
        disk.Position = 0;

        if (await disk.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false) < head.Length)
        {
            throw new InvalidGptException($"{DamagedMessage} The file ends inside the partition table.");
        }

        return Read(head);
    }

    // The table written for a disk of diskSectors: the backup at its end, and the usable range up to the backup.
    public GptLayout ForDisk(long diskSectors)
    {
        long lastUsable = diskSectors - EntrySectors - 2;

        if (LastUsedLba > lastUsable)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The partitions end at sector {LastUsedLba}, which a disk of {diskSectors} sectors cannot hold with its backup table."));
        }

        return new GptLayout(DiskId, FirstUsableLba, lastUsable, diskSectors - 1, EntriesLba, EntryCount, EntrySize, _entries);
    }

    // Adds a partition in the first free entry, from firstLba through lastLba.
    public GptLayout WithPartition(Guid type, Guid id, string name, long firstLba, long lastLba)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (type == Guid.Empty)
        {
            throw new ArgumentException("A partition needs a type.", nameof(type));
        }

        if (name.Length > NameLength / 2)
        {
            throw new ArgumentException($"A partition name has at most {NameLength / 2} characters.", nameof(name));
        }

        if (firstLba < FirstUsableLba || lastLba > LastUsableLba || firstLba > lastLba)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Sectors {firstLba} to {lastLba} are not within the usable sectors {FirstUsableLba} to {LastUsableLba}."));
        }

        if (Partitions.Any(partition => partition.FirstLba <= lastLba && firstLba <= partition.LastLba))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Sectors {firstLba} to {lastLba} overlap a partition."));
        }

        int free = Enumerable.Range(0, EntryCount).FirstOrDefault(index => IsUnused(EntryAt(_entries, index, EntrySize)), -1);

        if (free < 0)
        {
            throw new InvalidOperationException("The partition table has no free entry.");
        }

        byte[] entries = (byte[])_entries.Clone();
        Span<byte> entry = entries.AsSpan(free * EntrySize, EntrySize);
        entry.Clear();
        type.TryWriteBytes(entry);
        id.TryWriteBytes(entry[16..]);
        BinaryPrimitives.WriteInt64LittleEndian(entry[32..], firstLba);
        BinaryPrimitives.WriteInt64LittleEndian(entry[40..], lastLba);
        Encoding.Unicode.GetBytes(name, entry.Slice(NameOffset, NameLength));

        return new GptLayout(DiskId, FirstUsableLba, LastUsableLba, BackupLba, EntriesLba, EntryCount, EntrySize, entries);
    }

    // Adds a partition of sectors at the end of the usable range, starting on a 1 MiB boundary.
    public GptLayout WithPartitionAtEnd(Guid type, Guid id, string name, long sectors)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sectors, 1);

        long first = (LastUsableLba + 1 - sectors) / AlignmentSectors * AlignmentSectors;

        if (first <= LastUsedLba)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"A partition of {sectors} sectors does not fit after the last partition, which ends at sector {LastUsedLba}."));
        }

        return WithPartition(type, id, name, first, first + sectors - 1);
    }

    // LBA 0 for this disk, keeping the boot code and disk signature of original, the image's own sector 0, so the disk
    // still starts in firmware that boots it the legacy way.
    public byte[] ProtectiveMbr(ReadOnlySpan<byte> original)
    {
        byte[] mbr = new byte[SectorSize];

        if (original.Length >= SectorSize && original[510] == 0x55 && original[511] == 0xAA)
        {
            original[..446].CopyTo(mbr);
        }

        Span<byte> record = mbr.AsSpan(446, 16);
        record[2] = 0x02;
        record[4] = 0xEE;
        record[5] = 0xFF;
        record[6] = 0xFF;
        record[7] = 0xFF;
        BinaryPrimitives.WriteUInt32LittleEndian(record[8..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(record[12..], (uint)Math.Min(DiskSectors - 1, uint.MaxValue));
        mbr[510] = 0x55;
        mbr[511] = 0xAA;

        return mbr;
    }

    public byte[] PrimaryHeader() => Header(1, BackupLba, EntriesLba);

    public byte[] BackupHeader() => Header(BackupLba, 1, BackupEntriesLba);

    // The entries as they are written at EntriesLba and at BackupEntriesLba, padded to whole sectors.
    public byte[] EntryArray() => (byte[])_entries.Clone();

    private byte[] Header(long myLba, long alternateLba, long entriesLba)
    {
        byte[] sector = new byte[SectorSize];
        Span<byte> header = sector;
        BinaryPrimitives.WriteUInt64LittleEndian(header, Signature);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], Revision);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], HeaderSize);
        BinaryPrimitives.WriteInt64LittleEndian(header[24..], myLba);
        BinaryPrimitives.WriteInt64LittleEndian(header[32..], alternateLba);
        BinaryPrimitives.WriteInt64LittleEndian(header[40..], FirstUsableLba);
        BinaryPrimitives.WriteInt64LittleEndian(header[48..], LastUsableLba);
        DiskId.TryWriteBytes(header[56..]);
        BinaryPrimitives.WriteInt64LittleEndian(header[72..], entriesLba);
        BinaryPrimitives.WriteUInt32LittleEndian(header[80..], (uint)EntryCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header[84..], (uint)EntrySize);
        BinaryPrimitives.WriteUInt32LittleEndian(header[88..], Crc32.Append(0, _entries.AsSpan(0, EntryCount * EntrySize)));
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..], Crc32.Append(0, header[..HeaderSize]));

        return sector;
    }

    private static void CheckHeader(ReadOnlySpan<byte> header)
    {
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);

        if (BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != Revision || headerSize is < HeaderSize or > SectorSize)
        {
            throw new InvalidGptException($"{DamagedMessage} Its header has a revision or size DDT does not know.");
        }

        Span<byte> copy = stackalloc byte[(int)headerSize];
        header[..(int)headerSize].CopyTo(copy);
        BinaryPrimitives.WriteUInt32LittleEndian(copy[16..], 0);

        if (Crc32.Append(0, copy) != BinaryPrimitives.ReadUInt32LittleEndian(header[16..])
            || BinaryPrimitives.ReadUInt64LittleEndian(header[24..]) != 1)
        {
            throw new InvalidGptException(DamagedMessage);
        }

        ulong entriesLba = BinaryPrimitives.ReadUInt64LittleEndian(header[72..]);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(header[80..]);
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(header[84..]);

        if (entriesLba is < 2 or > MaxEntriesLba
            || count is 0 or > MaxEntryCount
            || size is < MinEntrySize or > MaxEntrySize
            || size % 8 != 0)
        {
            throw new InvalidGptException($"{DamagedMessage} Its partition entries are laid out in a way DDT does not read.");
        }
    }

    private static void CheckPartitions(GptLayout layout)
    {
        GptPartition? previous = null;

        foreach (GptPartition partition in layout.Partitions.OrderBy(partition => partition.FirstLba))
        {
            if (partition.FirstLba > partition.LastLba
                || partition.FirstLba < layout.FirstUsableLba
                || partition.LastLba > layout.LastUsableLba)
            {
                throw new InvalidGptException($"{DamagedMessage} Partition {partition.Number} lies outside the usable sectors.");
            }

            if (previous is not null && partition.FirstLba <= previous.LastLba)
            {
                throw new InvalidGptException($"{DamagedMessage} Partitions {previous.Number} and {partition.Number} overlap.");
            }

            previous = partition;
        }
    }

    private static List<GptPartition> ReadPartitions(byte[] entries, int count, int size)
    {
        List<GptPartition> partitions = [];

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = EntryAt(entries, index, size);

            if (IsUnused(entry))
            {
                continue;
            }

            string name = Encoding.Unicode.GetString(entry.Slice(NameOffset, NameLength));
            int end = name.IndexOf('\0', StringComparison.Ordinal);

            partitions.Add(new GptPartition(
                index + 1,
                new Guid(entry[..16]),
                new Guid(entry.Slice(16, 16)),
                BinaryPrimitives.ReadInt64LittleEndian(entry[32..]),
                BinaryPrimitives.ReadInt64LittleEndian(entry[40..]),
                BinaryPrimitives.ReadUInt64LittleEndian(entry[48..]),
                end < 0 ? name : name[..end]));
        }

        return partitions;
    }

    private static ReadOnlySpan<byte> EntryAt(byte[] entries, int index, int size) => entries.AsSpan(index * size, size);

    private static bool IsUnused(ReadOnlySpan<byte> entry) => entry[..16].IndexOfAnyExcept((byte)0) < 0;

    private static long ReadLba(ReadOnlySpan<byte> header, int offset)
    {
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(header[offset..]);

        return value > long.MaxValue / SectorSize ? throw new InvalidGptException(DamagedMessage) : (long)value;
    }

    private static bool HasSignatureAt(ReadOnlySpan<byte> head, int offset) =>
        head.Length >= offset + 8 && BinaryPrimitives.ReadUInt64LittleEndian(head[offset..]) == Signature;

    private static int SectorsFor(long bytes) => (int)((bytes + SectorSize - 1) / SectorSize);
}
