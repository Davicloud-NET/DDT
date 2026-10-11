// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// Puts a SequenceRunner together from the machine's tools and the platform's parts, the same way for WinPE, the
// installed Windows, a dry run and the tests.
public sealed class SequenceRunnerBuilder
{
    public required IAgentServer Server { get; init; }

    public required IDisks Disks { get; init; }

    // Finds a resumed run's volumes again. Null on a platform whose runs have none.
    public IDiskPartitioner? Partitioner { get; init; }

    // Opens the disk a raw disk image goes to. Null on a platform that writes none.
    public IRawDisks? RawDisks { get; init; }

    public required IFreeSpace FreeSpace { get; init; }

    // What the platform checks of a fresh run before anything is erased.
    public required IReadOnlyList<IPreflightCheck> PreflightChecks { get; init; }

    public required IStepRunners StepRunners { get; init; }

    // Makes an applied Windows start. Null on a platform that applies none.
    public IBcdWriter? BcdWriter { get; init; }

    public required IBootOrder BootOrder { get; init; }

    public required IRebooter Rebooter { get; init; }

    // Records a restart that the boot environment owes, from the moment the run knows about it until it happens.
    public required IBootRestartMarker RestartMarker { get; init; }

    public required IHandOver HandOver { get; init; }

    public required AgentLog Log { get; init; }

    public required TimeProvider TimeProvider { get; init; }

    public required SequenceRunnerOptions Options { get; init; }

    // What the console at the machine shows, which the run keeps up to date.
    public ConsoleStatus? Status { get; init; }

    // A dry run's tools only log accounts and shares.
    public required AccountTools AccountTools { get; init; }

    public SequenceRunner Build()
    {
        RunBootOrder bootOrder = new(BcdWriter, BootOrder, RestartMarker, Log);
        RunEnding ending = new(bootOrder, Rebooter, RestartMarker, Log, TimeProvider, Status);
        RunPreflight preflight = new(Server, new RunDiskChoice(Disks, RawDisks), PreflightChecks, Log, TimeProvider, Options.SequenceVersion);
        RunInputsWait inputs = new(Server, Status?.Console, Log, TimeProvider);

        return new SequenceRunner(
            new SequenceRunFactory(new RunHeartbeatFactory(Server, Log, TimeProvider, Options.HeartbeatInterval), Status, Log, Options.Environment),
            new RunStart(preflight, Partitioner, ending, Status, Log, TimeProvider),
            new PhaseRunner(StepsFor, inputs, HandOver, bootOrder, ending, Log),
            ending,
            Options,
            Log);
    }

    private AgentStepRunner StepsFor(SequenceRun run)
    {
        RunSession session = run.Session;
        RunHeartbeat heartbeat = run.Heartbeat;
        RunDownloads downloads = new(Server, session, FreeSpace, Log, TimeProvider, Options.HeartbeatInterval);

        return new AgentStepRunner(
            StepRunners.For(new StepRunnerParts(session, run.Store, heartbeat, downloads, Status?.Console)),
            new StepAccounts(Server, session, heartbeat.ReportNowAsync, AccountTools, Log, TimeProvider),
            heartbeat.TokenRejected,
            Log,
            TimeProvider);
    }
}
