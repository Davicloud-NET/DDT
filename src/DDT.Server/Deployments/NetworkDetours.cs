// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Machines;
using Microsoft.Extensions.Logging;

namespace DDT.Server.Deployments;

// A machine that started from the network while its run goes on in the installed Linux. The agent of the boot
// environment asks before it registers. The server answers and takes nothing of the registration, up to a limit.
public static partial class NetworkDetours
{
    // The start from the network in a row, with no call from the installed Linux between them, that ends the run.
    public const int Max = 5;

    // Why the run ends at the limit, and what to check.
    public static string TooMany { get; } = string.Create(
        CultureInfo.InvariantCulture,
        $"The machine started from the network {Max} times in a row while the run went on in the installed Linux, with no call from the " +
        $"installed Linux in between. Each time the agent of the boot environment sent it back to its disk, and it came from the " +
        $"network again, so the installed Linux is no longer reached. Check the firmware's boot order and whether the machine starts " +
        $"from its disk.");

    // Whether a registration without a valid token asks about a run that goes on in the installed Linux. A run it names
    // must be that run. One that names none, because the state it found cannot be read, asks about the machine's run.
    public static bool Asks(NormalisedRegistration registration, [NotNullWhen(true)] Deployment? active)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return registration is { HandedOver: { } found, Environment: AgentEnvironment.LinuxBoot, StartsOver: false }
            && active is { State: DeploymentState.Running, CurrentPhase: SequencePhase.Linux }
            && (found.RunId is null || found.RunId == active.Id);
    }

    // What the run's log says of a start the server answered so, counted in run.Detours.
    public static MachineLogLine Line(Deployment run, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new MachineLogLine
        {
            MachineId = run.MachineId,
            TimestampUtc = now,
            AgentTimestampUtc = now,
            ReceivedUtc = now,
            Level = AgentLogLevel.Warning,
            Message = string.Create(
                CultureInfo.InvariantCulture,
                $"The machine started from the network while this run goes on in the installed Linux, and the agent of the boot " +
                $"environment sends it back to its disk. This is start {run.Detours} of {Max} in a row without a call from the installed " +
                $"Linux in between. At start {Max}, the server ends the run."),
            DeploymentId = run.Id,
        };
    }

    [LoggerMessage(
        EventId = 442,
        Level = LogLevel.Information,
        Message = "Machine {MachineId} started from the network from {Address} while run {RunId} goes on in its installed Linux, {Detours} times in a row: nothing was changed")]
    public static partial void Answered(ILogger logger, Guid machineId, string address, Guid runId, int detours);
}
