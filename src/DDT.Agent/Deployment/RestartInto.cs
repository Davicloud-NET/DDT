// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Where a restart should lead. Either back into WinPE from the network, for the next step of a run whose state waits
// on disk, or into the Windows on the disk.
public enum RestartInto
{
    WindowsPE,
    Windows,
}
