// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// An account signed in for one step, which the step's script runs as and whose logon session its shares are connected
// in. It keeps no password: that went to the sign-in and nowhere else. Disposing it signs the account out again.
public interface IAccountSession : IDisposable
{
    // As the server sent it, DOMAIN\user or a UPN.
    string UserName { get; }

    // Runs action on a thread of its own that acts as the account, so what it connects belongs to the account's logon
    // session and not to the agent's.
    Task<T> ImpersonateAsync<T>(Func<T> action);

    // Lets this logon session read, change and run what is in directory, which is otherwise open to SYSTEM alone: the
    // step's script and package.
    void Admit(string directory);
}
