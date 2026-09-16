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

using HttpAgentServer server = new(options!.ServerUrl, options.EnrollmentToken, options.RootCertificate);

IMachineIdentityReader identity = options.DryRun
    ? new DryRunMachineIdentityReader(options.DryRunId)
    : new HardwareMachineIdentityReader();

AgentLog log = new(TimeProvider.System, Console.Out);

if (options.DryRun)
{
    log.Information($"Dry run {options.DryRunId}: this computer stands in for a fake machine and nothing on it is changed.");
}

AgentLoop loop = new(server, identity, log, TimeProvider.System, version);

return await loop.RunAsync(stop.Token).ConfigureAwait(false);
