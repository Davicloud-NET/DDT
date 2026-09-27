// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

// DDT's session in the installed Windows, which shows the run on DDT's console, as WindowsPhaseLoop uses it.
public interface IDeploySession
{
    // At every start of the service, before the run goes on. Never fails the run: without a session, the machine shows
    // Windows' own screens and the run goes on all the same.
    Task PrepareAsync(CancellationToken cancellationToken);

    // Once Windows setup has finished, its first user included, and before the next step.
    void SetupFinished();

    // Once the run is over here. signOut ends the session at once; otherwise the console shows how the run ended until
    // someone at the machine signs out. False when the stop token ended that wait, which the next start takes up again.
    Task<bool> EndAsync(bool signOut, CancellationToken cancellationToken);
}
