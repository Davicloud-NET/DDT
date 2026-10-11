// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Sequences;

// The runners for the steps of the installed Linux: scripts and pauses. Variables and restarts need none.
public sealed class LinuxStepRunners : IStepRunners
{
    public required IToolRunner Tools { get; init; }

    public required IScriptLauncher Scripts { get; init; }

    public required AgentLog Log { get; init; }

    public required TimeProvider TimeProvider { get; init; }

    public required SequenceRunnerOptions Options { get; init; }

    public IReadOnlyList<IStepKindRunner> For(StepRunnerParts run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return
        [
            new RunScriptStepRunner(Tools, Scripts, run.Downloads, run.Session, Log, Options.WorkDirectory),
            new PauseStepRunner(run.Heartbeat, run.Console, Log, TimeProvider),
        ];
    }
}
