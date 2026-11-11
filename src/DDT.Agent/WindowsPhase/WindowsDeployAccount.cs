// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Runtime.Versioning;
using DDT.Agent.Applications;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using static DDT.Agent.Deployment.AccountNativeMethods;

namespace DDT.Agent.WindowsPhase;

// DDTDeploy on the Windows the service runs in. Its password is the one DDT's session keeps for the auto-logon.
[SupportedOSPlatform("windows")]
public sealed class WindowsDeployAccount(DeploySession session, ISessionAccounts accounts, AgentLog log) : IDeployAccount
{
    public string UserName => DeploySession.AccountName;

    public int? SessionId()
    {
        try
        {
            return accounts.Exists(UserName) && accounts.Sessions(UserName) is [int first, ..] ? first : null;
        }
        catch (Win32Exception exception)
        {
            throw new DeploymentStepException($"The sessions of {UserName} could not be listed ({exception.Message}).", exception);
        }
    }

    public string Profile() => accounts.CreateProfile(accounts.Sid(UserName), UserName);

    public bool HasWinget() => File.Exists(WingetApplicationInstaller.AliasIn(Profile()));

    public Task<IAccountSession> InSessionAsync(int sessionId, CancellationToken cancellationToken) =>
        Task.FromResult<IAccountSession>(WindowsAccountSession.InSession(sessionId, UserName));

    public void JoinAdministrators() => Change(() => accounts.JoinAdministrators(UserName));

    public bool LeaveAdministrators()
    {
        bool left = false;
        Change(() => left = accounts.LeaveAdministrators(UserName));

        return left;
    }

    public Task<IAccountSession> LogOnAsBatchAsync(CancellationToken cancellationToken)
    {
        string password = Password() ?? throw new DeploymentStepException($"DDT's session does not know the password of {UserName}.");

        return DedicatedThread.RunAsync<IAccountSession>(
            () => WindowsAccountSession.LogOn(new AgentAccount(UserName, password), log, LogonBatch),
            "DDT sign-in");
    }

    private string? Password()
    {
        try
        {
            return session.Password();
        }
        catch (Win32Exception exception)
        {
            throw new DeploymentStepException($"The password of {UserName} could not be read ({exception.Message}).", exception);
        }
    }

    private static void Change(Action change)
    {
        try
        {
            change();
        }
        catch (Win32Exception exception)
        {
            throw new DeploymentStepException(exception.Message, exception);
        }
    }
}
