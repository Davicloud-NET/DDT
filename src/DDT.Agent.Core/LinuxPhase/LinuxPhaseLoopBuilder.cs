// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.InstalledPhase;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent.LinuxPhase;

// Puts the loop of the installed Linux together, the same way for the unit, a dry run and the tests. What differs
// between them is what reaches the system: its tools, its mounts and cloud-init's files.
public sealed class LinuxPhaseLoopBuilder
{
    public required IAgentServer Server { get; init; }

    public required IMachineIdentityReader IdentityReader { get; init; }

    public required LinuxAgentPaths Paths { get; init; }

    // Runs the scripts, and systemctl for a restart and for the removal.
    public required IToolRunner Tools { get; init; }

    public required IScriptLauncher Scripts { get; init; }

    public required IShareConnector Shares { get; init; }

    public required IMountedSeeds Seeds { get; init; }

    public required ICloudInit CloudInit { get; init; }

    public required IRestartMarker RestartMarker { get; init; }

    public required IFreeSpace FreeSpace { get; init; }

    public required AgentLog Log { get; init; }

    public required TimeProvider TimeProvider { get; init; }

    public required string AgentVersion { get; init; }

    public TimeSpan HeartbeatInterval { get; init; } = RunHeartbeat.DefaultInterval;

    public bool DryRun { get; init; }

    // What removes the agent once the run is over. Null is the agent's own removal.
    public IAgentRemoval? Removal { get; init; }

    public InstalledPhaseLoop Build()
    {
        LinuxPhaseRebooter rebooter = new(Tools, Log);

        return new InstalledPhaseLoopBuilder
        {
            Server = Server,
            IdentityReader = IdentityReader,
            Runner = Runner(rebooter),
            Profile = LinuxPhaseProfile.With(new CloudInitWait(CloudInit, new FirstStartNote(CloudInit, Paths.FirstStart, Log), Paths.CloudInitWaited, Log, TimeProvider)),
            Rebooter = rebooter,
            RestartMarker = RestartMarker,
            Removal = Removal ?? new LinuxAgentRemoval(Paths, Seeds, Tools, Log),
            Log = Log,
            TimeProvider = TimeProvider,
            HeartbeatInterval = HeartbeatInterval,
            Root = Paths.State,
            AgentVersion = AgentVersion,
            DryRun = DryRun,
        }.Build();
    }

    // The run's files, scripts and packages are under the state folder, on a disk that starts programs.
    private SequenceRunner Runner(LinuxPhaseRebooter rebooter)
    {
        SequenceRunnerOptions options = new(HeartbeatInterval, Paths.State, DryRun) { Environment = AgentEnvironment.Linux };

        return new SequenceRunnerBuilder
        {
            Server = Server,
            Disks = new NoDisks(),
            FreeSpace = FreeSpace,
            PreflightChecks = [],
            StepRunners = new LinuxStepRunners { Tools = Tools, Scripts = Scripts, Log = Log, TimeProvider = TimeProvider, Options = options },
            BootOrder = new NoBootOrder(),
            Rebooter = rebooter,
            RestartMarker = new NoBootRestart(),
            HandOver = new NoHandOver(),
            Log = Log,
            TimeProvider = TimeProvider,
            Options = options,
            AccountTools = new AccountTools(Shares, new NoLogons()),
        }.Build();
    }
}
