// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Agents;

// What the agent does between steps, so the page can say why no step is running.
public enum RunActivity
{
    Preparing,
    Step,
    HandingOver,
    Restarting,
    WaitingForWindowsSetup,
    Finishing,
    Removing,
}
