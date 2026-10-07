// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;

namespace DDT.Agent.Tests;

// The runners of the steps that need nothing of one system: a raw image, its seed and a pause.
internal sealed class TestStepRunners(IDisks disks, IRawDisks rawDisks, AgentLog log, TimeProvider timeProvider) : IStepRunners
{
    public IReadOnlyList<IStepKindRunner> For(StepRunnerParts run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return
        [
            new WriteRawImageStepRunner(disks, rawDisks, run.Downloads, run.Session, log),
            new WriteCloudInitSeedStepRunner(rawDisks, run.Session, log, timeProvider),
            new PauseStepRunner(run.Heartbeat, run.Console, log, timeProvider),
        ];
    }
}
