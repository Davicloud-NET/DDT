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

    // The address of the interface the datagram arrived on. Not taken from configuration, because
    // one host networked process may serve several segments and siaddr has to be the address on
    // the segment the client is actually on.
    public required IPAddress LocalAddress { get; init; }

    public required IPAddress SourceAddress { get; init; }

    public required int SourcePort { get; init; }
}
