// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Logs each share instead of connecting it, so a dry run reaches no server with the step's accounts.
public sealed class DryRunShareConnector(AgentLog log) : IShareConnector
{
    public Task<IAsyncDisposable> ConnectAsync(
        IReadOnlyList<AgentShareConnection> shares,
        IAccountSession? account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shares);

        foreach (AgentShareConnection share in shares)
        {
            string where = account is null ? string.Empty : $", for {account.UserName}";
            log.Information($"Dry run: not connected: {ShareConnector.RemoteName(share.Path)} as {share.UserName}{where}.");
        }

        return Task.FromResult<IAsyncDisposable>(Nothing.Instance);
    }

    private sealed class Nothing : IAsyncDisposable
    {
        public static readonly Nothing Instance = new();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
