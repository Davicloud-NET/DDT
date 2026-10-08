// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

public interface IRebooter
{
    Task RebootAsync(RestartInto into, CancellationToken cancellationToken);

    // lastLines sends what the server should still get, once all is said and before the machine goes down. A restart
    // that is only issued here, or not carried out, leaves that to the agent's loop.
    Task RebootAsync(RestartInto into, Func<Task> lastLines, CancellationToken cancellationToken) => RebootAsync(into, cancellationToken);
}
