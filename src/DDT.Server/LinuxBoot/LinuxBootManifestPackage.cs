// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.LinuxBoot;

// A Debian package the Linux boot environment was built from.
internal sealed class LinuxBootManifestPackage
{
    public string? Name { get; set; }

    public string? Version { get; set; }
}
