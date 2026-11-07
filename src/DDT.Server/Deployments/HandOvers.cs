// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
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

    // How often the boot environment has started again since it first handed the run over. Null when it has not, and
    // once the installed system has called.
    public static int? Returns(Machine machine, Deployment? run) =>
        Since(machine, run) is not null && run is { BootReturns: > 0 } ? run.BootReturns : null;

    // Why a run ends whose installed Linux never called while its token lasted. Null for any other run.
    public static string? NeverCalled(Machine machine, Deployment run)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.CurrentPhase != SequencePhase.Linux || Since(machine, run) is not { } since)
        {
            return null;
        }

        // After a return the run's last change is that return, not the hand-over
        string last = run.BootReturns > 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"The installed Linux has not called since the run was handed over. The machine started from the network again {Times(run.BootReturns)} " +
                $"after that and was sent back to its disk, last at {since:u}, and the run could no longer go on.")
            : $"The installed Linux has not called since the run was handed over at {since:u}, and the run could no longer go on.";

        return $"{last} The machine did not start from its disk, or cloud-init did not install DDT's agent there.";
    }

    private static string Times(int count) => count switch
    {
        1 => "once",
        2 => "twice",
        _ => string.Create(CultureInfo.InvariantCulture, $"{count} times"),
    };
}
