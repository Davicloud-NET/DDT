// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.RegularExpressions;

namespace DDT.Agent.Deployment;

// How a disk is attached, read from where the kernel put its device, in the names Windows has for the same thing.
public static partial class LinuxBusType
{
    // name is the disk's, such as sda. devicePath is where /sys/block/<name> leads, with the controllers on the way.
    // cardType is what an SD or eMMC card says it is, null for other disks.
    public static StorageBusType Of(string name, string devicePath, string? cardType)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(devicePath);

        string path = devicePath.Replace('\\', '/');

        if (name.StartsWith("nvme", StringComparison.Ordinal))
        {
            return StorageBusType.Nvme;
        }

        if (name.StartsWith("mmcblk", StringComparison.Ordinal))
        {
            return string.Equals(cardType, "SD", StringComparison.OrdinalIgnoreCase) ? StorageBusType.Sd : StorageBusType.Mmc;
        }

        // A USB adapter shows its disk as a SCSI or SATA one further down the same path, so USB is asked first.
        if (Usb().IsMatch(path))
        {
            return StorageBusType.Usb;
        }

        if (name.StartsWith("vd", StringComparison.Ordinal) || name.StartsWith("xvd", StringComparison.Ordinal)
            || path.Contains("/virtio", StringComparison.Ordinal) || path.Contains("/vmbus", StringComparison.OrdinalIgnoreCase))
        {
            return StorageBusType.Virtual;
        }

        return Controller(path);
    }

    private static StorageBusType Controller(string path)
    {
        if (IScsi().IsMatch(path))
        {
            return StorageBusType.IScsi;
        }

        if (Ata().IsMatch(path))
        {
            return StorageBusType.Sata;
        }

        if (path.Contains("/end_device-", StringComparison.Ordinal))
        {
            return StorageBusType.Sas;
        }

        return path.Contains("/rport-", StringComparison.Ordinal) ? StorageBusType.Fibre : StorageBusType.Scsi;
    }

    [GeneratedRegex(@"/usb\d+/")]
    private static partial Regex Usb();

    [GeneratedRegex(@"/session\d+/")]
    private static partial Regex IScsi();

    [GeneratedRegex(@"/ata\d+/")]
    private static partial Regex Ata();
}
