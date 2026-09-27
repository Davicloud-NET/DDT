// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// What the DdtSequence service runs: the agent the hand-over staged into <Windows volume>\DDT\agent, with the agent.json
// beside it, going on with the run. Nobody watches its console, so it logs into DDT\logs\agent.log as well as to the
// server.
public static class WindowsPhaseService
{
    public static string LogPathIn(string windowsRoot) => Path.Combine(windowsRoot, "DDT", "logs", "agent.log");

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        string windowsRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
        string directory = Path.Combine(windowsRoot, "DDT");
        string logPath = LogPathIn(windowsRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        FileStream file = new(logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        StreamWriter writer = new(file, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

        await using (writer.ConfigureAwait(false))
        {
            AgentLog log = new(TimeProvider.System, writer, datedLines: true);

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
        WindowsRebooter rebooter = new(tools);
        AgentConfiguration staged = new(options.ServerUrl.AbsoluteUri, options.RootCertificate?.ExportCertificatePem(), null);

        // The console of DDT's session, which Windows starts at its auto-logon, gets the run as the console in Windows PE
        // does. Its pipe opens once the session is prepared, for the session's account alone.
        DeploySession? session = null;
        SessionMachineConsole console = new(log, version, () => session?.SessionIsUp());

        await using (console.ConfigureAwait(false))
        {
            ConsoleStatus status = new(console, version, options.ServerUrl, null, dryRun: false);
            log.MachineConsole = console;
            session = new DeploySession(
                windowsRoot,
                new WindowsSessionAccounts(),
                Registry.LocalMachine,
                Registry.Users,
                tools,
                log,
                TimeProvider.System,
                (sid, pipeName) => console.Start(() => SessionMachineConsole.CreatePipe(pipeName, sid)));

            return await RunAsync(windowsRoot, directory, log, version, server, tools, rebooter, staged, session, status, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<int> RunAsync(
        string windowsRoot,
        string directory,
        AgentLog log,
        string version,
        HttpAgentServer server,
        ToolRunner tools,
        WindowsRebooter rebooter,
        AgentConfiguration staged,
        DeploySession session,
        ConsoleStatus status,
        CancellationToken cancellationToken)
    {
        SequenceRunner runner = new(
            server,
            new DiskpartPartitioner(tools, log, TimeProvider.System, directory),
            new PhysicalDisks(),
            new WimImageApplier(log, AppContext.BaseDirectory, Path.Combine(directory, "logs", "wimlib.log")),
            new BcdbootWriter(tools, new UefiVariables(), log),
            rebooter,
            new WindowsPERestartMarker(directory, log, dryRun: false),
            tools,
            new NetJoinDomainJoiner(),
            new WindowsHandOver(new OfflineServiceRegistration(tools, log, dryRun: false), Environment.ProcessPath!, staged, log, dryRun: false),
            log,
            TimeProvider.System,
            RunHeartbeat.DefaultInterval,
            directory,
            Environment.SystemDirectory,
            dryRun: false,
            status);

        WindowsPhaseLoop loop = new(
            server,
            new HardwareMachineIdentityReader(),
            runner,
            new RegistrySetupProbe(),
            rebooter,
            new VolatileRestartMarker(log),
            new AgentRemoval(windowsRoot, tools, new MoveFileRestartDeleter(log), log),
            log,
            TimeProvider.System,
            RunHeartbeat.DefaultInterval,
            windowsRoot,
            version,
            dryRun: false,
            session,
            status,
            new ConsoleLogo(server, status, Path.Combine(windowsRoot, "DDT", WindowsHandOver.ConsoleDirectory), log));

        // The control manager only needs to know whether the service failed.
        return await loop.RunAsync(cancellationToken).ConfigureAwait(false) switch
        {
            AgentExitCodes.Deployed or AgentExitCodes.Restarting => AgentExitCodes.Stopped,
            int exitCode => exitCode,
        };
    }
}
