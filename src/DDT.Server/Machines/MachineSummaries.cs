// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;
using DDT.Server.Deployments;

namespace DDT.Server.Machines;

public static class MachineSummaries
{
    // The deployment is the machine's active one, else the one that ended last. Every caller passes it, because
    // the web UI replaces the whole row with what it receives.
    public static MachineSummary From(Machine machine, Deployment? deployment)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return new MachineSummary(
            machine.Id,
            machine.State,
            machine.SmbiosUuid,
            machine.PrimaryMac,
            machine.MacAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries),
            machine.Manufacturer,
            machine.Model,
            machine.SerialNumber,
            machine.AssignedName,
            machine.AgentVersion,
            machine.FirstSeenUtc,
            machine.LastSeenUtc,
            machine.LastSeenAddress,
            machine.SignedInUserName,
            machine.FirstSeenAddress,
            machine.FirstApprovedUtc is not null,
            machine.Disks,
            machine.EligibleDiskCount,
            deployment is null ? null : DeploymentSummaries.From(deployment));
    }
}
