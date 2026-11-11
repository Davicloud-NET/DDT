// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Applications;

// DDT's session account raised to an administrator for one install. Disposing it signs the batch logon out and takes
// the account out of Administrators, however the install ended. The service's next start does that too, after a power
// loss.
public sealed class DeployAccountElevation : IDisposable
{
    private readonly IDeployAccount _account;
    private readonly AgentLog _log;
    private bool _disposed;

    private DeployAccountElevation(IDeployAccount account, IAccountSession session, AgentLog log)
    {
        _account = account;
        Session = session;
        _log = log;
    }

    // Starts a process with the full token.
    public IAccountSession Session { get; }

    public static async Task<DeployAccountElevation> RaiseAsync(IDeployAccount account, AgentLog log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(log);

        account.JoinAdministrators();

        try
        {
            IAccountSession session = await account.LogOnAsBatchAsync(cancellationToken).ConfigureAwait(false);
            log.Information($"{account.UserName} is an administrator for this install.");

            return new DeployAccountElevation(account, session, log);
        }
        catch
        {
            Lower(account, log);

            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            Session.Dispose();
        }
        finally
        {
            Lower(_account, _log);
        }
    }

    private static void Lower(IDeployAccount account, AgentLog log)
    {
        try
        {
            _ = account.LeaveAdministrators();
            log.Information($"{account.UserName} is a standard account again.");
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"{exception.Message} The agent tries again when it starts next.");
        }
    }
}
