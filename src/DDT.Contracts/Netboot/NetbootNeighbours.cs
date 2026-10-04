// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Netboot;

// What else answers netboot on the server, and what a DHCP server has to say to send machines to DDT. Ports is null on
// a server that is not Windows. Helper says whether DDT can change the DHCP server and WDS on this computer itself.
public sealed record NetbootNeighbours(
    IReadOnlyList<NetbootPort>? Ports,
    NetbootService Dhcp,
    NetbootService Wds,
    bool Helper,
    string BootServer,
    string BootFile)
{
    // DDT leaves UDP 67 to the DHCP server of this computer and answers on 4011 alone.
    public bool LeavesDhcpPort { get; init; }

    // Whether that DHCP server sends option 60, PXEClient, which sends machines to 4011. Null where nobody could ask it.
    public bool? DhcpSendsPxe { get; init; }
}
