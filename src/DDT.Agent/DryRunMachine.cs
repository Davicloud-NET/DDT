// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;
using DDT.Contracts.Sequences;

namespace DDT.Agent;

// The machine a dry run stands in for. Each restart starts the phase named in the run's state again, with new
// instances, until the run ends. Tools only log, and nothing on this computer changes. The installed Windows reaches
// the server through connect, with the settings from the agent.json that the hand-over staged.
public sealed class DryRunMachine(
    DryRunMachineOptions options,
    IAgentServer server,
    Func<AgentOptions, IAgentServer> connect,
    ConsoleStatus status,
    AgentLog log,
    TimeProvider timeProvider)
{
    private string Windows => Path.Combine(options.Root, "W");

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            bool inWindows = await LocalRun.LoadAsync(Windows, log, CancellationToken.None).ConfigureAwait(false) is { State.Phase: SequencePhase.Windows };
            int exitCode = inWindows
                ? await RunWindowsAsync(cancellationToken).ConfigureAwait(false)
                : await RunWindowsPEAsync(cancellationToken).ConfigureAwait(false);

            if (exitCode != AgentExitCodes.Restarting || cancellationToken.IsCancellationRequested)
            {
                return exitCode;
            }

            log.Information("Dry run: the machine restarts, which here starts its agent over.");
        }
    }

    private Task<int> RunWindowsPEAsync(CancellationToken cancellationToken)
    {
        DryRunDiskPartitioner disks = new(options.Root, log);
        SequenceRunner runner = Runner(server, disks, new DryRunToolRunner(log), new DryRunRebooter(log), SequencePhase.WindowsPE);
        AgentLoop loop = new(server, new AgentMachine(Identity(), disks, new LocalRunLocator([Windows]), options.AgentVersion), status, runner, log, timeProvider);

        return loop.RunAsync(cancellationToken);
    }

    // Starts the agent the way Windows would start the service. The staged agent.json has to name the server.
    private async Task<int> RunWindowsAsync(CancellationToken cancellationToken)
    {
        string configuration = Path.Combine(Windows, "DDT", WindowsHandOver.AgentDirectory, WindowsHandOver.ConfigurationFileName);

        if (!AgentOptions.TryParse(["--config", configuration], out AgentOptions? staged, out string error))
        {
            log.Error($"Dry run: the {OfflineServiceRegistration.ServiceName} service cannot start: {error}");

            return AgentExitCodes.ConfigurationError;
        }

        log.Information($"Dry run: Windows starts the {OfflineServiceRegistration.ServiceName} service, which here goes on in this process with {configuration}.");
        IAgentServer windowsServer = connect(staged!);

        try
        {
            DryRunToolRunner tools = new(log);
            WindowsRebooter rebooter = new(tools);

            WindowsPhaseLoop loop = new WindowsPhaseLoopBuilder
            {
                Server = windowsServer,
                IdentityReader = Identity(),
                Runner = Runner(windowsServer, new DryRunDiskPartitioner(options.Root, log), tools, rebooter, SequencePhase.Windows),
                Setup = new DryRunSetupProbe(log),
                Rebooter = rebooter,
                RestartMarker = new DryRunRestartMarker(log),
                Removal = new DryRunAgentRemoval(new AgentRemoval(Windows, tools, new DryRunRestartDeleter(log), log), options.Root, log),
                Log = log,
                TimeProvider = timeProvider,
                HeartbeatInterval = options.HeartbeatInterval,
                WindowsRoot = Windows,
                AgentVersion = options.AgentVersion,
                DryRun = true,
            }.Build();

            return await loop.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            (windowsServer as IDisposable)?.Dispose();
        }
    }

    private DryRunMachineIdentityReader Identity() => new(options.Agent.DryRunId, options.Agent.DryRunSecureBoot);

    // The hand-over really stages the agent into the directory that stands in for Windows. In WinPE the root stands
    // in for the agent's directory. The console only shows the run in WinPE.
    private SequenceRunner Runner(IAgentServer runServer, DryRunDiskPartitioner disks, DryRunToolRunner tools, IRebooter rebooter, SequencePhase phase)
    {
        bool inWindows = phase == SequencePhase.Windows;
        string workDirectory = inWindows ? Path.Combine(Windows, "DDT") : options.Root;
        AgentConfiguration staged = new(options.Agent.ServerUrl.AbsoluteUri, options.Agent.RootCertificate?.ExportCertificatePem(), null);

        return new SequenceRunnerBuilder
        {
            Server = runServer,
            Partitioner = disks,
            RawDisks = new FileRawDisks(options.Root, log),
            Applier = new DryRunImageApplier(log),
            BcdWriter = new DryRunBcdWriter(log),
            Rebooter = rebooter,
            RestartMarker = new WindowsPERestartMarker(workDirectory, log, dryRun: true),
            Tools = tools,
            Joiner = new DryRunDomainJoiner(log),
            HandOver = new WindowsHandOver(new OfflineServiceRegistration(tools, log, dryRun: true), options.AgentPath, staged, log, dryRun: true),
            Log = log,
            TimeProvider = timeProvider,
            Options = new SequenceRunnerOptions(options.HeartbeatInterval, workDirectory, Environment.SystemDirectory, DryRun: true),
            Status = inWindows ? null : status,
        }.Build();
    }
}
