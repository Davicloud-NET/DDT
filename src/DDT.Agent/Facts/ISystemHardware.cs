// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Facts;

// What the system counts of the machine's memory and processors. Each member is null when it cannot tell.
public interface ISystemHardware
{
    // The memory installed, in kilobytes, as the firmware's SMBIOS memory devices add up. Some virtual machines and
    // boards list none that the system can use.
    ulong? InstalledMemoryKilobytes();

    // The physical memory the system can use, in bytes, a little less than is installed.
    ulong? UsableMemoryBytes();

    // The cores and logical processors of the whole machine, not only those this process may run on.
    ProcessorCount? ProcessorCores();
}
