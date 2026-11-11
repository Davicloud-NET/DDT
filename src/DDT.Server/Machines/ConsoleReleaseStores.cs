// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DDT.Server.Machines;

// The consoles the server offers, one for each system the agents run on.
public sealed class ConsoleReleaseStores(IOptions<AgentReleaseOptions> options, IOptions<DdtOptions> ddt, BundledReleases bundled)
{
    public const string WindowsFileName = "ddt-console.zip";

    public const string LinuxFileName = "ddt-console-linux-x64.zip";

    public ConsoleReleaseStore Windows { get; } = new(new ConsoleSlot(
        AgentFormat.WindowsExecutable,
        options.Value.ConsolePath,
        Path.Combine(ddt.Value.StorePath, "agent", WindowsFileName),
        bundled.ConsolePath));

    public ConsoleReleaseStore Linux { get; } = new(new ConsoleSlot(
        AgentFormat.LinuxExecutable,
        options.Value.LinuxConsolePath,
        Path.Combine(ddt.Value.StorePath, "agent", LinuxFileName),
        bundled.LinuxConsolePath));

    // Null for a platform no console is built for. The names are those of AgentPlatforms.
    public ConsoleReleaseStore? Of(string platform) => platform switch
    {
        AgentPlatforms.WindowsX64 => Windows,
        AgentPlatforms.LinuxX64 => Linux,
        _ => null,
    };
}
