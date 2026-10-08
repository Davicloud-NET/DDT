// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Pxe;

// What a machine should start: the boot environment its run or its restart needs, or null for the default.
// LinuxRefused: its firmware is known not to start the Linux boot environment with Secure Boot on.
public sealed record BootWish(AgentEnvironment? Wanted, bool LinuxRefused)
{
    public static BootWish None { get; } = new(null, false);
}
