// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Server.Deployments;

namespace DDT.Server.Machines;

// A registration as MachineArrivals recorded it. Machine is null if it was refused. Before is the active run's state
// and Tested is what the rules tested about the machine, both from before the registration.
public sealed record Arrival(
    Machine? Machine,
    Deployment? Active,
    DeploymentState? Before,
    Deployment? Continued,
    string? Tested,
    RegistrationRefusal Refusal)
{
    // The line the server wrote into the run's log when the registration only asked whether its handed-over run goes
    // on in the installed Linux, and it does. Nothing of such a registration is taken. Null for every other.
    public MachineLogLine? Detour { get; init; }

    public static Arrival Refused(RegistrationRefusal refusal) => new(null, null, null, null, null, refusal);
}
