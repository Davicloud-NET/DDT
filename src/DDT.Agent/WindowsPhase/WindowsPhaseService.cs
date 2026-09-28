// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// What the DdtSequence service runs: the agent the hand-over staged into <Windows volume>\DDT\agent, going on with the
// run. Nobody watches its console, so it logs into DDT\logs\agent.log as well as to the server.
public static class WindowsPhaseService
{
    public static string LogPathIn(string windowsRoot) => Path.Combine(windowsRoot, "DDT", "logs", "agent.log");

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        string windowsRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
        string logPath = LogPathIn(windowsRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        FileStream file = new(logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        StreamWriter writer = new(file, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };

        await using (writer.ConfigureAwait(false))
        {
            AgentLog log = new(TimeProvider.System, writer, datedLines: true);

            try
            {
                return await RunAsync(windowsRoot, log, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                log.Error($"The agent stopped on an unexpected error: {exception}");

                throw;
            }
        }
    }

    private static async Task<int> RunAsync(string windowsRoot, AgentLog log, CancellationToken cancellationToken)
    {
        if (!AgentOptions.TryParse([], out AgentOptions? options, out string error))
        {
            log.Error(error);

            return AgentExitCodes.ConfigurationError;
        }

        string version = typeof(AgentLoop).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        using HttpAgentServer server = new(options!.ServerUrl, options.RootCertificate);
        ToolRunner tools = new(log, TimeProvider.System, new AccountProcessStarter(log));
        ServiceParts parts = new(windowsRoot, log, server, tools, new WindowsRebooter(tools), version);
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
                new SessionRegistry(Registry.LocalMachine, Registry.Users, tools),
                log,
                TimeProvider.System,
                (sid, pipeName) => console.Start(() => SessionMachineConsole.CreatePipe(pipeName, sid)));

            WindowsPhaseLoop loop = Loop(parts, Runner(parts, staged, status), session, status);

            // The control manager only needs to know whether the service failed.
            return await loop.RunAsync(cancellationToken).ConfigureAwait(false) switch
            {
                AgentExitCodes.Deployed or AgentExitCodes.Restarting => AgentExitCodes.Stopped,
                int exitCode => exitCode,
            };
        }
    }

    private static SequenceRunner Runner(ServiceParts parts, AgentConfiguration staged, ConsoleStatus status)
    {
        AgentLog log = parts.Log;
        ToolRunner tools = parts.Tools;

        return new SequenceRunnerBuilder
        {
            Server = parts.Server,
            Partitioner = new DiskpartPartitioner(tools, log, TimeProvider.System, parts.Directory),
            RawDisks = new PhysicalDisks(),
            Applier = new WimImageApplier(log, AppContext.BaseDirectory, Path.Combine(parts.Directory, "logs", "wimlib.log")),
            BcdWriter = new BcdbootWriter(tools, new UefiVariables(), log),
            Rebooter = parts.Rebooter,
            RestartMarker = new WindowsPERestartMarker(parts.Directory, log, dryRun: false),
            Tools = tools,
            Joiner = new NetJoinDomainJoiner(),
            HandOver = new WindowsHandOver(new OfflineServiceRegistration(tools, log, dryRun: false), Environment.ProcessPath!, staged, log, dryRun: false),
            Log = log,
            TimeProvider = TimeProvider.System,
            Options = new SequenceRunnerOptions(RunHeartbeat.DefaultInterval, parts.Directory, Environment.SystemDirectory, DryRun: false),
            Status = status,
        }.Build();
    }

    private static WindowsPhaseLoop Loop(ServiceParts parts, SequenceRunner runner, DeploySession session, ConsoleStatus status) =>
        new WindowsPhaseLoopBuilder
        {
            Server = parts.Server,
            IdentityReader = new HardwareMachineIdentityReader(),
            Runner = runner,
            Setup = new RegistrySetupProbe(),
            Rebooter = parts.Rebooter,
            RestartMarker = new VolatileRestartMarker(parts.Log),
            Removal = new AgentRemoval(parts.WindowsRoot, parts.Tools, new MoveFileRestartDeleter(parts.Log), parts.Log),
            Log = parts.Log,
            TimeProvider = TimeProvider.System,
            HeartbeatInterval = RunHeartbeat.DefaultInterval,
            WindowsRoot = parts.WindowsRoot,
            AgentVersion = parts.Version,
            DryRun = false,
            Session = session,
            Status = status,
            Logo = new ConsoleLogo(parts.Server, status, Path.Combine(parts.Directory, WindowsHandOver.ConsoleDirectory), parts.Log),
        }.Build();

    // What the service's runner and loop share.
    private sealed record ServiceParts(string WindowsRoot, AgentLog Log, HttpAgentServer Server, ToolRunner Tools, WindowsRebooter Rebooter, string Version)
    {
        public string Directory => Path.Combine(WindowsRoot, "DDT");
    }
}
