// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Facts;

// What Windows counts of the machine's memory and processors. Each member is null when Windows cannot tell.
public interface ISystemHardware
{
    // The memory installed, in kilobytes, as the firmware's SMBIOS memory devices add up. Some virtual machines and
    // boards list none that Windows can use.
    ulong? InstalledMemoryKilobytes();

    // The physical memory Windows can use, in bytes, a little less than is installed.
    ulong? UsableMemoryBytes();

    // What GetLogicalProcessorInformationEx writes for RelationProcessorCore: one record per core.
    byte[]? ProcessorCores();
}
