// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent;
using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Facts;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;
using DDT.Contracts.Agents;

// The service control manager gives a service 30 seconds to connect, so nothing comes before this.
if (args.Contains(WindowsServiceHost.Argument))
{
    if (WindowsServiceHost.TryRun(WindowsPhaseService.RunAsync, out int serviceExitCode))
    {
        return serviceExitCode;
    }

    Console.Error.WriteLine(
        $"{WindowsServiceHost.Argument} is only for the {OfflineServiceRegistration.ServiceName} service, which Windows starts " +
        $"after the hand-over (error {serviceExitCode}).");

    return AgentExitCodes.ConfigurationError;
}

AgentStartup startup = new(new AgentPlatform
{
    BootEnvironment = AgentEnvironment.WindowsPE,
    IsPublished = AgentBuild.IsPublished,
    WriteStartupNotices = AgentLegalNotices.WriteStartupNotices,
    WriteLicenses = AgentLegalNotices.WriteLicenses,
    DryRunAsync = DryRunAsync,
    RunAsync = RunAsync,
});

return await startup.RunAsync(args).ConfigureAwait(false);

// A dry run does the whole run in this process, in Windows PE unless it is asked for the Linux boot environment, and
// in the other one once its agent restarts into it. A directory stands in for the machine's disk and holds the run
// until it ends. The Windows phase reaches the server with the agent.json that the hand-over staged.
static Task<int> DryRunAsync(StartedAgent agent, CancellationToken cancellationToken)
{
    DryRunMachineOptions options = new(
        agent.Options,
        Path.Combine(Path.GetTempPath(), $"ddt-dry-run-{agent.Options.DryRunId}"),
        Environment.ProcessPath!,
        RunHeartbeat.DefaultInterval,
        agent.Version);

    DryRunBoot machine = new(
        options,
        agent.Server,
        staged => new HttpAgentServer(staged.ServerUrl, staged.RootCertificate),
        agent.Status,
        agent.Log,
        TimeProvider.System);

    return machine.RunAsync(cancellationToken);
}

// In WinPE the agent's directory is X:\DDT, on the RAM disk that every restart builds from scratch.
static Task<int> RunAsync(StartedAgent agent, CancellationToken cancellationToken)
{
    (_, HttpAgentServer server, AgentLog log, ConsoleStatus status, PipeMachineConsole? graphical, _, string version) = agent;
    ToolRunner tools = new(log, TimeProvider.System, AnsiCodePage.Decode, new AccountProcessStarter(log));
    UefiVariables firmware = new();
    DiskpartPartitioner disks = new(tools, log, TimeProvider.System, AppContext.BaseDirectory);
    BcdbootWriter bcdWriter = new(tools, firmware, log);
    WindowsPERebooter rebooter = new(tools, firmware, log);
    WimImageApplier applier = new(log, AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "wimlib.log"));
    SequenceRunnerOptions runnerOptions = new(RunHeartbeat.DefaultInterval, AppContext.BaseDirectory, DryRun: false);
    SequenceRunner runner = new SequenceRunnerBuilder
    {
        Server = server,
        Disks = disks,
        Partitioner = disks,
        FreeSpace = new DriveFreeSpace(),
        PreflightChecks =
        [
            new BootEnvironmentCheck(AgentEnvironment.WindowsPE),
            new BootImageToolsCheck(Environment.SystemDirectory),
            new ImageApplierCheck(applier),
        ],
        StepRunners = new WindowsStepRunners
        {
            Server = server,
            Partitioner = disks,
            Applier = applier,
            Tools = tools,
            Joiner = new NetJoinDomainJoiner(),
            Log = log,
            TimeProvider = TimeProvider.System,
            Options = runnerOptions,
        },
        BcdWriter = bcdWriter,
        BootOrder = bcdWriter,
        Rebooter = rebooter,
        RestartMarker = new FileRestartMarker(AppContext.BaseDirectory, log, dryRun: false),
        HandOver = HandOver(agent, tools),
        Log = log,
        TimeProvider = TimeProvider.System,
        Options = runnerOptions,
        Status = status,
        AccountTools = WindowsAccountTools.Create(log),
    }.Build();
    AgentMachine machine = Machine(disks, new EnvironmentSwitch(new ResumeCarrier(firmware, AgentEnvironment.WindowsPE, log), rebooter), version);
    AgentLoop loop = new(server, machine, status, runner, log, TimeProvider.System)
    {
        Logo = graphical is null ? null : new ConsoleLogo(server, status, AppContext.BaseDirectory, log),
    };

    return loop.RunAsync(cancellationToken);
}

// The machine as the loop sees it. Its resume token crosses a restart into the Linux boot environment in the firmware,
// and comes back the same way.
static AgentMachine Machine(DiskpartPartitioner disks, EnvironmentSwitch change, string version)
{
    HardwareMachineIdentityReader identity = new(new FirmwareTables(), new SystemHardware(), new UefiVariables(), new WindowsSecureBoot());

    return new(identity, disks, new LocalRunLocator(WindowsFixedDrives.Roots()), version, AgentEnvironment.WindowsPE) { Switch = change };
}

// The agent.json it stages into Windows only holds what it takes to reach the server.
static WindowsHandOver HandOver(StartedAgent agent, ToolRunner tools)
{
    AgentConfiguration staged = new(agent.Options.ServerUrl.AbsoluteUri, agent.Options.RootCertificate?.ExportCertificatePem(), null);

    return new WindowsHandOver(
        new OfflineServiceRegistration(tools, agent.Log, dryRun: false),
        Environment.ProcessPath!,
        staged,
        agent.Log,
        dryRun: false,
        () => ConsoleForWindows(agent));
}

// The console that goes into the installed Windows with the run. It's the one that ran here, but only if it spoke
// this agent's version of the protocol. A console of another version couldn't show DDT's session there.
static string? ConsoleForWindows(StartedAgent agent)
{
    if (agent.Graphical is null || agent.ConsolePath is null)
    {
        return null;
    }

    if (!agent.Graphical.Greeted)
    {
        agent.Log.Information("The graphical console did not speak this agent's version of the console protocol, so it does not come into Windows.");

        return null;
    }

    return Path.GetDirectoryName(agent.ConsolePath);
}
