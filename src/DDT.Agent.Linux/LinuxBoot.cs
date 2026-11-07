// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Facts;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// Puts the agent of the Linux boot environment together from the machine it runs on.
public static class LinuxBoot
{
    public const string AgentFileName = "ddt-agent";

    // On a file system in memory, which every start begins empty. The work directory holds a run's scripts.
    public const string RunDirectory = "/run/ddt";

    private const string Root = "/";

    public static string WorkDirectory => Path.Combine(RunDirectory, "work");

    // The running agent's own file, which a hand-over puts on the seed for the installed Linux.
    public static string AgentPath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AgentFileName);

    // A machine the firmware started from the network may have no adapter Linux has a driver for, or get it late.
    public static Task<bool> ReadyAsync(StartedAgent agent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);

        return new LinuxAdapters(Root).WaitAsync(agent.Log, TimeProvider.System, cancellationToken);
    }

    // The look before the update check, which opens the disks on its own as nothing is mounted yet. A server that is
    // away must keep the machine from its disk no longer than the question alone would.
    public static Task<TimeSpan?> UpdateCheckTimeAsync(HandedOverQuestionTime time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(time);

        AgentLog quiet = new(TimeProvider.System, TextWriter.Null);
        LinuxRawDisks rawDisks = new(Root, new KernelBlockDevices());
        HandedOverSeeds seeds = new(rawDisks, quiet, TimeProvider.System);

        return time.ForUpdateCheckAsync(new LinuxDisks(Root, rawDisks, quiet), seeds, cancellationToken);
    }

    // The machine restarts only once the agent has ended, so the server has the last lines of its log, and however it
    // ended: the server may have the run as done by then.
    public static async Task<int> RunAsync(Func<CancellationToken, Task<int>> agent, LinuxRestart restart, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(restart);

        try
        {
            return await agent(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            restart.NowIfIssued();
        }
    }

    public static Task<int> RunAsync(
        StartedAgent agent,
        LinuxRestart restart,
        HandedOverQuestionTime time,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(restart);
        ArgumentNullException.ThrowIfNull(time);

        return RunAsync(stop => StartAsync(agent, restart, time, stop), restart, cancellationToken);
    }

    // It first looks at the disks for a run that was handed over to the installed Linux, which a registration would
    // end, and only then registers. The look lists the disks without a line each, which the registration writes. Its
    // resume token crosses a restart into Windows PE in the firmware, and comes back the same way: to the look where
    // it registers, else to the loop.
    private static async Task<int> StartAsync(
        StartedAgent agent,
        LinuxRestart restart,
        HandedOverQuestionTime time,
        CancellationToken cancellationToken)
    {
        AgentLog log = agent.Log;
        LinuxUefiVariables firmware = new(Root);
        LinuxRawDisks rawDisks = new(Root, new KernelBlockDevices());
        LinuxDisks disks = new(Root, rawDisks, log);
        LinuxBootRebooter rebooter = new(firmware, restart, log);
        LinuxIdentityReader identity = new(
            new HardwareMachineIdentityReader(new LinuxFirmwareTables(Root), new LinuxSystemHardware(Root), firmware, new LinuxSecureBoot(firmware)),
            new LinuxAdapters(Root));
        ResumeCarrier carrier = new(firmware, AgentEnvironment.LinuxBoot, log);
        AgentMachine machine = new(identity, disks, new LocalRunLocator([]), agent.Version, AgentEnvironment.LinuxBoot);
        restart.Before = () => new LinuxMounts(Root, log).UnmountFrom(rawDisks.Opened);

        BootReturnOutcome returned = await new LinuxBootReturn(agent.Server, machine, agent.Status, log, TimeProvider.System)
        {
            Seeds = new HandedOverSeeds(rawDisks, log, TimeProvider.System),
            Disks = new LinuxDisks(Root, rawDisks, new AgentLog(TimeProvider.System, TextWriter.Null)),
            StartOver = new StartOverQuestion(
                new ConsoleKeyWait(TimeProvider.System),
                new ConsoleSignInPrompt(log, TimeProvider.System, keyboardLayout: null),
                log,
                TimeProvider.System),
            DiskStart = new FirmwareDiskStart(firmware, log),
            Restart = rebooter.RestartIntoDiskAsync,
            Carried = carrier.Take,
            Time = time,
        }.RunAsync(cancellationToken).ConfigureAwait(false);

        if (returned.ExitCode is { } exitCode)
        {
            return exitCode;
        }

        machine = machine with { Switch = new EnvironmentSwitch(carrier, rebooter), ResumeToken = returned.ResumeToken };
        SequenceRunner runner = Runner(agent, disks, rawDisks, new LinuxBootOrder(firmware, log), rebooter);
        AgentLoop loop = new(agent.Server, machine, agent.Status, runner, log, TimeProvider.System) { GoesOn = () => rebooter.Withdraw(carrier) };

        return await loop.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static SequenceRunner Runner(StartedAgent agent, LinuxDisks disks, LinuxRawDisks rawDisks, LinuxBootOrder bootOrder, LinuxBootRebooter rebooter)
    {
        AgentLog log = agent.Log;
        SequenceRunnerOptions options = new(RunHeartbeat.DefaultInterval, WorkDirectory, DryRun: false) { Environment = AgentEnvironment.LinuxBoot };

        return new SequenceRunnerBuilder
        {
            Server = agent.Server,
            Disks = disks,
            RawDisks = rawDisks,
            FreeSpace = new LinuxFreeSpace(),
            PreflightChecks = [new BootEnvironmentCheck(AgentEnvironment.LinuxBoot)],
            StepRunners = new LinuxBootStepRunners
            {
                Disks = disks,
                RawDisks = rawDisks,
                Tools = new ToolRunner(log, TimeProvider.System, Latin1Output.Decode)
                {
                    Strays = new LinuxStrayProcesses(Root, log, Environment.ProcessId) { Adopts = LinuxStrayProcesses.Adopt() },
                },
                Scripts = new ShellScriptLauncher { Environment = LinuxShell.Environment(Environment.GetEnvironmentVariable) },
                Log = log,
                TimeProvider = TimeProvider.System,
                Options = options,
            },
            BootOrder = bootOrder,
            Rebooter = rebooter,
            RestartMarker = new FileRestartMarker(RunDirectory, log, dryRun: false),
            HandOver = new LinuxHandOver(rawDisks, AgentPath, agent.Options.ForInstalledSystem(), log)
            {
                LetGo = () => new LinuxMounts(Root, log).UnmountFrom(rawDisks.Opened, LinuxMounts.TheHandOver),
                ServerAddress = () => agent.Server.Lookup.ReachedAt,
            },
            Log = log,
            TimeProvider = TimeProvider.System,
            Options = options,
            Status = agent.Status,
            AccountTools = new AccountTools(new NoShares(), new NoLogons()),
        }.Build();
    }
}
