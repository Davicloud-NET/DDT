// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// The runners for every kind of step WinPE and the installed Windows run, from the machine's tools. A raw disk image
// and its seed are the Linux boot environment's.
public sealed class WindowsStepRunners : IStepRunners
{
    public required IAgentServer Server { get; init; }

    public required IDiskPartitioner Partitioner { get; init; }

    public required IImageApplier Applier { get; init; }

    public required IToolRunner Tools { get; init; }

    public required IDomainJoiner Joiner { get; init; }

    public required AgentLog Log { get; init; }

    public required TimeProvider TimeProvider { get; init; }

    public required SequenceRunnerOptions Options { get; init; }

    public IReadOnlyList<IStepKindRunner> For(StepRunnerParts run)
    {
        ArgumentNullException.ThrowIfNull(run);

        (RunSession session, FileRunStateStore store, RunHeartbeat heartbeat, RunDownloads downloads, _) = run;

        return
        [
            new PartitionStepRunner(Partitioner, session, store, Log, Options.DryRun),
            new ApplyImageStepRunner(Applier, downloads, session, Log),
            new InjectDriversStepRunner(Tools, downloads, session, Log),
            new WriteUnattendStepRunner(Server, session, heartbeat.ReportNowAsync, Log, TimeProvider),
            new JoinDomainStepRunner(Joiner, Server, session, heartbeat.ReportNowAsync, Log, TimeProvider),
            new RunScriptStepRunner(Tools, new WindowsScriptLauncher(), downloads, session, Log, Options.WorkDirectory),
            new PauseStepRunner(heartbeat, run.Console, Log, TimeProvider),
        ];
    }
}
