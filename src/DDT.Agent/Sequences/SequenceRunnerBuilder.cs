// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// Puts a SequenceRunner together from the machine's tools, the same way for WinPE, the installed Windows, a dry run
// and the tests.
public sealed class SequenceRunnerBuilder
{
    public required IAgentServer Server { get; init; }

    public required IDisks Disks { get; init; }

    public required IDiskPartitioner Partitioner { get; init; }

    public required IRawDisks RawDisks { get; init; }

    public required IFreeSpace FreeSpace { get; init; }

    public required IImageApplier Applier { get; init; }

    // What the platform checks of a fresh run before anything is erased.
    public required IReadOnlyList<IPreflightCheck> PreflightChecks { get; init; }

    public required IBcdWriter BcdWriter { get; init; }

    public required IBootOrder BootOrder { get; init; }

    public required IRebooter Rebooter { get; init; }

    // Records a restart that the boot environment owes, from the moment the run knows about it until it happens.
    public required IBootRestartMarker RestartMarker { get; init; }

    public required IToolRunner Tools { get; init; }

    public required IDomainJoiner Joiner { get; init; }

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
        RunPreflight preflight = new(Server, new RunDiskChoice(Disks, RawDisks), PreflightChecks, Log, TimeProvider);
        RunInputsWait inputs = new(Server, Status?.Console, Log, TimeProvider);

        return new SequenceRunner(
            new SequenceRunFactory(new RunHeartbeatFactory(Server, Log, TimeProvider, Options.HeartbeatInterval), Status, Log),
            new RunStart(preflight, Partitioner, ending, Status, Log, TimeProvider),
            new PhaseRunner(StepsFor, inputs, HandOver, bootOrder, ending, Log),
            ending,
            Options,
            Log);
    }

    // Each run gets its own step runners, because they share its session, store and heartbeat.
    private AgentStepRunner StepsFor(SequenceRun run)
    {
        RunSession session = run.Session;
        RunHeartbeat heartbeat = run.Heartbeat;
        RunDownloads downloads = new(Server, session, FreeSpace, Log, TimeProvider, Options.HeartbeatInterval);

        return new AgentStepRunner(
            [
                new PartitionStepRunner(Partitioner, session, run.Store, Log, Options.DryRun),
                new ApplyImageStepRunner(Applier, downloads, session, Log),
                new InjectDriversStepRunner(Tools, downloads, session, Log),
                new WriteUnattendStepRunner(Server, session, heartbeat.ReportNowAsync, Log, TimeProvider),
                new JoinDomainStepRunner(Joiner, Server, session, heartbeat.ReportNowAsync, Log, TimeProvider),
                new RunScriptStepRunner(Tools, new WindowsScriptLauncher(), downloads, session, Log, Options.WorkDirectory),
                new WriteRawImageStepRunner(Disks, RawDisks, downloads, session, Log),
                new WriteCloudInitSeedStepRunner(RawDisks, session, Log, TimeProvider),
                new PauseStepRunner(heartbeat, Status?.Console, Log, TimeProvider),
            ],
            new StepAccounts(Server, session, heartbeat.ReportNowAsync, AccountTools, Log, TimeProvider),
            heartbeat.TokenRejected,
            Log,
            TimeProvider);
    }
}
