// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

public interface IAgentRemoval
{
    // Removes the agent from the installed Windows once its run is over there.
    Task RemoveAsync(CancellationToken cancellationToken);
}
