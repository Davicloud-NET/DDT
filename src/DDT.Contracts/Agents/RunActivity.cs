// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// What the agent does between steps, so the page can say why no step is running. New values go at the end. Only the
// agent writes them, and only a server at least as new as the agent reads them.
public enum RunActivity
{
    Preparing,
    Step,
    HandingOver,
    Restarting,
    WaitingForWindowsSetup,
    Finishing,
    Removing,

    // The run waits to start until its inputs are answered.
    WaitingForInput,

    // A Pause step waits for someone to continue the run.
    Paused,
}
