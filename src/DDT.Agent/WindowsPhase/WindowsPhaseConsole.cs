// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Contracts.Agents;

namespace DDT.Agent.WindowsPhase;

// DDT's session and the console in it, where the machine shows the run while the service continues it. A dry run has
// neither, and without an answer file to sign in with there's no session.
internal sealed class WindowsPhaseConsole(IDeploySession? session, ConsoleStatus? status, ConsoleLogo? logo)
{
    public ConsoleStatus? Status => status;

    public Task PrepareAsync(CancellationToken cancellationToken) => session?.PrepareAsync(cancellationToken) ?? Task.CompletedTask;

    public async Task RegisteredAsync(AgentRegistrationResult registration, CancellationToken cancellationToken)
    {
        status?.Registered(registration.MachineId);
        status?.SetLanguage(registration.ConsoleLanguage);

        if (logo is not null)
        {
            await logo.ShowAsync(registration.ConsoleLogoSha256, cancellationToken).ConfigureAwait(false);
        }
    }

    // Only once setup has finished. Until then setup owns the machine's sign-in settings.
    public void SetupFinished() => session?.SetupFinished();

    // False when the stop token ended the wait for someone to sign out.
    public async Task<bool> EndAsync(bool signOut, CancellationToken cancellationToken) =>
        session is null || await session.EndAsync(signOut, cancellationToken).ConfigureAwait(false);
}
