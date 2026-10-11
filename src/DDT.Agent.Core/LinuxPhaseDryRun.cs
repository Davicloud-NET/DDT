// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Deployment;
using DDT.Agent.InstalledPhase;
using DDT.Agent.LinuxPhase;
using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;

namespace DDT.Agent;

// A dry run's machine in the installed Linux. A folder under the dry run's root stands in for its /, and holds the
// run as the vendor-data script would install it from the seed. cloud-init, the mounts, systemd and the restart only
// log. The server is reached through connect, with the settings from the agent.json the hand-over put on the seed.
public sealed class LinuxPhaseDryRun(DryRunMachineOptions options, Func<AgentOptions, IAgentServer> connect, AgentLog log, TimeProvider timeProvider)
{
    private readonly LinuxAgentPaths _paths = new(Path.Combine(options.Root, "linux"));
    private readonly DryRunSeed _seed = new(Path.Combine(options.Root, "linux-seed"), log);

    // How this computer counts the free space of a path.
    public required IFreeSpace FreeSpace { get; init; }

    // Whether a run that was handed over waits in the folder, as after a restart of the installed Linux.
    public async Task<bool> HoldsRunAsync() =>
        await LocalRun.LoadAsync(_paths.State, log, CancellationToken.None).ConfigureAwait(false) is { State.Phase: SequencePhase.Linux };

    // Plays the first start of the installed Linux after a hand-over. False when the disk holds no run to go on with.
    public bool Install() => _seed.Install(FileRawDisks.PathFor(options.Root, DryRunDisks.Disk.Number), _paths);

    // One start of the unit. The staged agent.json has to name the server.
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        if (!AgentOptions.TryParse(["--config", _paths.Configuration], out AgentOptions? staged, out string error) || staged is null)
        {
            log.Error($"Dry run: the agent in the installed Linux cannot start: {error}");

            return AgentExitCodes.ConfigurationError;
        }

        log.Information($"Dry run: the installed Linux starts the agent's unit, which here goes on in this process with {_paths.Configuration}.");
        new SeedTokenRepair(_seed, log).Run();
        IAgentServer server = connect(staged);

        try
        {
            return await Loop(server).RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            (server as IDisposable)?.Dispose();
        }
    }

    private InstalledPhaseLoop Loop(IAgentServer server)
    {
        DryRunToolRunner tools = new(log);

        return new LinuxPhaseLoopBuilder
        {
            Server = server,
            IdentityReader = new DryRunMachineIdentityReader(options.Agent.DryRunId, options.Agent.DryRunSecureBoot),
            Paths = _paths,
            Tools = tools,
            Scripts = new ShellScriptLauncher(dryRun: true),
            Shares = new LinuxShareConnector(new DryRunMounts(log), _paths.Shares, log) { Resolve = NotLookedUp },
            Seeds = _seed,
            CloudInit = new DryRunCloudInit(log),
            RestartMarker = new DryRunLinuxRestartMarker(log),
            FreeSpace = FreeSpace,
            Log = log,
            TimeProvider = timeProvider,
            AgentVersion = options.AgentVersion,
            HeartbeatInterval = options.HeartbeatInterval,
            DryRun = true,
            Removal = new DryRunAgentRemoval(new LinuxAgentRemoval(_paths, _seed, tools, log), options.Root, log),
        }.Build();
    }

    // A dry run reaches no host of a step's shares, not even to ask for its address.
    private static Task<IPAddress[]> NotLookedUp(string host, CancellationToken cancellationToken) =>
        Task.FromResult<IPAddress[]>([IPAddress.Loopback]);
}
