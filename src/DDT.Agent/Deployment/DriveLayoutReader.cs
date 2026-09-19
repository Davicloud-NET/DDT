using System.Buffers.Binary;

namespace DDT.Agent.Deployment;

// Counts the partitions in use in DRIVE_LAYOUT_INFORMATION_EX as IOCTL_DISK_GET_DRIVE_LAYOUT_EX returns it
// (winioctl.h, 64-bit layout).
public static class DriveLayoutReader
{
    public const int HeaderLength = 48;
    public const int EntryLength = 144;

    public const int StyleMbr = 0;
    public const int StyleGpt = 1;
    public const int StyleRaw = 2;

    private const int PartitionCountOffset = 4;
    private const int EntryPartitionNumberOffset = 24;
    private const int EntryMbrTypeOffset = 32;

    // An MBR layout always lists four slots per table, and the extended container holding logical drives is no
    // partition anyone would recognise, so both are left out.
    public static int CountUsedPartitions(ReadOnlySpan<byte> layout)
    {
        if (layout.Length < HeaderLength)
        {
            throw new ArgumentException("The drive layout is too short.", nameof(layout));
        }

        int style = BinaryPrimitives.ReadInt32LittleEndian(layout);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(layout[PartitionCountOffset..]);

        if (style is not (StyleMbr or StyleGpt) || count == 0)
        {
            return 0;
        }

        if (count > (layout.Length - HeaderLength) / EntryLength)
        {
            throw new ArgumentException("The drive layout lists more partitions than it holds.", nameof(layout));
        }

        int used = 0;

        for (int index = 0; index < count; index++)
        {
            ReadOnlySpan<byte> entry = layout.Slice(HeaderLength + (index * EntryLength), EntryLength);

            if (style == StyleGpt)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(entry[EntryPartitionNumberOffset..]) != 0)
                {
                    used++;
                }

                continue;
            }

            if (entry[EntryMbrTypeOffset] is not (0x00 or 0x05 or 0x0F or 0x85))
            {
                used++;
            }
        }

        return used;
    }
}
