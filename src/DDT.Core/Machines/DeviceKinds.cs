// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Machines;

namespace DDT.Core.Machines;

// Tells what kind of computer a machine is from what its firmware reports: the manufacturer and model of the SMBIOS System
// Information structure and the chassis type of its System Enclosure structure, numbered as in DMTF DSP0134, table 17.
public static class DeviceKinds
{
    // Hypervisors mostly report a desktop chassis, or Other, so their names decide first. Hyper-V's model is "Virtual
    // Machine", VirtualBox's manufacturer "innotek GmbH", and KVM and Xen guests name the hypervisor in one of the two.
    private static readonly string[] s_virtualMarkers =
        ["Virtual Machine", "VMware", "VirtualBox", "QEMU", "KVM", "Xen", "Parallels", "Bochs", "innotek"];

    public static DeviceKind Classify(string? manufacturer, string? model, int? chassisType)
    {
        if (IsVirtual(manufacturer) || IsVirtual(model))
        {
            return DeviceKind.Virtual;
        }

        return chassisType switch
        {
            // Portable, Laptop, Notebook, Sub Notebook, Convertible and Detachable. A convertible or detachable has a
            // keyboard, and is used as a laptop more often than as a tablet.
            8 or 9 or 10 or 14 or 31 or 32 => DeviceKind.Laptop,
            30 => DeviceKind.Tablet,

            // Desktop, Low Profile Desktop, Pizza Box, Mini Tower, Tower, All in One, Space-saving, Lunch Box, Mini PC
            // and Stick PC.
            3 or 4 or 5 or 6 or 7 or 13 or 15 or 16 or 35 or 36 => DeviceKind.Desktop,

            // Main Server Chassis, Rack Mount Chassis, Multi-system chassis, Blade and Blade Enclosure.
            17 or 23 or 25 or 28 or 29 => DeviceKind.Server,

            // Other, Unknown and the kinds that say nothing about the computer, such as a docking station or an
            // expansion chassis, and no chassis type at all.
            _ => DeviceKind.Unknown,
        };
    }

    private static bool IsVirtual(string? value) =>
        value is not null && s_virtualMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
