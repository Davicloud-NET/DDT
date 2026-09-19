using DDT.Agent;

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
AgentLoop loop = new(server, identity, prompt, log, TimeProvider.System, version);

return await loop.RunAsync(stop.Token).ConfigureAwait(false);
