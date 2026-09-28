// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Protocols.Dhcp;

namespace DDT.Protocols.Pxe;

public sealed record ProxyDhcpRequest
{
    public required DhcpMessage Message { get; init; }

    public required ProxyDhcpListenPort ReceivedOn { get; init; }

    // The address of the interface the datagram arrived on, not one from configuration: a host serving several segments
    // must put the address on the client's own segment into siaddr.
    public required IPAddress LocalAddress { get; init; }

    public required IPAddress SourceAddress { get; init; }

    public required int SourcePort { get; init; }
}
