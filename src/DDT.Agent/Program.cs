// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent;
using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;

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

if (args.Contains(AgentLegalNotices.LicensesArgument))
{
    // Console.Out encodes for the console's code page, 437 in an English WinPE, and replaces characters it lacks,
    // such as the copyright sign. When output is redirected, the texts go out in UTF-8, so a file gets them unchanged.
    if (Console.IsOutputRedirected)
    {
        using StreamWriter output = new(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        AgentLegalNotices.WriteLicenses(output);
    }
    else
    {
        AgentLegalNotices.WriteLicenses(Console.Out);
    }

    return AgentExitCodes.Stopped;
}

AgentLegalNotices.WriteStartupNotices(Console.Out);

if (!AgentOptions.TryParse(args, out AgentOptions? options, out string error))
{
    Console.Error.WriteLine(error);

    return AgentExitCodes.ConfigurationError;
}

using CancellationTokenSource stop = new();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stop.Cancel();
};

string version = typeof(AgentLoop).Assembly.GetName().Version?.ToString(3) ?? "unknown";

using HttpAgentServer server = new(options!.ServerUrl, options.RootCertificate);

AgentLog log = new(TimeProvider.System, Console.Out);
TextMachineConsole text = new(new ConsoleSignInPrompt(log, TimeProvider.System, options.KeyboardLayout), log);

// If there's a graphical console, it starts before anything else, so it shows the update check and the connection
// too. The text console stays underneath it and takes over if it goes away.
string? consolePath = PipeMachineConsole.PathFor(options, Console.IsInputRedirected, AppContext.BaseDirectory);
await using PipeMachineConsole? graphical = consolePath is null
    ? null
    : new PipeMachineConsole(text, new ProcessConsoleLauncher(consolePath), log, version);
log.MachineConsole = graphical;
ConsoleStatus status = new(graphical ?? (IMachineConsole)text, version, options.ServerUrl, options.KeyboardLayout, options.DryRun);
graphical?.Start();

if (options.DryRun)
{
    log.Information($"Dry run {options.DryRunId}: this computer stands in for a fake machine and nothing on it is changed.");
}

// A dry run changes nothing on the computer it runs on, and a build run from source would swap itself for the
// published agent, so neither updates. An agent started by an update never updates again.
if (options.DryRun || !AgentBuild.IsPublished)
{
    log.Information("Not checking for a newer agent in a dry run or an agent that was not published.");
}
else if (!options.NoUpdate)
{
    string current = await ReleaseFiles.Sha256Async(Environment.ProcessPath!, stop.Token).ConfigureAwait(false);
    ProcessAgentRelauncher relauncher = new(() =>
    {
        server.CloseConnections();
        graphical?.Close();
    });
    // A console passed with --console is kept. The one next to the agent is replaced by the server's version.
    AgentUpdate update = new(
        server,
        relauncher,
        log,
        TimeProvider.System,
        new RunningAgent(current, AppContext.BaseDirectory, args, options.ConsolePath is null ? consolePath : null),
        status);

    if (await update.RunAsync(stop.Token).ConfigureAwait(false) is { } exitCode)
    {
        return exitCode;
    }
}

// A dry run does the whole run, both phases, in this process. A directory stands in for the machine's disk and holds
// the run until it ends. The Windows phase reaches the server with the agent.json that the hand-over staged.
if (options.DryRun)
{
    DryRunMachine machine = new(
        new DryRunMachineOptions(
            options,
            Path.Combine(Path.GetTempPath(), $"ddt-dry-run-{options.DryRunId}"),
            Environment.ProcessPath!,
            RunHeartbeat.DefaultInterval,
            version),
        server,
        staged => new HttpAgentServer(staged.ServerUrl, staged.RootCertificate),
        status,
        log,
        TimeProvider.System);

    return await machine.RunAsync(stop.Token).ConfigureAwait(false);
}

// In WinPE the agent's directory is X:\DDT, on the RAM disk that every restart builds from scratch. The agent.json it
// stages into Windows only holds what it takes to reach the server.
ToolRunner tools = new(log, TimeProvider.System, new AccountProcessStarter(log));
UefiVariables firmware = new();
DiskpartPartitioner disks = new(tools, log, TimeProvider.System, AppContext.BaseDirectory);
AgentConfiguration staged = new(options.ServerUrl.AbsoluteUri, options.RootCertificate?.ExportCertificatePem(), null);
SequenceRunner runner = new SequenceRunnerBuilder
{
    Server = server,
    Partitioner = disks,
    RawDisks = new PhysicalDisks(),
    Applier = new WimImageApplier(log, AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "wimlib.log")),
    BcdWriter = new BcdbootWriter(tools, firmware, log),
    Rebooter = new WindowsPERebooter(tools, firmware, log),
    RestartMarker = new WindowsPERestartMarker(AppContext.BaseDirectory, log, dryRun: false),
    Tools = tools,
    Joiner = new NetJoinDomainJoiner(),
    HandOver = new WindowsHandOver(
        new OfflineServiceRegistration(tools, log, dryRun: false),
        Environment.ProcessPath!,
        staged,
        log,
        dryRun: false,
        ConsoleForWindows),
    Log = log,
    TimeProvider = TimeProvider.System,
    Options = new SequenceRunnerOptions(RunHeartbeat.DefaultInterval, AppContext.BaseDirectory, Environment.SystemDirectory, DryRun: false),
    Status = status,
}.Build();
AgentLoop loop = new(
    server,
    new AgentMachine(new HardwareMachineIdentityReader(), disks, new LocalRunLocator(LocalRunLocator.FixedDrives()), version),
    status,
    runner,
    log,
    TimeProvider.System)
{
    Logo = graphical is null ? null : new ConsoleLogo(server, status, AppContext.BaseDirectory, log),
};

return await loop.RunAsync(stop.Token).ConfigureAwait(false);

// The console that goes into the installed Windows with the run. It's the one that ran here, but only if it spoke
// this agent's version of the protocol. A console of another version couldn't show DDT's session there.
string? ConsoleForWindows()
{
    if (graphical is null || consolePath is null)
    {
        return null;
    }

    if (!graphical.Greeted)
    {
        log.Information("The graphical console did not speak this agent's version of the console protocol, so it does not come into Windows.");

        return null;
    }

    return Path.GetDirectoryName(consolePath);
}
