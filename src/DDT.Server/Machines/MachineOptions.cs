// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

public sealed class MachineOptions
{
    public const string SectionName = "DDT:Machines";

    // When off, a technician who signs in at the machine authorizes it. When on, that sign-in only records who is at
    // the machine, and an operator also has to approve it on the web.
    public bool RequireWebApproval { get; set; }

    // Registration is open to anyone who reaches the server, so machines nobody has approved yet are capped. There's a
    // cap per address, which a lab behind one NAT address still fits in, and one in total.
    public int MaxWaitingPerAddress { get; set; } = 100;

    public int MaxWaiting { get; set; } = 10_000;

    // CIDR networks, comma separated. A machine assigned a sequence on the web stays authorized when it netboots from
    // one of them. Empty turns zero touch off. Behind a proxy listed in DDT:ForwardedHeaders, the client address is the
    // one the proxy reports, so no network may contain a proxy's own address.
    public string ZeroTouchNetworks { get; set; } = string.Empty;
}
