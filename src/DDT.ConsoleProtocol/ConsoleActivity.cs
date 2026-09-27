// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// What the agent does during a run, so the console can say why no step is running.
public enum ConsoleActivity
{
    // Checking the run before anything on the disk changes.
    Preparing,

    // A step runs.
    Step,

    // Handing the run over to the installed Windows and making the disk bootable.
    HandingOver,

    Restarting,

    // In the installed Windows only, where no console runs: waiting for Windows setup to finish.
    WaitingForWindowsSetup,

    // Making the disk bootable and sending the last reports.
    Finishing,

    // In the installed Windows only: the agent removes itself.
    Removing,
}
