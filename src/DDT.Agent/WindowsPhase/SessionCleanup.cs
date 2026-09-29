// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Principal;
using DDT.Agent.Sequences;

namespace DDT.Agent.WindowsPhase;

// Cleans up after DDT's session ends: its sign-ins, its profile, and the account setup left behind. A part that fails
// only leaves something behind, so it's logged and never ends the run.
internal sealed class SessionCleanup(ISessionAccounts accounts, AgentLog log, TimeProvider timeProvider)
{
    // False when the stop token ended the wait for someone to sign out.
    public async Task<bool> EndSessionsAsync(bool signOut, CancellationToken cancellationToken)
    {
        IReadOnlyList<int> sessions;

        try
        {
            sessions = accounts.Sessions(DeploySession.AccountName);
        }
        catch (Exception exception)
        {
            log.Warning($"The sessions of {DeploySession.AccountName} could not be listed ({LogText.OneLine(exception)}).");

            return true;
        }

        if (sessions.Count == 0)
        {
            return true;
        }

        try
        {
            if (signOut)
            {
                await Task.Delay(DeploySession.SignOutGrace, timeProvider, cancellationToken).ConfigureAwait(false);

                foreach (int session in sessions)
                {
                    Attempt($"sign out session {session}", () => accounts.SignOut(session));
                }

                return true;
            }

            log.Information("The console shows how the run ended until someone at the machine signs out with F9.");

            while (accounts.Sessions(DeploySession.AccountName).Count > 0)
            {
                await Task.Delay(DeploySession.SignOutPollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    // Setup deletes its first user, defaultuser0, at the end of that user's part of the out-of-box experience. The
    // session's console keeps setup from getting there, so the sign-in screen offers the account. It's deleted here
    // unless someone uses it.
    public async Task DeleteSetupUserAsync()
    {
        const string name = RegistrySetupProbe.SetupUser;

        try
        {
            if (!accounts.Exists(name) || accounts.Sessions(name).Count > 0)
            {
                return;
            }

            await DeleteProfileAsync(accounts.Sid(name), name).ConfigureAwait(false);
            accounts.Delete(name);
            log.Information($"Deleted {name}, the temporary account Windows setup left behind.");
        }
        catch (Exception exception)
        {
            log.Warning($"Could not delete {name}, the temporary account Windows setup left behind ({LogText.OneLine(exception)}).");
        }
    }

    // A profile stays in use for a moment after its session ended.
    public async Task DeleteProfileAsync(SecurityIdentifier sid, string name)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                accounts.DeleteProfile(sid);

                return;
            }
            catch (Exception exception) when (attempt < DeploySession.ProfileAttempts)
            {
                log.Information($"The profile of {name} is still in use ({LogText.OneLine(exception)}). Trying again.");
                await Task.Delay(DeploySession.ProfileRetryInterval, timeProvider, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                log.Warning($"The profile of {name} could not be deleted ({LogText.OneLine(exception)}). It stays in C:\\Users.");

                return;
            }
        }
    }

    public void Attempt(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            log.Warning($"Could not {what} ({LogText.OneLine(exception)}).");
        }
    }
}
