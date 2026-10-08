// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Server.Deployments;

namespace DDT.Server.Machines;

// The restart into the other boot environment a machine was asked for: how long it counts, and what ends it.
public static class PendingSwitch
{
    // Whether the machine was asked to restart no longer ago than a restart may take.
    public static bool IsOpen(Machine machine, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return IsOpen(machine.NextEnvironmentUtc, now);
    }

    // The same for the time alone, where the machine's row is not at hand. A time in the future is left from before
    // the clock was set back, or comes from a host with another clock, and opens nothing.
    public static bool IsOpen(DateTimeOffset? asked, DateTimeOffset now) =>
        asked is { } at && at <= now && now - at <= DeploymentLimits.EnvironmentSwitch;

    // Whether a registration from the environment is the restart the machine was asked for, in time.
    public static bool Arrives(Machine machine, AgentEnvironment environment, DateTimeOffset now) =>
        IsOpen(machine, now) && machine.NextEnvironment == environment;

    // Whether the server told the machine to restart into the environment so lately that its agent is still about to.
    // An agent that still polls after that did not restart.
    public static bool IsOrdered(Machine machine, AgentEnvironment environment, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return machine.NextEnvironment == environment
            && machine.NextEnvironmentUtc is { } at
            && at <= now
            && now - at <= DeploymentLimits.RestartOrderOpen;
    }

    // The boot environment the machine did not start although the server restarted it for its run as often as it
    // does. Null while the server still orders restarts, or one is under way, or the run is not the machine's any more.
    public static AgentEnvironment? GaveUp(Machine machine, Deployment? run)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return run is { State: DeploymentState.Assigned, BootEnvironment: { } starts }
            && run.Id == machine.ActiveDeploymentId
            && run.RestartsOrdered >= DeploymentLimits.RestartOrders
            && machine.NextEnvironment is null
            && starts != machine.AgentEnvironment
                ? starts
                : null;
    }

    // The machine is where it was asked to go, or is told where to go by a run. The sequence picked for it stays, with
    // the time it was picked at, so it lapses when the restart's time is over.
    public static void End(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        machine.NextEnvironment = null;

        if (machine.PendingSequenceId is null)
        {
            machine.NextEnvironmentUtc = null;
        }
    }

    public static void Forget(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        machine.NextEnvironment = null;
        machine.NextEnvironmentUtc = null;
        machine.PendingSequenceId = null;
    }

    // Forgets a restart that took too long, and the sequence picked for one once its time is over, also where the
    // machine arrived: the pick is only a suggestion. True if the machine changed.
    public static bool Expire(Machine machine, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(machine);

        if (machine.NextEnvironmentUtc is null || IsOpen(machine, now))
        {
            return false;
        }

        Forget(machine);

        return true;
    }
}
