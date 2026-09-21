// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Protocols.Dhcp;
using DDT.Protocols.Pxe;

namespace DDT.Pxe;

public sealed record ProxyDhcpOutcome
{
    public required ProxyDhcpOutcomeKind Kind { get; init; }

    public DhcpMessage? Message { get; init; }

    public DhcpParseError ParseError { get; init; }

    public ProxyDhcpSilenceReason SilenceReason { get; init; }

    public ProxyDhcpReply? Reply { get; init; }

    public int Length { get; init; }

    // Zero leaves the choice of interface to the routing table.
    public int EgressInterface { get; init; }
}
