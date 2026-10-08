// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

// The terminal shows a key in clear until the first read takes it over, so that read comes before any line is
// printed. A key pressed while the machine started must not show up in a password later.
if (!Console.IsInputRedirected && !args.Contains(AgentStartup.LicensesArgument))
{
    _ = Console.KeyAvailable;
}

// Scroll Lock and Ctrl+S must not hold the screen the agent writes to, or a run would wait with it.
using ITimer? flowing = Console.IsOutputRedirected ? null : new LinuxTerminal().KeepFlowing(TimeProvider.System);

AgentStartup startup = new(new AgentPlatform
{
    BootEnvironment = AgentEnvironment.LinuxBoot,
    IsPublished = AgentBuild.IsPublished,
    ReleasePlatform = AgentPlatforms.LinuxX64,
    StartsGraphicalConsole = false,
    WriteStartupNotices = LinuxLegalNotices.WriteStartupNotices,
    WriteLicenses = LinuxLegalNotices.WriteLicenses,
    ReadyAsync = LinuxBoot.ReadyAsync,
    DryRunAsync = DryRunAsync,
    RunAsync = RunAsync,
});

return await startup.RunAsync(args).ConfigureAwait(false);

// A dry run stands in for a machine that started the Linux boot environment. A file is its disk, and nothing on this
// computer is changed. It cannot go on in Windows PE, which only the agent for Windows stands in for.
static Task<int> DryRunAsync(StartedAgent agent, CancellationToken cancellationToken)
{
    DryRunMachineOptions options = new(
        agent.Options,
        Path.Combine(Path.GetTempPath(), $"ddt-dry-run-{agent.Options.DryRunId}"),
        Environment.ProcessPath ?? LinuxBoot.AgentFileName,
        RunHeartbeat.DefaultInterval,
        agent.Version);

    LinuxBootDryRun machine = new(options, agent.Server, agent.Status, agent.Log, TimeProvider.System) { FreeSpace = new LinuxFreeSpace() };

    return machine.RunAsync(cancellationToken);
}

static Task<int> RunAsync(StartedAgent agent, CancellationToken cancellationToken)
{
    LinuxRestart restart = new(agent.Log);

    return LinuxBoot.RunAsync(LinuxBoot.Loop(agent, restart), restart, cancellationToken);
}
