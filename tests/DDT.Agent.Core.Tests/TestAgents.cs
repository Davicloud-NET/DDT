// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

internal static class TestAgents
{
    public const string Version = "1.0.0-test";

    public static AgentConfiguration Configuration { get; } =
        new("https://ddt.example:8443/", "-----BEGIN CERTIFICATE-----\nMIIB\n-----END CERTIFICATE-----\n", null);

    // A runner with the parts every system has. Its steps are the Linux boot environment's, which need nothing of one
    // system. Unless the options name an environment, it checks nothing of a system before a run. Unless they bring a
    // hand-over, its own only goes into the tools' journal. Everything on the disk is under tools.Root. The restart
    // marker is a real file.
    public static SequenceRunner Runner(
        IAgentServer server,
        FakeDeploymentTools tools,
        AgentLog log,
        TimeProvider timeProvider,
        TestRunnerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tools);

        options ??= new TestRunnerOptions();
        IRawDisks rawDisks = options.RawDisks ?? new MemoryRawDisks();
        IDisks disks = options.Disks ?? tools;
        SequenceRunnerOptions runnerOptions = new(options.HeartbeatInterval ?? Timeout.InfiniteTimeSpan, Path.Combine(tools.Root, "X"), options.DryRun)
        {
            Environment = options.Environment ?? AgentEnvironment.WindowsPE,
        };

        return new SequenceRunnerBuilder
        {
            Server = server,
            Disks = disks,
            Partitioner = tools,
            RawDisks = rawDisks,
            FreeSpace = new FakeFreeSpace(),
            PreflightChecks = options.Environment is { } environment ? [new BootEnvironmentCheck(environment)] : [],
            StepRunners = new LinuxBootStepRunners
            {
                Disks = disks,
                RawDisks = rawDisks,
                Tools = options.ToolRunner ?? new RecordingToolRunner(),
                Scripts = new ShellScriptLauncher(dryRun: true) { Environment = options.ScriptEnvironment },
                Log = log,
                TimeProvider = timeProvider,
                Options = runnerOptions,
            },
            // The Linux boot environment applies no Windows, so its runner gets no writer for one.
            BcdWriter = options.BcdWriter ?? (options.Environment == AgentEnvironment.LinuxBoot ? null : tools),
            BootOrder = tools,
            Rebooter = options.Rebooter ?? tools,
            RestartMarker = RestartMarker(tools, log),
            HandOver = options.HandOver ?? new FakeHandOver(tools),
            Log = log,
            TimeProvider = timeProvider,
            Options = runnerOptions,
            Status = options.Status,
            AccountTools = new AccountTools(new NoShareConnector(), new DryRunAccountLogons(log)),
        }.Build();
    }

    // The text console, asking through prompt.
    public static ConsoleStatus Status(ISignInPrompt prompt, AgentLog log) => Status(new TextMachineConsole(prompt, log));

    public static ConsoleStatus Status(IMachineConsole console) =>
        new(console, Version, new Uri(Configuration.ServerUrl!), "German (Germany)", dryRun: true);

    // WinPE keeps it in its own directory. Here it gets a separate one, because the tests' dry runs delete X when they
    // end and would delete the marker with it.
    public static FileRestartMarker RestartMarker(FakeDeploymentTools tools, AgentLog log)
    {
        ArgumentNullException.ThrowIfNull(tools);

        return new FileRestartMarker(Path.Combine(tools.Root, "RAM disk"), log, dryRun: false);
    }

    // With the text console, asking through prompt.
    public static AgentLoop Loop(IAgentServer server, ISignInPrompt prompt, TestMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        return Loop(server, Status(prompt, machine.Log), machine);
    }

    // Unless the machine brings a runner, the default one keeps status up to date too.
    public static AgentLoop Loop(IAgentServer server, ConsoleStatus status, TestMachine machine)
    {
        ArgumentNullException.ThrowIfNull(machine);

        (FakeDeploymentTools tools, AgentLog log, TimeProvider time) = machine;

        return new(
            server,
            new AgentMachine(machine.Identity ?? new DryRunMachineIdentityReader(1), tools, new LocalRunLocator([tools.Volumes.Windows]), Version, machine.Environment)
            {
                Switch = machine.Switch,
            },
            status,
            machine.Runner ?? Runner(server, tools, log, time, new TestRunnerOptions { Status = status }),
            log,
            time)
        {
            GoesOn = machine.GoesOn,
        };
    }
}
