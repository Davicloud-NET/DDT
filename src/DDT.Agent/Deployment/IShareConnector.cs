// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

public interface IShareConnector
{
    // Connects every share in order, in account's logon session or the agent's own when null. A failure disconnects
    // the shares already connected and names the share, never a password. Disposing disconnects all, never throws.
    Task<IAsyncDisposable> ConnectAsync(IReadOnlyList<AgentShareConnection> shares, IAccountSession? account, CancellationToken cancellationToken);
}
