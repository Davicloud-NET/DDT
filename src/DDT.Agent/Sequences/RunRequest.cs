// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;

namespace DDT.Agent.Sequences;

// Resumed is the run's state found on the disk after a restart. ConfirmedDisk is the disk the technician confirmed
// with ERASE in this process: disk numbers can change at a restart, so a run chosen at the machine erases only that one.
public sealed record RunRequest(
    Guid MachineId,
    AgentRun Run,
    LocalRun? Resumed,
    LocalDisk? ConfirmedDisk,
    DeploymentTokens Tokens,
    MachineIdentity Identity);
