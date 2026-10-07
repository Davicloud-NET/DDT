// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.LinuxBoot;

public sealed class LinuxBootOptions
{
    public const string SectionName = "DDT:LinuxBoot";

    // The folder with the Linux boot environment, for a server run from source. Empty means linux-boot next to the
    // server's program files, where a release puts it.
    public string Path { get; set; } = string.Empty;
}
