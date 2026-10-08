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

    // Its resume token crosses a restart into Windows PE in the firmware, and comes back the same way.
    public static AgentLoop Loop(StartedAgent agent, LinuxRestart restart)
    {
        ArgumentNullException.ThrowIfNull(agent);

        AgentLog log = agent.Log;
        LinuxUefiVariables firmware = new(Root);
        LinuxRawDisks rawDisks = new(Root, new KernelBlockDevices());
        LinuxDisks disks = new(Root, rawDisks, log);
        LinuxBootRebooter rebooter = new(firmware, restart, log);
        HardwareMachineIdentityReader identity = new(new LinuxFirmwareTables(Root), new LinuxSystemHardware(Root), firmware, new LinuxSecureBoot(firmware));
        AgentMachine machine = new(identity, disks, new LocalRunLocator([]), agent.Version, AgentEnvironment.LinuxBoot)
        {
            Switch = new EnvironmentSwitch(new ResumeCarrier(firmware, AgentEnvironment.LinuxBoot, log), rebooter),
        };

        SequenceRunner runner = Runner(agent, disks, rawDisks, new LinuxBootOrder(firmware, log), rebooter);

        return new AgentLoop(agent.Server, machine, agent.Status, runner, log, TimeProvider.System);
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
                Tools = new ToolRunner(log, TimeProvider.System, Latin1Output.Decode),
                Scripts = new ShellScriptLauncher { Environment = LinuxShell.Environment(Environment.GetEnvironmentVariable) },
                Log = log,
                TimeProvider = TimeProvider.System,
                Options = options,
            },
            BootOrder = bootOrder,
            Rebooter = rebooter,
            RestartMarker = new FileRestartMarker(RunDirectory, log, dryRun: false),
            HandOver = new NoHandOver(),
            Log = log,
            TimeProvider = TimeProvider.System,
            Options = options,
            Status = agent.Status,
            AccountTools = new AccountTools(new NoShares(), new NoLogons()),
        }.Build();
    }
}
