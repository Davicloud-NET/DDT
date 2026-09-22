// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Where a restart is meant to lead: back into Windows PE from the network, for the next step of a run whose state waits
// on the disk, or into the Windows on the disk.
public enum RestartInto
{
    WindowsPE,
    Windows,
}
