// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// The files shim and GRUB ask for at every start without needing them. DDT has none, and says so only at Debug level.
public static class BootProbes
{
    private const string GrubModules = "grub/x86_64-efi/";

    // name has forward slashes and no leading one.
    public static bool IsProbe(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // GRUB reads its command, file system, crypto and terminal lists from its prefix.
        if (name.StartsWith(GrubModules, StringComparison.Ordinal))
        {
            return name.EndsWith(".lst", StringComparison.Ordinal) && name.IndexOf('/', GrubModules.Length) < 0;
        }

        // shim looks beside itself, which is linux/ or, when a DHCP server named the boot file, the TFTP root.
        string file = name.StartsWith(BootFiles.LinuxFolder + "/", StringComparison.Ordinal) ? name[(BootFiles.LinuxFolder.Length + 1)..] : name;

        return file.EndsWith(".efi", StringComparison.Ordinal)
            && (file.StartsWith("revocations_", StringComparison.Ordinal) || file.StartsWith("shim_certificate_", StringComparison.Ordinal))
            && !file.Contains('/', StringComparison.Ordinal);
    }
}
