// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

public enum ConsoleRemedy
{
    // Run it again: choose the sequence here once the console asks, or assign it on the Machines page. The agent waits
    // for either.
    RunAgain,

    // The agent has stopped. Restarting the machine from the network starts it over.
    Restart,
}
