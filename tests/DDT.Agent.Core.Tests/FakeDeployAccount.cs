// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// DDTDeploy signed in to session 2, with what happens to it recorded in Events. The alias shows up when the
// registration's tool call says so through Register.
internal sealed class FakeDeployAccount(List<string> events) : IDeployAccount
{
    public string UserName => "DDTDeploy";

    public int? Session { get; set; } = 2;

    public bool HasAlias { get; set; } = true;

    public bool IsAdministrator { get; private set; }

    public Exception? LogOnFailure { get; set; }

    public Exception? LeaveFailure { get; set; }

    public int? SessionId() => Session;

    public string Profile() => @"C:\Users\DDTDeploy";

    public bool HasWinget() => HasAlias;

    public Task<IAccountSession> InSessionAsync(int sessionId, CancellationToken cancellationToken)
    {
        events.Add($"session token {sessionId}");

        return Task.FromResult<IAccountSession>(new Token($"session {sessionId}", events));
    }

    public void JoinAdministrators()
    {
        events.Add("join Administrators");
        IsAdministrator = true;
    }

    public bool LeaveAdministrators()
    {
        events.Add("leave Administrators");

        if (LeaveFailure is { } failure)
        {
            throw failure;
        }

        bool was = IsAdministrator;
        IsAdministrator = false;

        return was;
    }

    public Task<IAccountSession> LogOnAsBatchAsync(CancellationToken cancellationToken)
    {
        events.Add("batch logon");

        return LogOnFailure is { } failure
            ? Task.FromException<IAccountSession>(failure)
            : Task.FromResult<IAccountSession>(new Token("batch", events));
    }

    private sealed class Token(string name, List<string> events) : IAccountSession
    {
        public string UserName => "DDTDeploy";

        public Task<T> ImpersonateAsync<T>(Func<T> action) => throw new NotSupportedException();

        public void Admit(string directory) => throw new NotSupportedException();

        public void Dispose() => events.Add($"sign out {name}");
    }
}
