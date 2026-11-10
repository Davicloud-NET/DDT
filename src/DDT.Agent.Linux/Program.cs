// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent;
using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.LinuxPhase;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Core.CloudInit;

// systemd starts the agent in the installed Linux with this argument alone, and nobody sits at it.
if (args.Contains(LinuxAgentFiles.Argument))
{
    return await LinuxPhaseService.RunAsync().ConfigureAwait(false);
}

// The terminal shows a key in clear until the first read takes it over, so that read comes before any line is
// printed. A key pressed while the machine started must not show up in a password later.
if (!Console.IsInputRedirected && !args.Contains(AgentStartup.LicensesArgument))
{
    _ = Console.KeyAvailable;
}

// Scroll Lock and Ctrl+S must not hold the screen the agent writes to, or a run would wait with it.
using ITimer? flowing = Console.IsOutputRedirected ? null : new LinuxTerminal().KeepFlowing(TimeProvider.System);

// The question about a run on a disk gets its time once, and the update check before it takes from it.
HandedOverQuestionTime questionTime = new(TimeProvider.System);

AgentStartup startup = new(new AgentPlatform
{
    BootEnvironment = AgentEnvironment.LinuxBoot,
    IsPublished = AgentBuild.IsPublished,
    ReleasePlatform = AgentPlatforms.LinuxX64,
    ConsoleLauncher = path => new LinuxConsoleLauncher(path),
    WriteStartupNotices = LinuxLegalNotices.WriteStartupNotices,
    WriteLicenses = LinuxLegalNotices.WriteLicenses,
    ReadyAsync = LinuxBoot.ReadyAsync,
    UpdateCheckTimeAsync = (_, cancellationToken) => LinuxBoot.UpdateCheckTimeAsync(questionTime, cancellationToken),
    DryRunAsync = DryRunAsync,
    RunAsync = (agent, cancellationToken) => RunAsync(agent, questionTime, cancellationToken),
});

return await startup.RunAsync(args).ConfigureAwait(false);

// A dry run stands in for a machine that started the Linux boot environment and goes on in the installed Linux. A
// file is its disk, and nothing on this computer is changed. It cannot go on in Windows PE, which only the agent for
// Windows stands in for.
static Task<int> DryRunAsync(StartedAgent agent, CancellationToken cancellationToken)
{
    DryRunMachineOptions options = new(
        agent.Options,
        Path.Combine(Path.GetTempPath(), $"ddt-dry-run-{agent.Options.DryRunId}"),
        LinuxBoot.AgentPath,
        RunHeartbeat.DefaultInterval,
        agent.Version);

    LinuxDryRunMachine machine = new(
        options,
        agent.Server,
        staged => new HttpAgentServer(staged.ServerUrl, staged.RootCertificate) { Environment = AgentEnvironment.Linux },
        agent.Status,
        agent.Log,
        TimeProvider.System)
    {
        FreeSpace = new LinuxFreeSpace(),
    };

    return machine.RunAsync(cancellationToken);
}

static Task<int> RunAsync(StartedAgent agent, HandedOverQuestionTime questionTime, CancellationToken cancellationToken)
{
    LinuxRestart restart = new(agent.Log);

    return LinuxBoot.RunAsync(agent, restart, questionTime, cancellationToken);
}
