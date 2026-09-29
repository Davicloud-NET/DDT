// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Sequences;

// Erases the run's disk and partitions it. The new partitions' ids find them again after a restart. From here on the
// store writes the run's state and token into the run's directory on the Windows volume.
public sealed class PartitionStepRunner(IDiskPartitioner partitioner, RunSession session, FileRunStateStore store, AgentLog log, bool dryRun)
    : IStepKindRunner<PartitionStep>
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

        // If only SYSTEM could open it, the person running a dry run would be locked out of their own folder.
        if (dryRun)
        {
            Directory.CreateDirectory(directory);
            log.Information($"Dry run: {directory} is left open. In Windows PE only SYSTEM could open it ({SystemOnlyDirectory.Sddl}).");
        }
        else
        {
            SystemOnlyDirectory.Create(directory);
        }

        // Before the step ends, because its Done is the first state that has to survive a restart. Like the engine's
        // saves, this runs without the stop token. A stop must not leave the disk erased and the run without its files.
        await store.AttachAsync(new RunFiles(directory, log), CancellationToken.None).ConfigureAwait(false);
        context.Progress.Report(100);

        return StepResult.Done(RunVariables.Of(volumes));
    }
}
