// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Signs nobody in. A dry run only names the account, and DryRunToolRunner runs nothing as it.
public sealed class DryRunAccountLogons(AgentLog log) : IAccountLogons
{
    public Task<IAccountSession> LogOnAsync(AgentAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        log.Information($"Dry run: {account.UserName} is not signed in.");

        return Task.FromResult<IAccountSession>(new Session(account.UserName));
    }

    private sealed class Session(string userName) : IAccountSession
    {
        public string UserName { get; } = userName;

        public Task<T> ImpersonateAsync<T>(Func<T> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            return Task.FromResult(action());
        }

        // A dry run leaves the run's directory open to everyone.
        public void Admit(string directory)
        {
        }

        public void Dispose()
        {
        }
    }
}
