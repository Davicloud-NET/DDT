// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent;
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
    // Console.Out encodes for the console's code page, 437 in an English Windows PE, and replaces what it lacks,
    // such as the copyright sign. Redirected, the texts go out in UTF-8, so a file gets them unchanged.
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
    string current = await AgentUpdate.Sha256Async(Environment.ProcessPath!, stop.Token).ConfigureAwait(false);
    AgentUpdate update = new(server, new ProcessAgentRelauncher(server.CloseConnections), log, TimeProvider.System, current, AppContext.BaseDirectory, args);

    if (await update.RunAsync(stop.Token).ConfigureAwait(false) is { } exitCode)
    {
        return exitCode;
    }
}

ConsoleSignInPrompt prompt = new(log, TimeProvider.System, options.KeyboardLayout);

// The whole run, both phases, in this process, with a directory for the machine's disk that holds the run until it
// ends. The Windows phase reaches the server as the agent.json the hand-over staged says.
if (options.DryRun)
{
    DryRunMachine machine = new(
        options,
        server,
        staged => new HttpAgentServer(staged.ServerUrl, staged.RootCertificate),
        prompt,
        Path.Combine(Path.GetTempPath(), $"ddt-dry-run-{options.DryRunId}"),
        Environment.ProcessPath!,
        log,
        TimeProvider.System,
        RunHeartbeat.DefaultInterval,
        version);

    return await machine.RunAsync(stop.Token).ConfigureAwait(false);
}

// The agent's directory is X:\DDT in Windows PE, on the RAM disk that every restart builds anew. What it stages into
// Windows needs to reach the server, and nothing else.
ToolRunner tools = new(log, TimeProvider.System);
UefiVariables firmware = new();
DiskpartPartitioner disks = new(tools, log, TimeProvider.System, AppContext.BaseDirectory);
AgentConfiguration staged = new(options.ServerUrl.AbsoluteUri, options.RootCertificate?.ExportCertificatePem(), null);
SequenceRunner runner = new(
    server,
    disks,
    new PhysicalDisks(),
    new WimImageApplier(log, AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "wimlib.log")),
    new BcdbootWriter(tools, firmware, log),
    new WindowsPERebooter(tools, firmware, log),
    new WindowsPERestartMarker(AppContext.BaseDirectory, log, dryRun: false),
    tools,
    new NetJoinDomainJoiner(),
    new WindowsHandOver(new OfflineServiceRegistration(tools, log, dryRun: false), Environment.ProcessPath!, staged, log, dryRun: false),
    log,
    TimeProvider.System,
    RunHeartbeat.DefaultInterval,
    AppContext.BaseDirectory,
    Environment.SystemDirectory,
    dryRun: false);
AgentLoop loop = new(
    server,
    new HardwareMachineIdentityReader(),
    prompt,
    disks,
    runner,
    new LocalRunLocator(LocalRunLocator.FixedDrives()),
    log,
    TimeProvider.System,
    version);

return await loop.RunAsync(stop.Token).ConfigureAwait(false);
