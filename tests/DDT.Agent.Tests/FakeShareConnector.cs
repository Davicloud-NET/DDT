// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

internal sealed class FakeShareConnector(List<string> events) : IShareConnector
{
    private readonly List<string> _connected = [];

    // The remote name whose connection throws, such as \\files\drivers.
    public string? FailPath { get; set; }

    public Exception Failure { get; set; } = new DeploymentStepException("The scripted share failed.");

    // The remotes connected right now, so a step can look while it runs.
    public IReadOnlyList<string> Connected
    {
        get
        {
            lock (_connected)
            {
                return [.. _connected];
            }
        }
    }

    public Task<IAsyncDisposable> ConnectAsync(IReadOnlyList<AgentShareConnection> shares, IAccountSession? account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(shares);
        string who = account is null ? "the agent" : account.UserName;

        foreach (AgentShareConnection share in shares)
        {
            string remote = ShareConnector.RemoteName(share.Path);

            if (remote == FailPath)
            {
                Disconnect();
                Record($"share failed {remote}");

                return Task.FromException<IAsyncDisposable>(Failure);
            }

            lock (_connected)
            {
                _connected.Add(remote);
            }

            Record($"connect {remote} as {share.UserName} for {who}");
        }

        return Task.FromResult<IAsyncDisposable>(new Handle(this));
    }

    private void Disconnect()
    {
        List<string> remotes;

        lock (_connected)
        {
            remotes = [.. _connected];
            _connected.Clear();
        }

        for (int index = remotes.Count - 1; index >= 0; index--)
        {
            Record($"disconnect {remotes[index]}");
        }
    }

    private void Record(string what)
    {
        lock (events)
        {
            events.Add(what);
        }
    }

    private sealed class Handle(FakeShareConnector connector) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            connector.Disconnect();

            return ValueTask.CompletedTask;
        }
    }
}
