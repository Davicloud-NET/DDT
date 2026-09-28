// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Deployments;

namespace DDT.Server.Machines;

// A registration as MachineArrivals recorded it. Machine is null when it was refused. Before is the active run's state
// and Tested what the rules tested of the machine, both from before the registration.
public sealed record Arrival(
    Machine? Machine,
    Deployment? Active,
    DeploymentState? Before,
    Deployment? Continued,
    string? Tested,
    RegistrationRefusal Refusal)
{
    public static Arrival Refused(RegistrationRefusal refusal) => new(null, null, null, null, null, refusal);
}
