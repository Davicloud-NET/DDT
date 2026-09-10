using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace DDT.Protocols.Tftp;

public static class TftpPacket
{
    public const int HeaderLength = 4;

    private const string BlockSizeOption = "blksize";
    private const string TimeoutOption = "timeout";
    private const string TransferSizeOption = "tsize";
    private const string WindowSizeOption = "windowsize";

    public static bool TryReadOpcode(ReadOnlySpan<byte> datagram, out TftpOpcode opcode)
    {
        if (datagram.Length < 2)
        {
            opcode = default;

            return false;
        }

        opcode = (TftpOpcode)BinaryPrimitives.ReadUInt16BigEndian(datagram);

        return true;
    }

    public static bool TryReadReadRequest(ReadOnlySpan<byte> datagram, [NotNullWhen(true)] out TftpReadRequest? request)
    {
        request = null;

        if (!TryReadOpcode(datagram, out TftpOpcode opcode) || opcode != TftpOpcode.ReadRequest)
        {
            return false;
        }

        TftpStringReader reader = new(datagram[2..]);

        if (!reader.TryRead(out string fileName) || !reader.TryRead(out string mode) || fileName.Length == 0)
        {
            return false;
        }

        int? blockSize = null;
        int? timeout = null;
        long? transferSize = null;
        int? windowSize = null;

        while (!reader.IsEmpty)
        {
            if (!reader.TryRead(out string name) || !reader.TryRead(out string value))
            {
                break;
            }

            // RFC 2347: option names are case insensitive.
            if (name.Equals(BlockSizeOption, StringComparison.OrdinalIgnoreCase))
            {
                blockSize = ParseInt32(value);
            }
            else if (name.Equals(TimeoutOption, StringComparison.OrdinalIgnoreCase))
            {
                timeout = ParseInt32(value);
            }
            else if (name.Equals(TransferSizeOption, StringComparison.OrdinalIgnoreCase))
            {
                transferSize = ParseInt64(value);
            }
            else if (name.Equals(WindowSizeOption, StringComparison.OrdinalIgnoreCase))
            {
                windowSize = ParseInt32(value);
            }
        }

        request = new TftpReadRequest(fileName, mode, new TftpRequestedOptions(blockSize, timeout, transferSize, windowSize));

        return true;
    }

    public static bool TryReadAcknowledgement(ReadOnlySpan<byte> datagram, out ushort block)
    {
        block = 0;

        if (datagram.Length != 4
            || !TryReadOpcode(datagram, out TftpOpcode opcode)
            || opcode != TftpOpcode.Acknowledgement)
        {
            return false;
        }

        block = BinaryPrimitives.ReadUInt16BigEndian(datagram[2..]);

        return true;
    }

    public static bool TryWriteDataHeader(Span<byte> destination, ushort block)
    {
        if (destination.Length < HeaderLength)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)TftpOpcode.Data);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], block);

        return true;
    }

    public static bool TryWriteOptionAck(Span<byte> destination, TftpNegotiation negotiation, out int bytesWritten)
    {
        bytesWritten = 0;

        if (destination.Length < 2)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)TftpOpcode.OptionAcknowledgement);
        int written = 2;

        if (negotiation.AcknowledgeBlockSize && !TryAppendOption(destination, ref written, BlockSizeOption, negotiation.BlockSize))
        {
            return false;
        }

        if (negotiation.AcknowledgeTimeout
            && !TryAppendOption(destination, ref written, TimeoutOption, (int)negotiation.Timeout.TotalSeconds))
        {
            return false;
        }

        if (negotiation.AcknowledgeTransferSize
            && !TryAppendOption(destination, ref written, TransferSizeOption, negotiation.TransferSize ?? 0))
        {
            return false;
        }

        if (negotiation.AcknowledgeWindowSize
            && !TryAppendOption(destination, ref written, WindowSizeOption, negotiation.WindowSize))
        {
            return false;
        }

        bytesWritten = written;

        return true;
    }

    public static bool TryWriteError(Span<byte> destination, TftpErrorCode code, string message, out int bytesWritten)
    {
        ArgumentNullException.ThrowIfNull(message);

        bytesWritten = 0;

        if (destination.Length < 5)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt16BigEndian(destination, (ushort)TftpOpcode.Error);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)code);

        if (Ascii.FromUtf16(message, destination[4..], out int written) != OperationStatus.Done
            || 4 + written >= destination.Length)
        {
            return false;
        }

        destination[4 + written] = 0;
        bytesWritten = 5 + written;

        return true;
    }

    private static bool TryAppendOption(Span<byte> destination, ref int written, string name, long value)
    {
        Span<byte> scratch = stackalloc byte[24];

        if (!value.TryFormat(scratch, out int valueLength, provider: CultureInfo.InvariantCulture))
        {
            return false;
        }

        if (written + name.Length + 1 + valueLength + 1 > destination.Length)
        {
            return false;
        }

        Ascii.FromUtf16(name, destination[written..], out int nameLength);
        written += nameLength;
        destination[written++] = 0;

        scratch[..valueLength].CopyTo(destination[written..]);
        written += valueLength;
        destination[written++] = 0;

        return true;
    }

    private static int? ParseInt32(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;

    private static long? ParseInt64(string value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) ? parsed : null;
}
