// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Joins nothing and answers as a join that worked. The step fetched the account from the server all the same, which
// tries the server's side; neither the account nor its password reaches the log.
public sealed class DryRunDomainJoiner(AgentLog log) : IDomainJoiner
{
    public Task<int> JoinAsync(AgentJoinDomainCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        log.Information($"Dry run: this computer does not join {credentials.Domain}. In Windows, NetJoinDomain would join it with the account the server sent.");

        return Task.FromResult(0);
    }
}
