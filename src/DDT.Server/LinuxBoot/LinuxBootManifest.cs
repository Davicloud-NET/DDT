// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.LinuxBoot;

// What ddt-linux-boot.json says about the Linux boot environment beside it. Only what the server reads so far.
internal sealed class LinuxBootManifest
{
    // The snapshot.debian.org time its packages are pinned at.
    public string? Snapshot { get; set; }

    public string? KernelRelease { get; set; }
}
