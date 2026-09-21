// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;

namespace DDT.Protocols.Dhcp;

public sealed record DhcpMessage
{
    public required byte Operation { get; init; }

    public required byte HardwareType { get; init; }

    public required uint TransactionId { get; init; }

    public required ushort Seconds { get; init; }

    public required ushort Flags { get; init; }

    public required IPAddress ClientAddress { get; init; }

    public required IPAddress GatewayAddress { get; init; }

    public required ReadOnlyMemory<byte> ClientHardwareAddress { get; init; }

    public required DhcpMessageType MessageType { get; init; }

    public ushort? MaximumMessageSize { get; init; }

    public string? VendorClassIdentifier { get; init; }

    public IPAddress? ServerIdentifier { get; init; }

    public ReadOnlyMemory<byte> ClientIdentifier { get; init; }

    public ReadOnlyMemory<byte> ParameterRequestList { get; init; }

    // Option 97 exactly as it arrived, all 17 octets including the leading type byte. Deliberately
    // not turned into a Guid: SMBIOS, Windows and RFC 4122 disagree about the byte order of the
    // first three fields, so this layer echoes the bytes and leaves the rendering to a layer that
    // can be corrected without changing what goes on the wire.
    public ReadOnlyMemory<byte> MachineIdentifier { get; init; }

    public ClientArchitecture? Architecture { get; init; }

    // Option 93 was present but its length was zero or odd, which RFC 4578 section 2.1 forbids.
    // Kept separate from a missing option so an operator can tell a non PXE client from a broken one.
    public bool ArchitectureMalformed { get; init; }

    public ReadOnlyMemory<byte> VendorSpecific { get; init; }

    public bool RequestsOption(byte code) => ParameterRequestList.Span.Contains(code);

    public bool IsBroadcastRequested => (Flags & 0x8000) != 0;
}
