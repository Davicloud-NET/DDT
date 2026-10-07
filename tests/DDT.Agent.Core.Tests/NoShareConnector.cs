// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

// For runs whose steps name no share. A step that names one fails, because every system connects shares its own way.
internal sealed class NoShareConnector : IShareConnector
{
    public Task<IConnectedShares> ConnectAsync(IReadOnlyList<AgentShareConnection> shares, IAccountSession? account, CancellationToken cancellationToken) =>
        Task.FromException<IConnectedShares>(new DeploymentStepException("This test's runner connects no shares."));
}
