// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

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
