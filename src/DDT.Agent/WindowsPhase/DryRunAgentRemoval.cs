// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;

namespace DDT.Agent.WindowsPhase;

// A dry run's removal: the agent's own, whose tools and deletions at the next start only log, then the dry run's root,
// which stood in for the machine's disks and held the run until it was over.
public sealed class DryRunAgentRemoval(IAgentRemoval removal, string root, AgentLog log) : IAgentRemoval
{
    public async Task RemoveAsync(CancellationToken cancellationToken)
    {
        await removal.RemoveAsync(cancellationToken).ConfigureAwait(false);
        Leftovers.Delete(root, log);
    }
}
