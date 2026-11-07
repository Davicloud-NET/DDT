// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.LinuxBoot;

// A file of the Linux boot environment as its description lists it. Sha256 is in hex.
internal sealed class LinuxBootManifestFile
{
    public string? Name { get; set; }

    public long Size { get; set; }

    public string? Sha256 { get; set; }
}
