// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Facts;
using DDT.Agent.InstalledPhase;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// What the DdtSequence service runs: the agent the hand-over staged into <Windows volume>\DDT\agent, continuing the
// run. Nobody watches its console, so it logs to DDT\logs\agent.log as well as to the server.
public static class WindowsPhaseService
{
    public static string LogPathIn(string windowsRoot) => Path.Combine(RunFiles.DirectoryIn(windowsRoot), "logs", "agent.log");

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
        ToolRunner tools = new(log, TimeProvider.System, AnsiCodePage.Decode, new AccountProcessStarter(log));
        ServiceParts parts = new(windowsRoot, log, server, tools, new WindowsRebooter(tools), version);
        AgentConfiguration staged = options.ForInstalledSystem();

        // The console of DDT's session, which Windows starts at its auto-logon, gets the run just like the console in
        // WinPE. Its pipe opens once the session is prepared, and only for the session's account.
        DeploySession? session = null;
        SessionMachineConsole console = new(log, version, () => session?.SessionIsUp());

        await using (console.ConfigureAwait(false))
        {
            ConsoleStatus status = new(console, AgentEnvironment.Windows, version, options.ServerUrl, null, dryRun: false);
            log.MachineConsole = console;
            WindowsSessionAccounts accounts = new();
            session = new DeploySession(
                windowsRoot,
                accounts,
                new SessionRegistry(Registry.LocalMachine, Registry.Users, tools),
                log,
                TimeProvider.System,
                (sid, pipeName) => console.Start(() => SessionPipe.Create(pipeName, sid)));

            SequenceRunner runner = Runner(parts, staged, status, new WindowsDeployAccount(session, accounts, log));
            InstalledPhaseLoop loop = Loop(parts, runner, session, status);

            // The control manager only needs to know whether the service failed.
            return await loop.RunAsync(cancellationToken).ConfigureAwait(false) switch
            {
                AgentExitCodes.Deployed or AgentExitCodes.Restarting => AgentExitCodes.Stopped,
                int exitCode => exitCode,
            };
        }
    }

    private static SequenceRunner Runner(ServiceParts parts, AgentConfiguration staged, ConsoleStatus status, WindowsDeployAccount account)
    {
        AgentLog log = parts.Log;
        ToolRunner tools = parts.Tools;
        DiskpartPartitioner disks = new(tools, log, TimeProvider.System, parts.Directory);
        BcdbootWriter bcdWriter = new(tools, new UefiVariables(), log);
        WimImageApplier applier = new(log, AppContext.BaseDirectory, Path.Combine(parts.Directory, "logs", "wimlib.log"));
        SequenceRunnerOptions options = new(RunHeartbeat.DefaultInterval, parts.Directory, DryRun: false) { Environment = AgentEnvironment.Windows };

        return new SequenceRunnerBuilder
        {
            Server = parts.Server,
            Disks = disks,
            Partitioner = disks,
            FreeSpace = new DriveFreeSpace(),
            PreflightChecks = [new BootImageToolsCheck(Environment.SystemDirectory), new ImageApplierCheck(applier)],
            StepRunners = new WindowsStepRunners
            {
                Server = parts.Server,
                Partitioner = disks,
                Applier = applier,
                Tools = tools,
                Joiner = new NetJoinDomainJoiner(),
                Log = log,
                TimeProvider = TimeProvider.System,
                Options = options,
                DeployAccount = account,
            },
            BcdWriter = bcdWriter,
            BootOrder = bcdWriter,
            Rebooter = parts.Rebooter,
            RestartMarker = new FileRestartMarker(parts.Directory, log, dryRun: false),
            HandOver = new WindowsHandOver(new OfflineServiceRegistration(tools, log, dryRun: false), Environment.ProcessPath!, staged, log, dryRun: false),
            Log = log,
            TimeProvider = TimeProvider.System,
            Options = options,
            Status = status,
            AccountTools = WindowsAccountTools.Create(log),
        }.Build();
    }

    private static InstalledPhaseLoop Loop(ServiceParts parts, SequenceRunner runner, DeploySession session, ConsoleStatus status) =>
        new InstalledPhaseLoopBuilder
        {
            Server = parts.Server,
            IdentityReader = new HardwareMachineIdentityReader(new FirmwareTables(), new SystemHardware(), new UefiVariables(), new WindowsSecureBoot()),
            Runner = runner,
            Profile = WindowsPhaseProfile.With(new RegistrySetupProbe()),
            Rebooter = parts.Rebooter,
            RestartMarker = new VolatileRestartMarker(parts.Log),
            Removal = new AgentRemoval(parts.WindowsRoot, parts.Tools, new MoveFileRestartDeleter(parts.Log), parts.Log),
            Log = parts.Log,
            TimeProvider = TimeProvider.System,
            HeartbeatInterval = RunHeartbeat.DefaultInterval,
            Root = parts.WindowsRoot,
            AgentVersion = parts.Version,
            DryRun = false,
            Session = session,
            Status = status,
            Logo = new ConsoleLogo(parts.Server, status, Path.Combine(parts.Directory, WindowsHandOver.ConsoleDirectory), parts.Log),
        }.Build();

    // What the service's runner and loop share.
    private sealed record ServiceParts(string WindowsRoot, AgentLog Log, HttpAgentServer Server, ToolRunner Tools, WindowsRebooter Rebooter, string Version)
    {
        public string Directory => RunFiles.DirectoryIn(WindowsRoot);
    }
}
