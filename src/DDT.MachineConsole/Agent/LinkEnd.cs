// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.Agent;

// Why the pipe to the agent ended.
public enum LinkEnd
{
    // The agent closed it, as it does when it ends.
    Closed,

    // It broke, or the agent sent what is not a message of the protocol.
    Broken,
}
