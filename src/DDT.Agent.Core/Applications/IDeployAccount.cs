// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Applications;

// The standard account of DDT's session in the installed Windows, which winget runs as. Failures throw
// DeploymentStepException.
public interface IDeployAccount
{
    string UserName { get; }

    // The session the account is signed in to, or null.
    int? SessionId();

    string Profile();

    // Whether winget's app execution alias is in the profile, which App Installer's registration puts there.
    bool HasWinget();

    // The account's own token in its session, on that session's desktop.
    Task<IAccountSession> InSessionAsync(int sessionId, CancellationToken cancellationToken);

    // By the SID S-1-5-32-544, whatever the group is called in this language.
    void JoinAdministrators();

    // False when it was no member.
    bool LeaveAdministrators();

    // A batch sign-in, which only an administrator may make. It gives one token with every right.
    Task<IAccountSession> LogOnAsBatchAsync(CancellationToken cancellationToken);
}
