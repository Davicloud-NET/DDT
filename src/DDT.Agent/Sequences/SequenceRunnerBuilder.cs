// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// Puts a SequenceRunner together from the machine's tools, the same way for Windows PE, the installed Windows, a dry
// run and the tests. Without AccountTools, a dry run only logs accounts and shares, and any other run uses Windows.
public sealed class SequenceRunnerBuilder
{
    public required IAgentServer Server { get; init; }

    public required IDiskPartitioner Partitioner { get; init; }

    public required IRawDisks RawDisks { get; init; }

    public required IImageApplier Applier { get; init; }

    public required IBcdWriter BcdWriter { get; init; }

    public required IRebooter Rebooter { get; init; }

    // Keeps a restart that leaves Windows PE due from the moment the run knows of it until it happens.
    public required WindowsPERestartMarker RestartMarker { get; init; }

    public required IToolRunner Tools { get; init; }

    public required IDomainJoiner Joiner { get; init; }

    public required WindowsHandOver HandOver { get; init; }

    public required AgentLog Log { get; init; }

    public required TimeProvider TimeProvider { get; init; }

    public required SequenceRunnerOptions Options { get; init; }

    // What the console at the machine shows, which the run keeps up to date.
    public ConsoleStatus? Status { get; init; }

    public AccountTools? AccountTools { get; init; }

    public SequenceRunner Build()
    {
        RunBootOrder bootOrder = new(BcdWriter, RestartMarker, Log);
        RunEnding ending = new(bootOrder, Rebooter, RestartMarker, Log, TimeProvider, Status);
        RunPreflight preflight = new(Server, new RunDiskChoice(Partitioner, RawDisks), Applier, Log, TimeProvider, Options.SystemDirectory);
        RunInputsWait inputs = new(Server, Status?.Console, Log, TimeProvider);

        return new SequenceRunner(
            new SequenceRunFactory(new RunHeartbeatFactory(Server, Log, TimeProvider, Options.HeartbeatInterval), Status, Log),
            new RunStart(preflight, Partitioner, ending, Status, Log, TimeProvider),
            new PhaseRunner(StepsFor, inputs, HandOver, bootOrder, ending, Log),
            ending,
            Options,
            Log);
    }

    // A run's own step runners, as they share its session, store and heartbeat.
    private AgentStepRunner StepsFor(SequenceRun run)
    {
        RunSession session = run.Session;
        RunHeartbeat heartbeat = run.Heartbeat;
        RunDownloads downloads = new(Server, session, Log, TimeProvider, Options.HeartbeatInterval);
        AccountTools accountTools = AccountTools ?? (Options.DryRun ? AccountTools.DryRun(Log) : AccountTools.Native(Log));

        return new AgentStepRunner(
            [
                new PartitionStepRunner(Partitioner, session, run.Store, Log, Options.DryRun),
                new ApplyImageStepRunner(Applier, downloads, session, Log),
                new InjectDriversStepRunner(Tools, downloads, session, Log),
                new WriteUnattendStepRunner(Server, session, heartbeat.ReportNowAsync, Log, TimeProvider),
                new JoinDomainStepRunner(Joiner, Server, session, heartbeat.ReportNowAsync, Log, TimeProvider),
                new RunScriptStepRunner(Tools, downloads, session, Log, Options.WorkDirectory),
                new WriteRawImageStepRunner(Partitioner, RawDisks, downloads, session, Log),
                new WriteCloudInitSeedStepRunner(RawDisks, session, Log, TimeProvider),
                new PauseStepRunner(heartbeat, Status?.Console, Log, TimeProvider),
            ],
            new StepAccounts(Server, session, heartbeat.ReportNowAsync, accountTools, Log, TimeProvider),
            heartbeat.TokenRejected,
            Log,
            TimeProvider);
    }
}
