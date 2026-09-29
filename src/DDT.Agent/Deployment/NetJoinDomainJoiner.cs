// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Joins through NetJoinDomain on this computer, with the join account's credentials. The call blocks while it looks
// for a domain controller, so it runs on the thread pool. Once it has started, it can't be cancelled.
public sealed class NetJoinDomainJoiner : IDomainJoiner
{
    public Task<int> JoinAsync(AgentJoinDomainCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        return Task.Run(
            () => DomainJoinNativeMethods.NetJoinDomain(
                null,
                credentials.Domain,
                string.IsNullOrWhiteSpace(credentials.OrganizationalUnit) ? null : credentials.OrganizationalUnit,
                credentials.UserName,
                credentials.Password,
                DomainJoinNativeMethods.NetSetupJoinDomain | DomainJoinNativeMethods.NetSetupAccountCreate),
            cancellationToken);
    }
}
