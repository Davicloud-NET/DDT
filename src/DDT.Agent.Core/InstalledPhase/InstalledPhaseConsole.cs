// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Security;
using DDT.Agent.Consoles;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent.InstalledPhase;

// DDT's session and the console in it, where the machine shows the run. A dry run has neither. Once the session of a
// finished run is gone, Winlogon is free for the first-logon settings the run fetched.
internal sealed class InstalledPhaseConsole(IDeploySession? session, ConsoleStatus? status, ConsoleLogo? logo, IFirstLogon? firstLogon, AgentLog log)
{
    // Only in memory: it holds a password.
    private AgentFirstLogon? _firstLogon;

    public ConsoleStatus? Status => status;

    public bool KeepsFirstLogon => _firstLogon is not null;

    public Task PrepareAsync(CancellationToken cancellationToken) => session?.PrepareAsync(cancellationToken) ?? Task.CompletedTask;

    public async Task RegisteredAsync(AgentRegistrationResult registration, CancellationToken cancellationToken)
    {
        status?.Registered(registration.MachineId);
        status?.SetLanguage(registration.ConsoleLanguage);
        status?.SetKeyboardLayout(registration.ConsoleKeyboardLayout);

        if (logo is not null)
        {
            await logo.ShowAsync(registration.ConsoleLogoSha256, cancellationToken).ConfigureAwait(false);
        }
    }

    // Only once setup has finished. Until then setup owns the machine's sign-in settings.
    public void SetupFinished() => session?.SetupFinished();

    public void KeepFirstLogon(AgentFirstLogon read) => _firstLogon = read;

    // A run that did not end done sets nothing up.
    public void DropFirstLogon() => _firstLogon = null;

    // False when the stop token ended the wait for someone to sign out. Then nothing is set up.
    public async Task<bool> EndAsync(bool signOut, CancellationToken cancellationToken)
    {
        if (session is not null && !await session.EndAsync(signOut, cancellationToken).ConfigureAwait(false))
        {
            if (_firstLogon is not null)
            {
                log.Warning("The first-logon commands and the automatic logon are not set up, because the agent was stopped first.");
                _firstLogon = null;
            }

            return false;
        }

        SetUpFirstLogon();

        return true;
    }

    // A failure here is a warning: the run is done.
    private void SetUpFirstLogon()
    {
        if (_firstLogon is not { } read || firstLogon is null)
        {
            return;
        }

        _firstLogon = null;

        try
        {
            firstLogon.SetUp(read);
            log.Information($"Set up {FirstLogonTexts.Summary(read)}, from the next start of Windows.");
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or SecurityException)
        {
            log.Warning($"The first-logon commands and the automatic logon could not be set up ({LogText.OneLine(exception)}). Windows starts without them.");
        }
    }
}
