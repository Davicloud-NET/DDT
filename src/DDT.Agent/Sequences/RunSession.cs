// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Images;

namespace DDT.Agent.Sequences;

// What the steps of one run share: the run, the machine's tokens, the disk chosen before it started, and its volumes once
// Partition made them or a restart found them again. In the installed Windows the run goes on in the running one.
public sealed class RunSession(Guid machineId, AgentRun run, DeploymentTokens tokens)
{
    public Guid MachineId { get; } = machineId;

    public AgentRun Run { get; } = run;

    public DeploymentTokens Tokens { get; } = tokens;

    public LocalDisk? Disk { get; set; }

    // What the firmware said at the start of the run.
    public bool? SecureBootEnabled { get; init; }

    // Which of Microsoft's third-party UEFI CAs the firmware trusts, null when that is not known.
    public UefiCa? TrustedUefiCas { get; init; }

    public TargetVolumes? Volumes { get; set; }

    // The root of the running Windows, such as C:\, while the run goes on in it.
    public string? RunningWindows { get; set; }

    // <Windows volume>\DDT, which holds the run's files; null until the disk is partitioned.
    public string? RunDirectory => (Volumes?.Windows ?? RunningWindows) is { } windows ? Path.Combine(windows, "DDT") : null;

    public TargetVolumes RequireVolumes() =>
        Volumes ?? throw new DeploymentStepException("This step works on the partitioned disk, but no earlier step partitioned it.");
}
