// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

// The agents and console a release puts next to the server. Machines get them while nothing is uploaded, so a new
// server offers agents of its own version from its first start.
public sealed record BundledReleases(string Folder)
{
    public string AgentPath => Path.Combine(Folder, AgentReleaseStores.WindowsFileName);

    // The agent for Linux, which the Linux boot environment's ddt.img holds too.
    public string LinuxAgentPath => Path.Combine(Folder, AgentReleaseStores.LinuxFileName);

    public string ConsolePath => Path.Combine(Folder, "ddt-console.zip");
}
