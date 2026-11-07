// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Which disks DDT may install on in Linux: the rules of Windows PE, and what Linux reports otherwise than Windows.
public static class LinuxDiskEligibility
{
    // Windows calls a card in a built-in reader removable media. Linux does not, so the card is told by its kind.
    public const string SdCardReason = "it is an SD card, which Linux does not mark as removable";

    public const string ReadOnlyReason = "the kernel has it read-only";

    // Why a disk is left out, or null when DDT may install on it. A built-in eMMC stays in, as in Windows PE.
    public static string? ExclusionReason(bool removableMedia, bool readOnly, StorageBusType busType, long sizeBytes)
    {
        if (!removableMedia && busType == StorageBusType.Sd)
        {
            return SdCardReason;
        }

        return DiskEligibility.ExclusionReason(removableMedia, busType, sizeBytes) ?? (readOnly ? ReadOnlyReason : null);
    }
}
