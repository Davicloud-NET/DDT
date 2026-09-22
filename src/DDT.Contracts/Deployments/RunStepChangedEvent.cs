// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Deployments;

// Sent to the connections that watch the machine when a step of its run changes.
public sealed record RunStepChangedEvent(Guid MachineId, Guid DeploymentId, DeploymentStepView Step);
