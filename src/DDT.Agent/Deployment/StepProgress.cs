// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;

namespace DDT.Agent.Deployment;

// Reported synchronously, unlike Progress<T>, so the heartbeat never sends a percent older than the last one.
internal sealed class StepProgress(DeploymentHeartbeat heartbeat, DeploymentStep step) : IProgress<int>
{
    public void Report(int value) => heartbeat.Progress(step, value);
}
