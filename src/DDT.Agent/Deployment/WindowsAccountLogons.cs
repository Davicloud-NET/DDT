// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.Versioning;
using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Signs an account in interactively, with its profile loaded. A domain account needs that for its own network access.
// The sign-in may wait for a domain controller, so it runs on a separate thread.
[SupportedOSPlatform("windows")]
public sealed class WindowsAccountLogons(AgentLog log) : IAccountLogons
{
    public Task<IAccountSession> LogOnAsync(AgentAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        return DedicatedThread.RunAsync<IAccountSession>(() => WindowsAccountSession.LogOn(account, log), "DDT sign-in");
    }
}
