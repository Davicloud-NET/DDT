// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

public interface IShareConnector
{
    // Connects every share, in order, in the logon session of account, or in the agent's own when it is null. When one
    // fails, those already connected are disconnected again and DeploymentStepException names the share and why, never
    // with a password. Disposing the result disconnects them all and never throws.
    Task<IAsyncDisposable> ConnectAsync(IReadOnlyList<AgentShareConnection> shares, IAccountSession? account, CancellationToken cancellationToken);
}
