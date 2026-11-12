// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.InstalledPhase;

// Windows' Automatic Repair, which after failed starts waits for a key. Off while the run goes on, as it was once the
// run is over. A failure is a warning, never the run's end.
public interface IAutomaticRepair
{
    // Called at every start of the service. Only the first keeps what was set before.
    Task TurnOffAsync(CancellationToken cancellationToken);

    // Called once the run is over here, before the agent leaves.
    Task PutBackAsync(CancellationToken cancellationToken);
}
