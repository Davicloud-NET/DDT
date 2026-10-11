// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Machines;

namespace DDT.Server.Deployments;

// A run between its boot environment and its installed system, where the server only knows what it was last told.
public static class HandOvers
{
    // When the run was handed over, for as long as nothing registered from the installed system. The boot environment's
    // last report says that it restarts there, and a run's reports stop with that restart, so that is its last change.
    public static DateTimeOffset? Since(Machine machine, Deployment? run)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return run is { State: DeploymentState.Running, Activity: RunActivity.Restarting, CurrentPhase: { } phase }
            && !SequenceKinds.IsBoot(phase)
            && SequenceKinds.IsBoot(SequenceKinds.Phase(machine.AgentEnvironment))
                ? run.UpdatedUtc
                : null;
    }

    // Why a run ends whose installed Linux never called while its token lasted. Null for any other run.
    public static string? NeverCalled(Machine machine, Deployment run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return run.CurrentPhase == SequencePhase.Linux && Since(machine, run) is { } since
            ? $"The installed Linux has not called since the run was handed over at {since:u}, and the run could no longer go on. " +
              "The machine did not start from its disk, or cloud-init did not install DDT's agent there."
            : null;
    }
}
