// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Server.LinuxBoot;

// What netboot needs to know of one machine: how to recognise it, the boot environment of its run, the one it was
// asked to restart into and when, and whether its firmware would refuse the Linux boot environment's shim.
public sealed record BootChoiceEntry(Guid? Uuid, IReadOnlyList<string> Macs)
{
    public AgentEnvironment? Run { get; init; }

    public AgentEnvironment? Next { get; init; }

    public DateTimeOffset? NextUtc { get; init; }

    public bool LinuxRefused { get; init; }

    // Whether someone ever approved the machine. Until then anyone on the network could have registered the row.
    public bool Approved { get; init; }
}
