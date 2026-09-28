// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

// Stands in for signing an account in and connecting its shares, without a real logon or a real server. One journal
// records sign-in, share connect and disconnect, impersonation and sign-out, in order, so a test can check that shares
// are connected before the step and disconnected after it, even when it fails, and that the run-as account signs out
// again. FailShare and FailLogon make either step fail. Passwords are recorded nowhere.
internal sealed class FakeAccountTools
{
    private readonly List<string> _events = [];

    public FakeShareConnector Shares { get; }

    public FakeAccountLogons Logons { get; }

    public FakeAccountTools()
    {
        Shares = new FakeShareConnector(_events);
        Logons = new FakeAccountLogons(_events);
    }

    public AccountTools Tools => new(Shares, Logons);

    public IReadOnlyList<string> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    internal void Record(string what)
    {
        lock (_events)
        {
            _events.Add(what);
        }
    }
}

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

internal sealed class FakeAccountSession(string userName, FakeAccountLogons logons) : IAccountSession
{
    public string UserName { get; } = userName;

    public Task<T> ImpersonateAsync<T>(Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        logons.Record($"impersonate {UserName}");

        return Task.FromResult(action());
    }

    public void Admit(string directory) => logons.Record($"admit {UserName}");

    public void Dispose() => logons.Record($"sign out {UserName}");
}
