// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

public interface IDomainJoiner
{
    // Joins this computer to the domain with the account in credentials, and creates its computer account there when
    // the domain has none. Returns what Windows answered: 0 once it joined, otherwise the error code.
    Task<int> JoinAsync(AgentJoinDomainCredentials credentials, CancellationToken cancellationToken);
}
