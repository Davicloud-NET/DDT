// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;

namespace DDT.Agent.Sequences;

// What the steps of one run share: the run as the server sent it, the machine's tokens, the disk chosen for the run
// before it started, and that disk's volumes once Partition made them or the agent found them again after a restart. In
// the installed Windows there are no volumes to find: the run goes on in the Windows that runs.
public sealed class RunSession(Guid machineId, AgentRun run, DeploymentTokens tokens)
{
    public Guid MachineId { get; } = machineId;

    public AgentRun Run { get; } = run;

    public DeploymentTokens Tokens { get; } = tokens;

    public LocalDisk? Disk { get; set; }

    public TargetVolumes? Volumes { get; set; }

    // The root of the running Windows, such as C:\, while the run goes on in it.
    public string? RunningWindows { get; set; }

    // <Windows volume>\DDT, which holds the run's files; null until the disk is partitioned.
    public string? RunDirectory => (Volumes?.Windows ?? RunningWindows) is { } windows ? Path.Combine(windows, "DDT") : null;

    public TargetVolumes RequireVolumes() =>
        Volumes ?? throw new DeploymentStepException("This step works on the partitioned disk, but no earlier step partitioned it.");
}
