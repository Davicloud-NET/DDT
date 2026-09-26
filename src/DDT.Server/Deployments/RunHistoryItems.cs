// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Core.Machines;
using DDT.Server.Machines;

namespace DDT.Server.Deployments;

public static class RunHistoryItems
{
    // The run history lists runs of every machine, so each row names its machine the way the machines list does.
    public static RunHistoryItem From(Machine machine, Deployment run)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);

        return new RunHistoryItem(
            machine.Id,
            machine.AssignedName,
            machine.Model,
            machine.Manufacturer,
            machine.PrimaryMac,
            DeviceKinds.Classify(machine.Manufacturer, machine.Model, machine.ChassisType),
            DeploymentSummaries.From(run));
    }
}
