// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// What a disk calls itself, as the kernel shows it under /sys/block.
public static class LinuxDiskModel
{
    // SCSI and SATA disks have a vendor and a model, NVMe disks a model, cards a name. Other disks have none of them:
    // what a virtio disk calls its vendor is a number. name is the disk, block its folder in /sys/block.
    public static string? Of(string name, string block)
    {
        ArgumentNullException.ThrowIfNull(name);

        string device = Path.Combine(block, "device");
        string? vendor = name.StartsWith("sd", StringComparison.Ordinal) ? SysfsText.Read(Path.Combine(device, "vendor")) : null;
        string?[] parts = [vendor, SysfsText.Read(Path.Combine(device, "model")) ?? SysfsText.Read(Path.Combine(device, "name"))];
        string model = string.Join(' ', parts.Where(part => part is not null));

        return model.Length == 0 ? null : model;
    }
}
