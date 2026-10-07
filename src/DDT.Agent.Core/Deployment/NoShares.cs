// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// For a boot environment that connects no shares. The validator refuses a share there, so this only fails a step that
// names one anyway.
public sealed class NoShares : IShareConnector
{
    public const string Message = "This boot environment connects no shares, so a step that names one cannot run in it.";

    public Task<IConnectedShares> ConnectAsync(IReadOnlyList<AgentShareConnection> shares, IAccountSession? account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shares);

        return shares.Count == 0
            ? Task.FromResult<IConnectedShares>(Nothing.Instance)
            : Task.FromException<IConnectedShares>(new DeploymentStepException(Message));
    }

    private sealed class Nothing : IConnectedShares
    {
        public static readonly Nothing Instance = new();

        public IReadOnlyList<string> LocalPaths => [];

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
