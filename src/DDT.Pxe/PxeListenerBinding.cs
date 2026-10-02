// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Core.Windows;

namespace DDT.Pxe;

// Where the listeners bind and how the host finds its interfaces. Standard is the real setup. It uses the wildcard
// address, which leaves Kestrel alone, the PXE ports, and the host's interfaces as they are at each apply.
public sealed record PxeListenerBinding(IPAddress Address, int DhcpPort, int BootServerPort, int TftpPort, Func<string, NetworkInterfaceMap> Interfaces)
{
    public static PxeListenerBinding Standard { get; } = new(IPAddress.Any, 67, 4011, 69, NetworkInterfaceMap.FromHost)
    {
        DhcpServerHere = DhcpServerRole.Installed,
    };

    // Whether a DHCP server on this computer needs the DHCP port for itself. Asked at each apply.
    public Func<bool> DhcpServerHere { get; init; } = () => false;
}
