// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DDT.Server.Machines;

// The agents the server offers, one for each system an agent is built for: Windows PE and the Linux boot environment.
public sealed class AgentReleaseStores(IOptions<AgentReleaseOptions> options, IOptions<DdtOptions> ddt, BundledReleases bundled)
{
    public const string WindowsFileName = "ddt-agent.exe";

    public const string LinuxFileName = "ddt-agent-linux-x64";

    public AgentReleaseStore Windows { get; } = new(new AgentSlot(
        AgentFormat.WindowsExecutable,
        options.Value.BinaryPath,
        Path.Combine(ddt.Value.StorePath, "agent", WindowsFileName),
        bundled.AgentPath));

    public AgentReleaseStore Linux { get; } = new(new AgentSlot(
        AgentFormat.LinuxExecutable,
        options.Value.LinuxBinaryPath,
        Path.Combine(ddt.Value.StorePath, "agent", LinuxFileName),
        bundled.LinuxAgentPath));

    // Null for a platform no agent is built for. The names are those of AgentPlatforms.
    public AgentReleaseStore? Of(string platform) => platform switch
    {
        AgentPlatforms.WindowsX64 => Windows,
        AgentPlatforms.LinuxX64 => Linux,
        _ => null,
    };
}
