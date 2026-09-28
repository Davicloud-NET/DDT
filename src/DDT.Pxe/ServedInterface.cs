// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;

namespace DDT.Pxe;

// Address answers a request that does not say which address the client used, such as a broadcast DISCOVER. Addresses
// holds every IPv4 address, because a site's option 66 or an HTTP boot URL may name a secondary one.
public sealed record ServedInterface(int Index, string Name, IReadOnlyList<IPAddress> Addresses)
{
    public ServedInterface(int index, string name, IPAddress address)
        : this(index, name, [address])
    {
    }

    public IPAddress Address => Addresses[0];

    public bool Owns(IPAddress address) => Addresses.Contains(address);
}
