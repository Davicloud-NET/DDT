using System.Buffers.Binary;
using System.Text;

namespace DDT.Agent.Deployment;

// Reads STORAGE_DEVICE_DESCRIPTOR as IOCTL_STORAGE_QUERY_PROPERTY returns it (winioctl.h).
public static class StorageDescriptorReader
{
    public const int HeaderLength = 8;
    public const int MinimumLength = 36;

    private const int SizeOffset = 4;
    private const int RemovableMediaOffset = 10;
    private const int VendorIdOffset = 12;
    private const int ProductIdOffset = 16;
    private const int BusTypeOffset = 28;

    // The Size field of STORAGE_DESCRIPTOR_HEADER: how many bytes the full descriptor needs.
    public static int ReadSize(ReadOnlySpan<byte> header) =>
        header.Length < HeaderLength ? 0 : (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(header[SizeOffset..]), int.MaxValue);

    public static StorageDeviceInfo Read(ReadOnlySpan<byte> descriptor)
    {
        if (descriptor.Length < MinimumLength)
        {
            throw new ArgumentException("The storage descriptor is too short.", nameof(descriptor));
        }

        bool removable = descriptor[RemovableMediaOffset] != 0;
        StorageBusType busType = (StorageBusType)BinaryPrimitives.ReadInt32LittleEndian(descriptor[BusTypeOffset..]);
        string? vendor = ReadString(descriptor, BinaryPrimitives.ReadUInt32LittleEndian(descriptor[VendorIdOffset..]));
        string? product = ReadString(descriptor, BinaryPrimitives.ReadUInt32LittleEndian(descriptor[ProductIdOffset..]));

        // SCSI pads both fields with spaces; NVMe drives often have no vendor and the whole name as the product.
        string model = string.Join(' ', new[] { vendor, product }.Where(part => part is not null));

        return new StorageDeviceInfo(removable, busType, model.Length == 0 ? null : model);
    }

    // A zero-terminated ASCII string at an offset from the start, where 0 means none.
    private static string? ReadString(ReadOnlySpan<byte> descriptor, uint offset)
    {
        if (offset == 0 || offset >= descriptor.Length)
        {
            return null;
        }

        ReadOnlySpan<byte> rest = descriptor[(int)offset..];
        int end = rest.IndexOf((byte)0);
        ReadOnlySpan<byte> text = end < 0 ? rest : rest[..end];

        // Firmware strings can hold control characters, which must not reach the console or the server's log.
        string printable = new([.. Encoding.ASCII.GetString(text).Select(c => c is >= ' ' and <= '~' ? c : ' ')]);
        string value = string.Join(' ', printable.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return value.Length == 0 ? null : value;
    }
}
