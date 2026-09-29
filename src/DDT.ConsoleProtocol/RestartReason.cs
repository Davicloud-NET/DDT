// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

public enum RestartReason
{
    // A step asked for it, and the run continues after the restart.
    StepAsked,

    // The run continues in the installed Windows.
    HandOver,

    // The run is done.
    RunDone,

    // An earlier start of the agent should have restarted and didn't, so this one restarts before anything else.
    WasDue,
}
