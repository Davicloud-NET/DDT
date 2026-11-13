// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;

namespace DDT.Agent.Applications;

// DDT's session account in a dry run: signed in, with winget set up, and raised in name only. Nobody signs in, and
// DryRunToolRunner runs nothing as it.
public sealed class DryRunDeployAccount(string userName, AgentLog log) : IDeployAccount
{
    private readonly DryRunAccountLogons _logons = new(log);

    public string UserName { get; } = userName;

    public int? SessionId() => 1;

    public string Profile() => $@"C:\Users\{UserName}";

    public bool HasWinget() => true;

    public Task<IAccountSession> InSessionAsync(int sessionId, CancellationToken cancellationToken) => LogOnAsync(cancellationToken);

    public void JoinAdministrators() => log.Information($"Dry run: {UserName} is not added to Administrators.");

    public bool LeaveAdministrators() => true;

    public Task<IAccountSession> LogOnAsBatchAsync(CancellationToken cancellationToken) => LogOnAsync(cancellationToken);

    private Task<IAccountSession> LogOnAsync(CancellationToken cancellationToken) => _logons.LogOnAsync(new AgentAccount(UserName, string.Empty), cancellationToken);
}
