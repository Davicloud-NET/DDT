// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Deployments;

// Sent to the connections that watch the machine when the variables its run's agent reported changed: all of them, as
// DeploymentView.Variables has them.
public sealed record RunVariablesChangedEvent(Guid MachineId, Guid DeploymentId, IReadOnlyDictionary<string, string> Variables);
