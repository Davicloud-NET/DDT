// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// An account signed in for one step. The step's script runs as it, and the step's shares are connected in its logon
// session. It keeps no password, because that went to the sign-in and nowhere else. Disposing it signs the account out
// again.
public interface IAccountSession : IDisposable
{
    // As the server sent it, DOMAIN\user or a UPN.
    string UserName { get; }

    // Runs action on a separate thread that acts as the account, so what it connects belongs to the account's logon
    // session and not to the agent's.
    Task<T> ImpersonateAsync<T>(Func<T> action);

    // Lets this logon session read, change and run what's in directory, such as the step's script and package.
    // Otherwise only SYSTEM may open it.
    void Admit(string directory);
}
