// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Deployments;

namespace DDT.Server.Settings;

// What the machines section decides once its own problems and those of the proxies are taken into account.
public sealed record MachinePolicy(bool RequireWebApproval, int MaxWaitingPerAddress, int MaxWaiting, ZeroTouchNetworks ZeroTouchNetworks)
{
    // With RequireWebApproval on, a netboot always waits for a sign-in, no matter which networks are listed.
    public bool ZeroTouchEnabled => !RequireWebApproval && !ZeroTouchNetworks.IsEmpty;
}
