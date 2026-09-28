// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Net;
using DDT.Protocols.Dhcp;

namespace DDT.Protocols.Pxe;

public sealed record ProxyDhcpConfiguration
{
    public required FrozenDictionary<ClientArchitecture, BootTarget> BootTargets { get; init; }

    // Used to recognise a request that already names another server in option 54.
    public ImmutableArray<IPAddress> LocalAddresses { get; init; } = [];

    // Relayed requests come from a subnet DDT was never told about. Answering one means serving a remote segment a
    // boot target chosen without any site context. Empty means refuse them all.
    public ImmutableArray<IPAddress> AuthorisedRelayAgents { get; init; } = [];
}
