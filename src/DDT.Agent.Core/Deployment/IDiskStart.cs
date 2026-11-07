// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// How a boot environment sends the machine back to a disk whose installed system has a run, or is to take one.
public interface IDiskStart
{
    // Makes the firmware start the disk next, whatever its boot order says. first also puts the disk first in that
    // order again. systemPartition is null for a disk without one. Never throws. A failure is only a warning.
    void Next(LocalDisk disk, EspPartition? systemPartition, bool first);
}
