using DDT.Agent;
using DDT.Agent.Deployment;

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

IMachineIdentityReader identity = options.DryRun
    ? new DryRunMachineIdentityReader(options.DryRunId)
    : new HardwareMachineIdentityReader();

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
    AgentUpdate update = new(server, new ProcessAgentRelauncher(), log, TimeProvider.System, current, AppContext.BaseDirectory, args);

    if (await update.RunAsync(stop.Token).ConfigureAwait(false) is { } exitCode)
    {
        return exitCode;
    }
}

ConsoleSignInPrompt prompt = new(log, TimeProvider.System, options.KeyboardLayout);
DeploymentRunner runner;
IDiskPartitioner disks;

// A dry run works in a normal Windows session: its disk is a directory the run deletes when it ends, and it
// never loads wimlib, whose strict mode needs Windows PE's privileges.
if (options.DryRun)
{
    string root = Path.Combine(Path.GetTempPath(), $"ddt-dry-run-{options.DryRunId}");
    disks = new DryRunDiskPartitioner(root, log);
    runner = new DeploymentRunner(
        server,
        disks,
        new DryRunImageApplier(log),
        new DryRunBcdWriter(log),
        new DryRunRebooter(log),
        log,
        TimeProvider.System,
        DeploymentHeartbeat.DefaultInterval,
        root);
}
else
{
    // The agent's directory is X:\DDT in Windows PE.
    ToolRunner tools = new(log, TimeProvider.System);
    disks = new DiskpartPartitioner(tools, log, TimeProvider.System, AppContext.BaseDirectory);
    runner = new DeploymentRunner(
        server,
        disks,
        new WimImageApplier(log, AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "wimlib.log")),
        new BcdbootWriter(tools, new UefiVariables(), log),
        new WpeutilRebooter(tools),
        log,
        TimeProvider.System,
        DeploymentHeartbeat.DefaultInterval);
}

AgentLoop loop = new(server, identity, prompt, disks, runner, log, TimeProvider.System, version);

return await loop.RunAsync(stop.Token).ConfigureAwait(false);
