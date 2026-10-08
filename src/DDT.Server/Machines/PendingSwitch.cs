// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
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

    // The machine is where it was asked to go, or is told where to go by a run. The sequence picked for it stays.
    public static void End(Machine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        machine.NextEnvironment = null;
        machine.NextEnvironmentUtc = null;
    }

    public static void Forget(Machine machine)
    {
        End(machine);
        machine.PendingSequenceId = null;
    }

    // Forgets a restart that took too long, with the sequence picked for it. True if the machine changed.
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
