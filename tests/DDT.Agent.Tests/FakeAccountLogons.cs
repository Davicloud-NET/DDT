// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

internal sealed class FakeAccountLogons(List<string> events) : IAccountLogons
{
    public Exception? Failure { get; set; }

    public Task<IAccountSession> LogOnAsync(AgentAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (Failure is not null)
        {
            Record($"sign-in failed {account.UserName}");

            return Task.FromException<IAccountSession>(Failure);
        }

        Record($"sign in {account.UserName}");

        return Task.FromResult<IAccountSession>(new FakeAccountSession(account.UserName, this));
    }

    internal void Record(string what)
    {
        lock (events)
        {
            events.Add(what);
        }
    }
}
