// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;

namespace DDT.Contracts.Agents;

// A run as the agent receives it: the sequence frozen when it was assigned and the files its steps use, never
// secrets. State is Assigned or Running. DiskNumber is the disk chosen at the machine; a run assigned on the web has
// none. ComputerName is the name assigned to the machine, which conditions can test.
public sealed record AgentRun(
    Guid Id,
    DeploymentState State,
    string SequenceName,
    SequenceDefinition Sequence,
    IReadOnlyList<AgentRunImage> Images,
    IReadOnlyList<AgentRunPackage> Packages,
    int? DiskNumber,
    string? ComputerName);
