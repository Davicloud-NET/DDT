// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;

namespace DDT.Protocols.Dhcp;

public static class DhcpMessageParser
{
    // RFC 2131 section 2: the fixed BOOTP part is 236 octets, then the four octet magic cookie.
    private const int FixedHeaderLength = 236;
    private const int OptionsOffset = FixedHeaderLength + 4;
    private const int ServerNameOffset = 44;
    private const int ServerNameLength = 64;
    private const int BootFileOffset = 108;
    private const int BootFileLength = 128;

    private static ReadOnlySpan<byte> MagicCookie => [0x63, 0x82, 0x53, 0x63];

    public static bool TryParse(
        ReadOnlySpan<byte> datagram,
        [NotNullWhen(true)] out DhcpMessage? message,
        out DhcpParseError error)
    {
        message = null;

        if (datagram.Length < OptionsOffset)
        {
            error = DhcpParseError.TooShort;
            return false;
        }

        if (!datagram.Slice(FixedHeaderLength, 4).SequenceEqual(MagicCookie))
        {
            error = DhcpParseError.BadMagicCookie;
            return false;
        }

        Accumulator accumulator = new();

        if (!accumulator.Read(datagram[OptionsOffset..]))
        {
            error = DhcpParseError.OptionOverrunsDatagram;
            return false;
        }

        // RFC 2131 section 4.1: the options field is interpreted first so option 52 is discovered,
        // then the file field, then the sname field.
        if ((accumulator.Overload & 1) != 0 && !accumulator.Read(datagram.Slice(BootFileOffset, BootFileLength)))
        {
            error = DhcpParseError.OptionOverrunsDatagram;
            return false;
        }

        if ((accumulator.Overload & 2) != 0 && !accumulator.Read(datagram.Slice(ServerNameOffset, ServerNameLength)))
        {
            error = DhcpParseError.OptionOverrunsDatagram;
            return false;
        }

        if (accumulator.MessageType is null)
        {
            error = DhcpParseError.MissingMessageType;
            return false;
        }

        byte hardwareLength = Math.Min(datagram[2], (byte)16);

        message = new DhcpMessage
        {
            Operation = datagram[0],
            HardwareType = datagram[1],
            TransactionId = BinaryPrimitives.ReadUInt32BigEndian(datagram[4..]),
            Seconds = BinaryPrimitives.ReadUInt16BigEndian(datagram[8..]),
            Flags = BinaryPrimitives.ReadUInt16BigEndian(datagram[10..]),
            ClientAddress = new IPAddress(datagram.Slice(12, 4)),
            GatewayAddress = new IPAddress(datagram.Slice(24, 4)),
            ClientHardwareAddress = datagram.Slice(28, hardwareLength).ToArray(),
            MessageType = accumulator.MessageType.Value,
            MaximumMessageSize = accumulator.MaximumMessageSize,
            VendorClassIdentifier = accumulator.VendorClass,
            ServerIdentifier = accumulator.ServerIdentifier,
            ClientIdentifier = accumulator.ClientIdentifier,
            ParameterRequestList = accumulator.ParameterRequestList,
            MachineIdentifier = accumulator.MachineIdentifier,
            Architecture = accumulator.Architecture,
            ArchitectureMalformed = accumulator.ArchitectureMalformed,
            VendorSpecific = accumulator.VendorSpecific,
        };

        error = DhcpParseError.None;

        return true;
    }

    // RFC 3396: a value split across repeated occurrences of one code is concatenated in the order
    // the occurrences appear.
    private static byte[] Append(byte[] existing, scoped ReadOnlySpan<byte> value) =>
        existing.Length == 0 ? value.ToArray() : [.. existing, .. value];

    private sealed class Accumulator
    {
        public DhcpMessageType? MessageType { get; private set; }

        public ushort? MaximumMessageSize { get; private set; }

        public string? VendorClass { get; private set; }

        public IPAddress? ServerIdentifier { get; private set; }

        public ClientArchitecture? Architecture { get; private set; }

        public bool ArchitectureMalformed { get; private set; }

        public byte Overload { get; private set; }

        public byte[] ClientIdentifier { get; private set; } = [];

        public byte[] ParameterRequestList { get; private set; } = [];

        public byte[] MachineIdentifier { get; private set; } = [];

        public byte[] VendorSpecific { get; private set; } = [];

        public bool Read(ReadOnlySpan<byte> block)
        {
            DhcpOptionReader reader = new(block);

            while (reader.MoveNext())
            {
                switch (reader.Code)
                {
                    case DhcpOption.MessageType when reader.Value.Length == 1:
                        MessageType = (DhcpMessageType)reader.Value[0];
                        break;
                    case DhcpOption.Overload when reader.Value.Length == 1:
                        Overload = reader.Value[0];
                        break;
                    case DhcpOption.MaximumMessageSize when reader.Value.Length == 2:
                        MaximumMessageSize = BinaryPrimitives.ReadUInt16BigEndian(reader.Value);
                        break;
                    case DhcpOption.ServerIdentifier when reader.Value.Length == 4:
                        ServerIdentifier = new IPAddress(reader.Value);
                        break;
                    case DhcpOption.VendorClassIdentifier:
                        VendorClass = Encoding.ASCII.GetString(reader.Value);
                        break;
                    case DhcpOption.ClientIdentifier:
                        ClientIdentifier = Append(ClientIdentifier, reader.Value);
                        break;
                    case DhcpOption.ParameterRequestList:
                        ParameterRequestList = Append(ParameterRequestList, reader.Value);
                        break;
                    case DhcpOption.ClientMachineIdentifier:
                        MachineIdentifier = Append(MachineIdentifier, reader.Value);
                        break;
                    case DhcpOption.VendorSpecific:
                        VendorSpecific = Append(VendorSpecific, reader.Value);
                        break;
                    case DhcpOption.ClientArchitecture:
                        ReadArchitecture(reader.Value);
                        break;
                    default:
                        break;
                }
            }

            return !reader.Truncated;
        }

        // RFC 4578 section 2.1: the length must be an even number greater than zero. Only the first
        // entry is used, because serving a later one hands a machine an image it cannot execute, and
        // honouring the list would let a hostile client force a reply by naming every architecture.
        private void ReadArchitecture(scoped ReadOnlySpan<byte> value)
        {
            if (value.Length < 2 || value.Length % 2 != 0)
            {
                ArchitectureMalformed = true;
                return;
            }

            Architecture = (ClientArchitecture)BinaryPrimitives.ReadUInt16BigEndian(value);
        }
    }
}
