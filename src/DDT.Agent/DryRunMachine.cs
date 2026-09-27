// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Agent.WindowsPhase;
using DDT.Contracts.Sequences;

namespace DDT.Agent;

// The fake machine a dry run stands in for, in this process: it starts the agent as Windows PE would, and once the run
// went over to the installed Windows, as that Windows starts its service, with the agent.json the hand-over staged,
// which connect turns into the server the service reaches. Its disk is root, the only thing that outlasts a restart:
// each restart starts the phase the run's state names over with new instances, as a real restart does, until the run
// ends. Tools only log, and nothing on this computer is changed, so the dry run works in a normal Windows session. A
// dry run that was stopped goes on when it is started again with the same dry run id, which names the same root. The
// console shows Windows PE's part, as the service in the installed Windows has none.
public sealed class DryRunMachine(
    AgentOptions options,
    IAgentServer server,
    Func<AgentOptions, IAgentServer> connect,
    ConsoleStatus status,
    string root,
    string agentPath,
    AgentLog log,
    TimeProvider timeProvider,
    TimeSpan heartbeatInterval,
    string agentVersion)
{
    private string Windows => Path.Combine(root, "W");

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
        DryRunDiskPartitioner disks = new(root, log);
        SequenceRunner runner = Runner(server, disks, new DryRunToolRunner(log), new DryRunRebooter(log), root, status);
        AgentLoop loop = new(server, Identity(), status, disks, runner, new LocalRunLocator([Windows]), log, timeProvider, agentVersion);

        return loop.RunAsync(cancellationToken);
    }

    // As Windows would start the service: the staged agent.json has to name the server.
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
            IAgentRemoval removal = new DryRunAgentRemoval(new AgentRemoval(Windows, tools, new DryRunRestartDeleter(log), log), root, log);

            WindowsPhaseLoop loop = new(
                windowsServer,
                Identity(),
                Runner(windowsServer, new DryRunDiskPartitioner(root, log), tools, rebooter, Path.Combine(Windows, "DDT"), null),
                new DryRunSetupProbe(log),
                rebooter,
                new DryRunRestartMarker(log),
                removal,
                log,
                timeProvider,
                heartbeatInterval,
                Windows,
                agentVersion,
                dryRun: true);

            return await loop.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            (windowsServer as IDisposable)?.Dispose();
        }
    }

    private DryRunMachineIdentityReader Identity() => new(options.DryRunId, options.DryRunSecureBoot);

    // The hand-over really stages the agent into the directory that stands in for Windows.
    private SequenceRunner Runner(
        IAgentServer runServer,
        DryRunDiskPartitioner disks,
        DryRunToolRunner tools,
        IRebooter rebooter,
        string workDirectory,
        ConsoleStatus? runStatus)
    {
        AgentConfiguration staged = new(options.ServerUrl.AbsoluteUri, options.RootCertificate?.ExportCertificatePem(), null);

        return new SequenceRunner(
            runServer,
            disks,
            new FileRawDisks(root, log),
            new DryRunImageApplier(log),
            new DryRunBcdWriter(log),
            rebooter,
            new WindowsPERestartMarker(workDirectory, log, dryRun: true),
            tools,
            new DryRunDomainJoiner(log),
            new WindowsHandOver(new OfflineServiceRegistration(tools, log, dryRun: true), agentPath, staged, log, dryRun: true),
            log,
            timeProvider,
            heartbeatInterval,
            workDirectory,
            Environment.SystemDirectory,
            dryRun: true,
            runStatus);
    }
}
