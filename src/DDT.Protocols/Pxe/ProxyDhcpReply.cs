// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Protocols.Dhcp;

namespace DDT.Protocols.Pxe;

public sealed record ProxyDhcpReply
{
    public required DhcpMessageType MessageType { get; init; }

    public required uint TransactionId { get; init; }

    public required ushort Flags { get; init; }

    public required byte HardwareType { get; init; }

    public required ReadOnlyMemory<byte> ClientHardwareAddress { get; init; }

    public required IPAddress ClientAddress { get; init; }

    public required IPAddress GatewayAddress { get; init; }

    public required IPAddress NextServerAddress { get; init; }

    public required IPAddress ServerIdentifier { get; init; }

    public required string ServerHostName { get; init; }

    public required string BootFileName { get; init; }

    public required string VendorClassIdentifier { get; init; }

    public ReadOnlyMemory<byte> MachineIdentifier { get; init; }

    public ReadOnlyMemory<byte> VendorSpecific { get; init; }

    public required DhcpReplyDestination Destination { get; init; }
}
