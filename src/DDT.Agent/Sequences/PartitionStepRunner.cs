// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Erases the disk chosen for the run and partitions it with the step's sizes. The outputs are the new partitions' ids,
// which find them again after a restart, and from here on the run has its directory on the Windows volume. A dry run
// leaves that directory open: closed to all but SYSTEM, it would lock the person running it out of their own folder.
public sealed class PartitionStepRunner(IDiskPartitioner partitioner, RunSession session, AgentLog log, bool dryRun)
{
    public async Task<StepResult> RunAsync(PartitionStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        LocalDisk disk = session.Disk
            ?? throw new DeploymentStepException("No disk was chosen for this run, so nothing was partitioned.");

        TargetVolumes volumes = await partitioner
            .PartitionAsync(disk, step.SystemPartitionMegabytes, step.RecoveryPartitionMegabytes, cancellationToken)
            .ConfigureAwait(false);

        session.Volumes = volumes;
        string directory = session.RunDirectory!;

        if (dryRun)
        {
            Directory.CreateDirectory(directory);
            log.Information($"Dry run: {directory} is left open. In Windows PE only SYSTEM could open it ({SystemOnlyDirectory.Sddl}).");
        }
        else
        {
            SystemOnlyDirectory.Create(directory);
        }

        context.Progress.Report(100);

        return StepResult.Done(RunVariables.Of(volumes));
    }
}
