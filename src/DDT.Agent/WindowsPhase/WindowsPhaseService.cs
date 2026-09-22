// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;

namespace DDT.Agent.WindowsPhase;

// What the DdtSequence service runs: the agent the hand-over staged into <Windows volume>\DDT\agent, with the agent.json
// beside it, going on with the run. Nobody watches its console, so it logs into DDT\logs\agent.log as well as to the
// server.
public static class WindowsPhaseService
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        string windowsRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
        string directory = Path.Combine(windowsRoot, "DDT");
        string logs = Path.Combine(directory, "logs");
        Directory.CreateDirectory(logs);

        FileStream file = new(Path.Combine(logs, "agent.log"), FileMode.Append, FileAccess.Write, FileShare.Read);
        StreamWriter writer = new(file, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

        await using (writer.ConfigureAwait(false))
        {
            AgentLog log = new(TimeProvider.System, writer);

            try
            {
                return await RunAsync(windowsRoot, directory, log, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                log.Error($"The agent stopped on an unexpected error: {exception}");

                throw;
            }
        }
    }

    private static async Task<int> RunAsync(string windowsRoot, string directory, AgentLog log, CancellationToken cancellationToken)
    {
        if (!AgentOptions.TryParse([], out AgentOptions? options, out string error))
        {
            log.Error(error);

            return AgentExitCodes.ConfigurationError;
        }

        string version = typeof(AgentLoop).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        using HttpAgentServer server = new(options!.ServerUrl, options.RootCertificate);
        ToolRunner tools = new(log, TimeProvider.System);
        AgentConfiguration staged = new(options.ServerUrl.AbsoluteUri, options.RootCertificate?.ExportCertificatePem(), null);

        SequenceRunner runner = new(
            server,
            new DiskpartPartitioner(tools, log, TimeProvider.System, directory),
            new WimImageApplier(log, AppContext.BaseDirectory, Path.Combine(directory, "logs", "wimlib.log")),
            new BcdbootWriter(tools, new UefiVariables(), log),
            new WindowsRebooter(tools),
            tools,
            new WindowsHandOver(new OfflineServiceRegistration(tools, log, dryRun: false), Environment.ProcessPath!, staged, log, dryRun: false),
            log,
            TimeProvider.System,
            RunHeartbeat.DefaultInterval,
            directory,
            Environment.SystemDirectory,
            dryRun: false);

        WindowsPhaseLoop loop = new(
            server,
            new HardwareMachineIdentityReader(),
            runner,
            new RegistrySetupProbe(),
            new AgentRemoval(windowsRoot, log),
            log,
            TimeProvider.System,
            RunHeartbeat.DefaultInterval,
            windowsRoot,
            version,
            dryRun: false);

        // The control manager only needs to know whether the service failed.
        return await loop.RunAsync(cancellationToken).ConfigureAwait(false) switch
        {
            AgentExitCodes.Deployed or AgentExitCodes.Restarting => AgentExitCodes.Stopped,
            int exitCode => exitCode,
        };
    }
}
