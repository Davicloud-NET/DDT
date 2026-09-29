// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

// DDT's session in the installed Windows, which shows the run on DDT's console. WindowsPhaseLoop uses it.
public interface IDeploySession
{
    // Called at every start of the service, before the run continues. Never fails the run. Without a session, the
    // machine shows Windows' own screens and the run still continues.
    Task PrepareAsync(CancellationToken cancellationToken);

    // Called once Windows setup has finished, including its first user, and before the next step.
    void SetupFinished();

    // Called once the run is over here. signOut ends the session right away. Otherwise the console shows how the run
    // ended until someone at the machine signs out. Returns false when the stop token ended that wait, and the next
    // start picks it up again.
    Task<bool> EndAsync(bool signOut, CancellationToken cancellationToken);
}
