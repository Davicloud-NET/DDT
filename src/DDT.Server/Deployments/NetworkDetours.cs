// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
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
        $"installed Linux in between. After each of the first {Max - 1} starts the agent of the boot environment sent it back to its " +
        $"disk, and it came from the network again, so the installed Linux is no longer reached. Check the firmware's boot order and " +
        $"whether the machine starts from its disk.");

    // Whether a registration without the run's token asks about a run that goes on in the installed Linux. A run it names
    // must be that run. One that names none, because the state it found cannot be read, asks about the machine's run.
    public static bool Asks(NormalisedRegistration registration, [NotNullWhen(true)] Deployment? active)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return registration is { HandedOver: { } found, Environment: AgentEnvironment.LinuxBoot, StartsOver: false }
            && active is { State: DeploymentState.Running, CurrentPhase: SequencePhase.Linux }
            && (found.RunId is null || found.RunId == active.Id);
    }

    // Whether a question is about the start the run counted last. An agent asks again when the answer got lost. A
    // question that names no start is a new one each time.
    public static bool IsRepeated(Deployment run, Guid? start)
    {
        ArgumentNullException.ThrowIfNull(run);

        return start is not null && run.DetourStart == start;
    }

    // The starts counted for a run that still goes on in the installed Linux, for a page to show. Null when none.
    public static int? Counted(Deployment? run) =>
        run is { State: DeploymentState.Running, CurrentPhase: SequencePhase.Linux, Detours: > 0 } ? run.Detours : null;

    // The audit row of a start the server counted, which holds the address the question came from.
    public static AuditEvent Audit(Deployment run, Actor machine, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(run);

        return AuditEvents.Create(
            AuditActions.DeploymentDetoured,
            run.Id.ToString("D"),
            machine,
            now,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{run.Title} on machine {run.MachineId:D}. Counted start {run.Detours} of {Max} from the network in a row while the run " +
                $"goes on in the installed Linux. Nothing else of the registration was taken."));
    }

    // What the run's log says of a start the server answered so, counted in run.Detours. Someone at the machine may
    // still start over there instead, which ends the run with an error that says so.
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
                $"environment sends it back to its disk unless someone at the machine chooses to start over there. This is start " +
                $"{run.Detours} of {Max} in a row without a call from the installed Linux in between. At start {Max}, the server ends " +
                $"the run."),
            DeploymentId = run.Id,
        };
    }

    [LoggerMessage(
        EventId = 442,
        Level = LogLevel.Information,
        Message = "Machine {MachineId} started from the network from {Address} while run {RunId} goes on in its installed Linux, {Detours} times in a row: nothing was changed")]
    public static partial void Answered(ILogger logger, Guid machineId, string address, Guid runId, int detours);

    [LoggerMessage(
        EventId = 443,
        Level = LogLevel.Information,
        Message = "Machine {MachineId} asked again from {Address} about a start from the network that run {RunId} had counted: it was not counted again")]
    public static partial void AnsweredAgain(ILogger logger, Guid machineId, string address, Guid runId);
}
