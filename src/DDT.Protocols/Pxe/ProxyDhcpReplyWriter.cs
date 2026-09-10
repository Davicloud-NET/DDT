using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Text;
using DDT.Protocols.Dhcp;

namespace DDT.Protocols.Pxe;

public static class ProxyDhcpReplyWriter
{
    // RFC 1542 section 2.1: a BOOTP datagram must be able to hold the minimal 300 octet message.
    // Padding goes after the End option so it cannot affect parsing.
    public const int MinimumDatagramLength = 300;

    private const int FixedHeaderLength = 236;
    private const int OptionsOffset = FixedHeaderLength + 4;
    private const int ServerNameOffset = 44;
    private const int ServerNameLength = 64;
    private const int BootFileOffset = 108;
    private const int BootFileLength = 128;

    private static ReadOnlySpan<byte> MagicCookie => [0x63, 0x82, 0x53, 0x63];

    public static bool TryWrite(ProxyDhcpReply reply, Span<byte> destination, out int bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(reply);

        bytesWritten = 0;

        if (destination.Length < MinimumDatagramLength)
        {
            return false;
        }

        destination[..MinimumDatagramLength].Clear();

        destination[0] = DhcpOperation.BootReply;
        destination[1] = reply.HardwareType;
        destination[2] = (byte)reply.ClientHardwareAddress.Length;
        destination[3] = 0;

        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], reply.TransactionId);

        // RFC 2131 Table 3: secs and hops are zero in an OFFER and an ACK.
        BinaryPrimitives.WriteUInt16BigEndian(destination[8..], 0);
        BinaryPrimitives.WriteUInt16BigEndian(destination[10..], reply.Flags);

        if (!TryWriteAddress(reply.ClientAddress, destination[12..])
            || !TryWriteAddress(IPAddress.Any, destination[16..])
            || !TryWriteAddress(reply.NextServerAddress, destination[20..])
            || !TryWriteAddress(reply.GatewayAddress, destination[24..]))
        {
            return false;
        }

        if (reply.ClientHardwareAddress.Length > 16)
        {
            return false;
        }

        reply.ClientHardwareAddress.Span.CopyTo(destination[28..]);

        if (!TryWriteNullTerminated(reply.ServerHostName, destination.Slice(ServerNameOffset, ServerNameLength)))
        {
            return false;
        }

        // The boot file goes in the fixed field as well as option 67 whenever it fits, because some
        // firmware reads only one of the two and they disagree about which.
        _ = TryWriteNullTerminated(reply.BootFileName, destination.Slice(BootFileOffset, BootFileLength));

        MagicCookie.CopyTo(destination[FixedHeaderLength..]);

        DhcpOptionWriter options = new(destination[OptionsOffset..]);

        options.WriteByte(DhcpOption.MessageType, (byte)reply.MessageType);
        options.WriteAddress(DhcpOption.ServerIdentifier, reply.ServerIdentifier);
        options.WriteAscii(DhcpOption.VendorClassIdentifier, reply.VendorClassIdentifier);

        if (!reply.MachineIdentifier.IsEmpty)
        {
            options.Write(DhcpOption.ClientMachineIdentifier, reply.MachineIdentifier.Span);
        }

        options.WriteAscii(DhcpOption.BootFileName, reply.BootFileName);

        if (!reply.VendorSpecific.IsEmpty)
        {
            options.Write(DhcpOption.VendorSpecific, reply.VendorSpecific.Span);
        }

        options.WriteEnd();

        if (options.Overflowed)
        {
            return false;
        }

        bytesWritten = Math.Max(OptionsOffset + options.BytesWritten, MinimumDatagramLength);

        return true;
    }

    private static bool TryWriteAddress(IPAddress address, Span<byte> destination) =>
        address.TryWriteBytes(destination, out int written) && written == 4;

    private static bool TryWriteNullTerminated(string value, Span<byte> field)
    {
        if (Ascii.FromUtf16(value, field, out int written) != OperationStatus.Done || written >= field.Length)
        {
            return false;
        }

        field[written] = 0;

        return true;
    }
}
